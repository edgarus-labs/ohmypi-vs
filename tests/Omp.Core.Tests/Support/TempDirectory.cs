namespace Omp.Core.Tests.Support;

/// <summary>Per-test scratch directories under the system temp directory.</summary>
public static class TempDirectory
{
    /// <summary>
    /// Creates a unique directory in the system&apos;s temporary path using the specified prefix and returns the full path to the created directory.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The string result.</returns>
    public static string Create(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        return dir;
    }

    /// <summary>
    /// Deletes <paramref name="dir"/>, retrying while a dying child process, the indexer or an antivirus scan still holds
    /// a file in it. A directory that stays locked is left behind: the next test run gets a fresh one, and teardown must
    /// not fail a test over it.
    /// </summary>
    public static async Task DeleteAsync(string dir, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }

                return;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                if (DateTime.UtcNow > deadline)
                {
                    Console.Error.WriteLine($"Left {dir} behind: {error.Message}");

                    return;
                }
            }
            await Task.Delay(50);
        }
    }
}
