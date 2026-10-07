using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OhMyPi.VisualStudio.Logic;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Notifications as one-line info bars in the main window, with action links. A message already on screen is not
/// shown twice, error bars close once OMP is ready, and every bar gives up its choice when the package is disposed.
/// </summary>
internal sealed class InfoBarNotifier : IServiceNotifier
{
    private readonly AsyncPackage _package;
    private readonly IOmpLogger _logger;
    private readonly Action _showLog;
    private readonly Action _openSettings;
    private readonly OpenNotifications _open = new OpenNotifications();
    /// <summary>Error bars on screen; UI thread only.</summary>
    private readonly List<IVsInfoBarUIElement> _errorBars = new List<IVsInfoBarUIElement>();

    public InfoBarNotifier(AsyncPackage package, IOmpLogger logger, Action showLog, Action openSettings)
    {
        _package = package;
        _logger = logger;
        _showLog = showLog;
        _openSettings = openSettings;
    }

    public void ShowLog() => _showLog();

    public void OpenSettings() => _openSettings();

    public Task<string?> ShowErrorAsync(string message, params string[] actions) =>
        _open.ShowOnceAsync(message, () => ShowNewAsync(message, KnownMonikers.StatusError, error: true, actions));

    /// <summary>Shows an info bar; completes with the clicked action, or null when it is closed.</summary>
    public Task<string?> ShowAsync(string message, ImageMoniker icon, params string[] actions) =>
        _open.ShowOnceAsync(message, () => ShowNewAsync(message, icon, error: false, actions));

    public void CloseErrors() => Background.Run(_package.JoinableTaskFactory, _logger, "Closing oh-my-pi error notifications", async () =>
                                      {
                                          await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                                          foreach (var bar in _errorBars.ToArray())
                                          {
                                              bar.Close();
                                          }
                                      });

    private async Task<string?> ShowNewAsync(string message, ImageMoniker icon, bool error, string[] actions)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var shell = await _package.GetServiceAsync<SVsShell, IVsShell>();
        var factory = await _package.GetServiceAsync<SVsInfoBarUIFactory, IVsInfoBarUIFactory>();
        ErrorHandler.ThrowOnFailure(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject));
        var host = (IVsInfoBarHost)hostObject;
        var summary = NotificationText.Summary(message);
        if (summary != message)
        {
            _logger.Info($"Notification: {message}");
        }

        var model = new InfoBarModel(
            new[] { new InfoBarTextSpan(summary) },
            actions.Select(action => new InfoBarHyperlink(action, action)).ToArray(),
            icon,
            isCloseButtonVisible: true);
        var element = factory.CreateInfoBar(model);
        var events = new Events();
        element.Advise(events, out var cookie);
        host.AddInfoBar(element);
        if (error)
        {
            _errorBars.Add(element);
        }

        string? choice;
        using (_package.DisposalToken.Register(() => events.Choice.TrySetResult(null)))
        {
#pragma warning disable VSTHRD003 // Completed by this info bar's own UI events or by package disposal; awaiting it cannot deadlock.
            choice = await events.Choice.Task;
#pragma warning restore VSTHRD003
        }
        if (_package.DisposalToken.IsCancellationRequested)
        {
            return choice;
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
        _errorBars.Remove(element);
        element.Unadvise(cookie);

        return choice;
    }

    private sealed class Events : IVsInfoBarUIEvents
    {
        public readonly TaskCompletionSource<string?> Choice = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement) => Choice.TrySetResult(null);

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Choice.TrySetResult(actionItem.ActionContext as string);
            infoBarUIElement.Close();
        }
    }
}
