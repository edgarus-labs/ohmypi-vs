using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Omp.Core;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio
{
    internal static class Background
    {
        /// <summary>Runs <paramref name="work"/> as a joinable task; a failure is logged, never thrown at the caller.</summary>
        public static void Run(JoinableTaskFactory joinableTaskFactory, IOmpLogger? logger, string what, Func<Task> work)
        {
            _ = joinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await work();
                }
                catch (Exception error)
                {
                    if (logger != null) logger.Error($"{what} failed", error);
                    else ActivityLog.TryLogError("oh-my-pi", $"{what} failed: {error}");
                }
            });
        }
    }
}
