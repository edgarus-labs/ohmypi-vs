namespace Omp.Core.Tests.Support;

public static class Wait
{
    public static async Task For(Func<bool> predicate, int timeoutMs = 5000, string label = "condition")
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {label}");
            await Task.Delay(10);
        }
    }

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
