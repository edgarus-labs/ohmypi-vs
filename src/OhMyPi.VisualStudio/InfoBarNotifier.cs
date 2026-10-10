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

    /// <summary>
    /// Initializes a new instance of the InfoBarNotifier class with the specified package, logger, and action delegates for displaying logs and opening settings.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="showLog">The show log.</param>
    /// <param name="openSettings">The open settings.</param>
    public InfoBarNotifier(AsyncPackage package, IOmpLogger logger, Action showLog, Action openSettings)
    {
        _package = package;
        _logger = logger;
        _showLog = showLog;
        _openSettings = openSettings;
    }

    /// <summary>
    /// Displays the application log output to the user.
    /// </summary>
    public void ShowLog() => _showLog();

    /// <summary>
    /// Opens the application settings configuration interface.
    /// </summary>
    public void OpenSettings() => _openSettings();

    /// <summary>
    /// Asynchronously displays an error message to the user with a set of optional action buttons.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="actions">The collection of actions.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string?.</returns>
    public Task<string?> ShowErrorAsync(string message, params string[] actions) =>
        _open.ShowOnceAsync(message, () => ShowNewAsync(message, KnownMonikers.StatusError, error: true, actions));

    /// <summary>Shows an info bar; completes with the clicked action, or null when it is closed.</summary>
    public Task<string?> ShowAsync(string message, ImageMoniker icon, params string[] actions) =>
        _open.ShowOnceAsync(message, () => ShowNewAsync(message, icon, error: false, actions));

    /// <summary>
    /// Asynchronously closes all active error notification bars on the main thread.
    /// </summary>
    public void CloseErrors() => Background.Run(_package.JoinableTaskFactory, _logger, "Closing oh-my-pi error notifications", async () =>
                                      {
                                          await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                                          foreach (var bar in _errorBars.ToArray())
                                          {
                                              bar.Close();
                                          }
                                      });

    /// <summary>
    /// Asynchronously displays an info bar notification in the Visual Studio shell and returns the user&apos;s selected action.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="error">The error.</param>
    /// <param name="actions">The collection of actions.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string?.</returns>
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

    /// <summary>
    /// Represents an internal implementation of the info bar user interface events handler for managing user interactions such as closures and action item clicks.
    /// </summary>
    private sealed class Events : IVsInfoBarUIEvents
    {
        /// <summary>
        /// The choice.
        /// </summary>
        public readonly TaskCompletionSource<string?> Choice = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Handles the event when the specified info bar UI element is closed by completing the associated choice operation with a null result.
        /// </summary>
        /// <param name="infoBarUIElement">The info bar uielement.</param>
        public void OnClosed(IVsInfoBarUIElement infoBarUIElement) => Choice.TrySetResult(null);

        /// <summary>
        /// Handles the click event of an info bar action item by capturing the action context as a result and closing the associated info bar element.
        /// </summary>
        /// <param name="infoBarUIElement">The info bar uielement.</param>
        /// <param name="actionItem">The action item.</param>
        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Choice.TrySetResult(actionItem.ActionContext as string);
            infoBarUIElement.Close();
        }
    }
}
