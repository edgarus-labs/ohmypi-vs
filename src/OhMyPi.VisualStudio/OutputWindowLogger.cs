using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic;
using Omp.Core;
using System;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Writes to the "oh-my-pi" Output window pane, filtered by the Log level option; tracing follows
/// the Trace RPC option. Both are read on every use. Callers pass already-redacted text only.
/// </summary>
internal sealed class OutputWindowLogger : IOmpLogger
{
    private readonly IVsOutputWindowPane _pane;
    private readonly OmpOptionsPage _options;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly PendingText _pending = new PendingText();

    private OutputWindowLogger(IVsOutputWindowPane pane, OmpOptionsPage options, JoinableTaskFactory joinableTaskFactory)
    {
        _pane = pane;
        _options = options;
        _joinableTaskFactory = joinableTaskFactory;
    }

    public static async Task<OutputWindowLogger> CreateAsync(AsyncPackage package, OmpOptionsPage options)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var output = await package.GetServiceAsync<SVsOutputWindow, IVsOutputWindow>();
        var guid = PackageGuids.OutputPane;
        ErrorHandler.ThrowOnFailure(output.CreatePane(ref guid, "oh-my-pi", 1, 0));
        ErrorHandler.ThrowOnFailure(output.GetPane(ref guid, out var pane));

        return new OutputWindowLogger(pane, options, package.JoinableTaskFactory);
    }

    public bool TraceEnabled => _options.TraceRpc;

    public void Error(string message, Exception? error = null) => Write(OmpLogLevel.Error, message, error);

    public void Warn(string message, Exception? error = null) => Write(OmpLogLevel.Warn, message, error);

    public void Info(string message, Exception? error = null) => Write(OmpLogLevel.Info, message, error);

    public void Debug(string message, Exception? error = null) => Write(OmpLogLevel.Debug, message, error);

    public void Trace(string direction, string frame) => Append(LogFormat.Trace(DateTime.UtcNow, direction, frame));

    /// <summary>Reveals the pane in the Output window without taking focus from the editor.</summary>
    public void Show() => Background.Run(_joinableTaskFactory, null, "Showing the oh-my-pi log", async () =>
                               {
                                   await _joinableTaskFactory.SwitchToMainThreadAsync();
                                   if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell)
                                   {
                                       var outputWindow = new Guid(ToolWindowGuids80.Outputwindow);
                                       if (ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref outputWindow, out var frame)) && frame is not null)
                                       {
                                           frame.ShowNoActivate();
                                       }
                                   }
                                   _pane.Activate();
                               });

    private void Write(OmpLogLevel level, string message, Exception? error)
    {
        if (!LogFormat.Allows(_options.LogLevel, level))
        {
            return;
        }

        Append(LogFormat.Line(DateTime.UtcNow, level, message, error));
    }

    private void Append(string line)
    {
        if (!_pending.Append(line))
        {
            return;
        }

        _ = _joinableTaskFactory.RunAsync(async () =>
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
            _pane.OutputStringThreadSafe(_pending.Drain());
        });
    }
}
