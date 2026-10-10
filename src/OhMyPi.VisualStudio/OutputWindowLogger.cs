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

    /// <summary>
    /// Initializes a new instance of the OutputWindowLogger class with the specified output window pane, options page, and joinable task factory.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="options">The options.</param>
    /// <param name="joinableTaskFactory">The joinable task factory.</param>
    private OutputWindowLogger(IVsOutputWindowPane pane, OmpOptionsPage options, JoinableTaskFactory joinableTaskFactory)
    {
        _pane = pane;
        _options = options;
        _joinableTaskFactory = joinableTaskFactory;
    }

    /// <summary>
    /// Asynchronously creates and initializes an OutputWindowLogger by registering a custom output pane within the Visual Studio output window.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="options">The options.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the output window logger.</returns>
    public static async Task<OutputWindowLogger> CreateAsync(AsyncPackage package, OmpOptionsPage options)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var output = await package.GetServiceAsync<SVsOutputWindow, IVsOutputWindow>();
        var guid = PackageGuids.OutputPane;
        ErrorHandler.ThrowOnFailure(output.CreatePane(ref guid, "oh-my-pi", 1, 0));
        ErrorHandler.ThrowOnFailure(output.GetPane(ref guid, out var pane));

        return new OutputWindowLogger(pane, options, package.JoinableTaskFactory);
    }

    /// <summary>
    /// Gets a value indicating whether trace enabled.
    /// </summary>
    public bool TraceEnabled => _options.TraceRpc;

    /// <summary>
    /// Logs an error message and an optional exception to the output with an error log level.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Error(string message, Exception? error = null) => Write(OmpLogLevel.Error, message, error);

    /// <summary>
    /// Logs a warning message and an optional exception to the system output.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Warn(string message, Exception? error = null) => Write(OmpLogLevel.Warn, message, error);

    /// <summary>
    /// Logs an informational message and an optional exception to the system output.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Info(string message, Exception? error = null) => Write(OmpLogLevel.Info, message, error);

    /// <summary>
    /// Writes a debug-level log message and an optional exception to the log output.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Debug(string message, Exception? error = null) => Write(OmpLogLevel.Debug, message, error);

    /// <summary>
    /// Appends a formatted trace log entry containing the specified direction and frame information to the log.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <param name="frame">The frame.</param>
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

    /// <summary>
    /// Writes a formatted log entry to the output if the specified log level meets the configured minimum threshold.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    private void Write(OmpLogLevel level, string message, Exception? error)
    {
        if (!LogFormat.Allows(_options.LogLevel, level))
        {
            return;
        }

        Append(LogFormat.Line(DateTime.UtcNow, level, message, error));
    }

    /// <summary>
    /// Appends a line of text to the pending buffer and schedules its output to the pane on the main thread.
    /// </summary>
    /// <param name="line">The line.</param>
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
