namespace Omp.Core.Tests.Support;

/// <summary>
/// Provides utility methods for managing and synchronizing asynchronous wait operations.
/// </summary>
public static class Wait
{
    /// <summary>
    /// Asynchronously polls a predicate function until it returns true or the specified timeout duration is exceeded.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="timeoutMs">The timeout ms.</param>
    /// <param name="label">The label.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="TimeoutException">Thrown when an error occurs during execution.</exception>
    public static async Task For(Func<bool> predicate, int timeoutMs = 5000, string label = "condition")
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {label}");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Asynchronously awaits the completion of the specified task and returns any exception encountered during its execution, or null if the task completed successfully.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the exception?.</returns>
    public static async Task<Exception?> Settle(Task task)
    {
        try
        {
            await task;

            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }
}
