using Omp.Core.Processes;
using System.Diagnostics;
using System.Text.RegularExpressions;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
namespace Omp.Core.Tests.Support;

/// <summary>
/// The fake OMP (<c>Fixtures/fake-omp.mjs</c>) run by Node through a <c>.cmd</c> launcher, shaped like the
/// <c>omp.cmd</c> npm installs. Every Node process is capped with <c>--max-old-space-size=1024</c>, including the
/// grandchildren fake-omp spawns (via <c>NODE_OPTIONS</c>).
/// </summary>
internal static class FakeOmp
{
    /// <summary>
    /// The node memory cap.
    /// </summary>
    public const string NodeMemoryCap = "--max-old-space-size=1024";

    /// <summary>
    /// The script.
    /// </summary>
    public static readonly string Script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-omp.mjs");

    /// <summary>
    /// The node.
    /// </summary>
    public static readonly string Node = FindNode();

    /// <summary>Environment overrides every fake-omp process gets.</summary>
    public static Dictionary<string, string?> Environment(string sessionDir, string? logFile = null, IDictionary<string, string>? extra = null)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["NODE_OPTIONS"] = NodeMemoryCap,
            ["FAKE_OMP_SESSION_DIR"] = sessionDir,
        };
        if (logFile is not null)
        {
            env["FAKE_OMP_LOG"] = logFile;
        }

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                env[pair.Key] = pair.Value;
            }
        }

        return env;
    }

    /// <summary>Write <c>name.cmd</c> into <paramref name="dir"/> running <paramref name="script"/> with Node.</summary>
    public static string WriteLauncher(string dir, string name, string script)
    {
        var file = Path.Combine(dir, name + ".cmd");
        File.WriteAllText(file, $"@\"{Node}\" {NodeMemoryCap} \"{script}\" %*\r\n");

        return file;
    }

    /// <summary>
    /// Determines whether a process with the specified process identifier is currently running.
    /// </summary>
    /// <param name="pid">The unique identifier of the p.</param>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    public static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Pids fake-omp reported on stderr (its own and its spawned child).</summary>
    public static IReadOnlyList<int> ReportedPids(MemoryLogger logger) =>
        Regex.Matches(logger.Text("debug"), @"fake-omp (?:started pid|child pid) (\d+)").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToArray();

    /// <summary>Last resort for a failed test: no Node process may outlive it.</summary>
    public static void KillLeftovers(MemoryLogger logger)
    {
        foreach (var pid in ReportedPids(logger))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase))
                {
                    KillTree(process);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    /// <summary>Ends <paramref name="process"/> and every descendant.</summary>
    public static void KillTree(Process process) => ProcessTree.Kill(process.Id, new MemoryLogger());

    /// <summary>
    /// Searches the system PATH environment variable to locate and return the full file path to the node.exe executable.
    /// </summary>
    /// <returns>The string result.</returns>
    /// <exception cref="FileNotFoundException">Thrown when an error occurs during execution.</exception>
    private static string FindNode()
    {
        foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split([';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "node.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("node.exe is not on PATH; the fake OMP tests need Node.js");
    }
}

/// <summary>
/// Samples the private memory of every Node process fake-omp reported while a test runs; one above the cap is
/// killed on the spot and the test fails, naming it.
/// </summary>
internal sealed class NodeMemoryGuard
{
    /// <summary>
    /// The cap bytes.
    /// </summary>
    public const long CapBytes = 1536L * 1024 * 1024;
    private readonly Func<IEnumerable<MemoryLogger>> _loggers;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private readonly List<string> _violations = new();

    /// <summary>
    /// Initializes a new instance of the NodeMemoryGuard class with a specified logger provider and starts the asynchronous memory sampling process.
    /// </summary>
    /// <param name="loggers">The loggers.</param>
    public NodeMemoryGuard(Func<IEnumerable<MemoryLogger>> loggers)
    {
        _loggers = loggers;
        _sampling = Task.Run(SampleAsync);
    }

    /// <summary>Stops sampling and fails when a Node process exceeded the cap.</summary>
    public async Task DisposeAsync()
    {
        _stop.Cancel();
        await _sampling;
        lock (_violations)
        {
            Assert.True(_violations.Count == 0, string.Join("\n", _violations));
        }
    }

    /// <summary>
    /// Asynchronously monitors reported process identifiers and terminates any process tree that exceeds the specified memory capacity threshold.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            foreach (var pid in _loggers().ToArray().SelectMany(FakeOmp.ReportedPids).Distinct())
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.HasExited || process.PrivateMemorySize64 <= CapBytes)
                    {
                        continue;
                    }

                    lock (_violations)
                    {
                        _violations.Add($"node {pid} used {process.PrivateMemorySize64 / (1024 * 1024)} MB; killed");
                    }

                    FakeOmp.KillTree(process);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
            try
            {
                await Task.Delay(100, _stop.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
