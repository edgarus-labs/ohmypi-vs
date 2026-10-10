using System;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Provides utility methods for managing asynchronous waiting operations.
/// </summary>
internal static class Waiting
{
    /// <summary>Waits for <paramref name="task"/> up to <paramref name="timeout"/>; false when the time ran out first.</summary>
    public static async Task<bool> WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var delay = Task.Delay(timeout, linked.Token);
#pragma warning disable VSTHRD003 // The task is completed by a DTE event handler that runs on the UI thread this method resumes on, never by work that needs it.
            var finished = await Task.WhenAny(task, delay);
#pragma warning restore VSTHRD003
            var cancelled = cancellationToken.IsCancellationRequested;
            linked.Cancel();
            if (cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return finished == task;
        }
    }
}
