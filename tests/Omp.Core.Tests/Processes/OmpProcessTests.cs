using Newtonsoft.Json.Linq;
using Omp.Core.Processes;
using Omp.Core.Protocol;
using Omp.Core.Tests.Support;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Omp.Core.Tests.Processes;

public sealed class OmpProcessTests : IAsyncLifetime
{
    private readonly string _dir = TempDirectory.Create("omp-proc-");
    private readonly List<(OmpProcess Process, MemoryLogger Logger)> _started = new();
    private readonly NodeMemoryGuard _memory;

    public OmpProcessTests()
    {
        _memory = new NodeMemoryGuard(() => { lock (_started) { return _started.Select(s => s.Logger).ToArray(); } });
    }

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        foreach (var (process, logger) in _started)
        {
            await Wait.Settle(process.ShutdownAsync(500));
            FakeOmp.KillLeftovers(logger);
        }
        await _memory.DisposeAsync();
        await TempDirectory.DeleteAsync(_dir);
    }

    private sealed class Spawned
    {
        public required OmpProcess Process { get; init; }

        public required MemoryLogger Logger { get; init; }

        public List<TransportClose> Closes { get; } = new();

        public List<byte> Stdout { get; } = new();

        public string StdoutText()
        {
            lock (Stdout)
            {
                return Encoding.UTF8.GetString([.. Stdout]);
            }
        }
    }

    private Spawned Spawn(string executable, IReadOnlyList<string> args, IDictionary<string, string>? env = null, bool start = true, bool useJob = true, ProcessTree.INative? tree = null, IProcessNative? native = null)
    {
        var logger = new MemoryLogger();
        var process = new OmpProcess(new OmpProcessOptions
        {
            Executable = executable,
            Args = args,
            Cwd = _dir,
            Environment = FakeOmp.Environment(_dir, extra: env),
            Logger = logger,
            UseJobObject = useJob,
            TreeNative = tree,
            Native = native,
        });
        var spawned = new Spawned { Process = process, Logger = logger };
        process.Data += chunk => { lock (spawned.Stdout) { spawned.Stdout.AddRange(chunk); } };
        process.Closed += close => { lock (spawned.Closes) { spawned.Closes.Add(close); } };
        lock (_started)
        {
            _started.Add((process, logger));
        }

        if (start)
        {
            process.Start();
        }

        return spawned;
    }

    private Spawned SpawnFake(IDictionary<string, string>? env = null) =>
        Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), new[] { "--profile", "x" }, env);

    /// <summary>An OMP stand-in running <paramref name="source"/> with Node, launched like an installed launcher.</summary>
    private Spawned SpawnScript(string source)
    {
        var script = Path.Combine(_dir, "script.js");
        File.WriteAllText(script, source);

        return Spawn(FakeOmp.WriteLauncher(_dir, "script-omp", script), Array.Empty<string>());
    }

    private static Task<int> GrandchildPidAsync(MemoryLogger logger) => ReportedPidAsync(logger, "child pid");

    private static Task<int> NodePidAsync(MemoryLogger logger) => ReportedPidAsync(logger, "started pid");

    /// <summary>fake-omp writes <c>child pid</c> before <c>started pid</c>, and stderr is read apart from stdout, so each line is awaited on its own.</summary>
    private static async Task<int> ReportedPidAsync(MemoryLogger logger, string label)
    {
        var pattern = $@"fake-omp {label} (\d+)";
        await Wait.For(() => Regex.IsMatch(logger.Text("debug"), pattern), 10000, $"fake-omp to report its {label}");

        return int.Parse(Regex.Match(logger.Text("debug"), pattern).Groups[1].Value);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetHandleInformation(SafeHandle handle, out uint flags);

    [Fact]
    public void CreatesPipesWhoseEndsAreNotInheritable()
    {
        OmpProcess.CreatePipe(out var read, out var write);
        using (read)
        using (write)
        {
            Assert.True(GetHandleInformation(read, out var readFlags));
            Assert.True(GetHandleInformation(write, out var writeFlags));
            Assert.Equal((0u, 0u), (readFlags & 1, writeFlags & 1));
        }
    }

    [Fact]
    public async Task StartsOmpWithoutTheCurrentDirectoryOnItsExecutableSearchPath()
    {
        var fake = SpawnScript("process.stderr.write(`search ${process.env.NoDefaultCurrentDirectoryInExePath}\\n`);");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        Assert.Contains("search 1", fake.Closes[0].Stderr);
    }

    [Fact]
    public async Task CapsAStderrLineThatNeverEnds()
    {
        var fake = SpawnScript("process.stderr.write(\"x\".repeat(100000)); process.stderr.write(\"\\nshort\\n\");");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        var lines = fake.Logger.Records.Where(r => r.Level == "debug" && r.Message.StartsWith("omp: ")).Select(r => r.Message).ToArray();
        Assert.Contains("omp: short", lines);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, 4010));
    }

    [Fact]
    public async Task EndsTheWholeTreeWhenThereIsNoJobObject()
    {
        var fake = Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), Array.Empty<string>(), new Dictionary<string, string> { ["FAKE_OMP_IGNORE_EOF"] = "1", ["FAKE_OMP_SPAWN_CHILD"] = "1" }, useJob: false);
        var grandchild = await GrandchildPidAsync(fake.Logger);
        var node = await NodePidAsync(fake.Logger);
        var pid = fake.Process.Pid!.Value;
        Assert.Null(typeof(OmpProcess).GetField("_job", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(fake.Process));
        await fake.Process.ShutdownAsync(300);
        await Wait.For(() => !FakeOmp.IsAlive(pid) && !FakeOmp.IsAlive(node) && !FakeOmp.IsAlive(grandchild), 5000, "process tree gone");
    }

    [Fact]
    public async Task SpawnsWithModeRpcUiPlusExtraArgsAndForwardsStdoutAndStderr()
    {
        var fake = SpawnFake();
        Assert.NotNull(fake.Process.Pid);
        await Wait.For(() => fake.StdoutText().Contains("\"type\":\"ready\""), 10000, "ready");
        await Wait.For(() => fake.Logger.Text("debug").Contains("fake-omp started"), 5000, "stderr");
        Assert.Contains("args [\"--mode\",\"rpc-ui\",\"--profile\",\"x\"]", fake.Logger.Text("debug"));
        Assert.DoesNotContain("job object", fake.Logger.Text("warn"));
    }

    [Fact]
    public async Task LaunchesAnInstalledLauncherWhosePathAndArgumentsContainSpacesAndShellMetacharacters()
    {
        var bin = Path.Combine(_dir, "my tools (x86) & more");
        Directory.CreateDirectory(bin);
        var launcher = FakeOmp.WriteLauncher(bin, "omp", FakeOmp.Script);
        var args = new[] { "--profile", "a b", "--name", "x&y|z \"q\" 100% ^c" };
        var fake = Spawn(launcher, args);
        await Wait.For(() => fake.Logger.Text("debug").Contains("fake-omp started") || fake.Closes.Count > 0, 10000, "start");
        var logged = Regex.Match(fake.Logger.Text("debug"), @"args (\[.*\])").Groups[1].Value;
        Assert.Equal(new[] { "--mode", "rpc-ui" }.Concat(args), JArray.Parse(logged).Select(a => (string)a!));
    }

    [Fact]
    public async Task WritesStdinAsUtf8WithoutABomAndReadsUtf8Back()
    {
        var fake = SpawnFake();
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        fake.Process.Write(new JObject { ["id"] = "p", ["type"] = "prompt", ["message"] = "zażółć 😀" }.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        await Wait.For(() => fake.StdoutText().Contains("prompt_result"), 10000, "prompt result");
        Assert.Contains("Echo: zażółć 😀", fake.StdoutText());
        Assert.DoesNotContain("Failed to parse command", fake.StdoutText());
    }

    [Fact]
    public async Task ReportsTheExitCodeProcessIdAndStderrTailWhenTheProcessDies()
    {
        var fake = SpawnFake();
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        fake.Process.Write("{\"id\":\"p\",\"type\":\"prompt\",\"message\":\"please crash\"}\n");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        Assert.Equal(3, fake.Closes[0].Code);
        Assert.Equal(fake.Process.Pid, fake.Closes[0].Pid);
        Assert.Contains("fake-omp started", fake.Closes[0].Stderr);
        Assert.Equal(new[] { 3 }, fake.Closes.Select(c => c.Code!.Value));
        Assert.Contains("not running", Assert.Throws<InvalidOperationException>(() => fake.Process.Write("{}\n")).Message);
    }

    [Fact]
    public async Task KeepsOnlyTheLast20StderrLinesIncludingAnUnterminatedOne()
    {
        var fake = SpawnScript("for (let i = 1; i <= 50; i++) process.stderr.write(`line ${i}\\n`); process.stderr.write(\"last words\"); process.exitCode = 1;");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        var expected = string.Join("\n", Enumerable.Range(32, 19).Select(i => $"line {i}").Append("last words"));
        Assert.Equal(expected, fake.Closes[0].Stderr);
        Assert.Equal(1, fake.Closes[0].Code);
    }

    [Fact]
    public async Task BoundsTheStderrTailToItsLast4000Characters()
    {
        var fake = SpawnScript("process.stderr.write(\"a\".repeat(3000) + \"\\n\" + \"b\".repeat(3000) + \"\\n\"); process.exitCode = 1;");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        var tail = fake.Closes[0].Stderr ?? "";
        Assert.Equal(4000, tail.Length);
        Assert.StartsWith("…", tail);
        Assert.EndsWith("a\n" + new string('b', 3000), tail);
    }

    [Fact]
    public async Task ReportsAWriteThePipeRejectsAfterTheProcessWentAway()
    {
        var fake = SpawnScript("");
        var errors = new List<Exception>();
        fake.Process.Write("{\"type\":\"prompt\",\"message\":\"" + new string('x', 4 * 1024 * 1024) + "\"}\n", error => { lock (errors) { errors.Add(error); } });
        await Wait.For(() => errors.Count > 0, 10000, "write error");
        Assert.IsAssignableFrom<IOException>(errors[0]);
    }

    [Fact]
    public async Task ShutsDownGracefullyViaStdinEofIdempotentlyLeavingNoProcessBehind()
    {
        var fake = SpawnFake();
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        var node = await NodePidAsync(fake.Logger);
        var pid = fake.Process.Pid!.Value;
        await Task.WhenAll(fake.Process.ShutdownAsync(3000), fake.Process.ShutdownAsync(3000));
        Assert.Single(fake.Closes);
        Assert.Equal(0, fake.Closes[0].Code);
        Assert.False(FakeOmp.IsAlive(pid));
        await Wait.For(() => !FakeOmp.IsAlive(node), 3000, "node gone");
        Assert.DoesNotContain("killing its process tree", fake.Logger.Text("info"));
    }

    [Fact]
    public async Task EscalatesPastAnIgnoredStdinEofAndKillsTheWholeProcessTree()
    {
        var fake = SpawnFake(new Dictionary<string, string> { ["FAKE_OMP_IGNORE_EOF"] = "1", ["FAKE_OMP_SPAWN_CHILD"] = "1" });
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        var grandchild = await GrandchildPidAsync(fake.Logger);
        var node = await NodePidAsync(fake.Logger);
        var pid = fake.Process.Pid!.Value;
        Assert.True(FakeOmp.IsAlive(grandchild));
        var watch = Stopwatch.StartNew();
        await fake.Process.ShutdownAsync(300);
        Assert.True(watch.ElapsedMilliseconds >= 290, $"waited through the EOF grace period ({watch.ElapsedMilliseconds} ms)");
        await Wait.For(() => !FakeOmp.IsAlive(pid) && !FakeOmp.IsAlive(node) && !FakeOmp.IsAlive(grandchild), 3000, "process tree gone");
        Assert.Contains("killing its process tree", fake.Logger.Text("info"));
    }

    /// <summary>
    /// Represents a native implementation for traversing a process tree to manage and retrieve information about process lifecycles and parent-child relationships.
    /// </summary>
    private sealed class BlindTreeWalk : ProcessTree.INative
    {
        /// <summary>
        /// Opens a process handle for the specified process identifier and outputs any resulting error code.
        /// </summary>
        /// <param name="pid">The unique identifier of the p.</param>
        /// <param name="error">The error.</param>
        /// <returns>The microsoft.win32.safe handles.safe process handle result.</returns>
        public Microsoft.Win32.SafeHandles.SafeProcessHandle Open(int pid, out int error)
        {
            error = 87;

            return new Microsoft.Win32.SafeHandles.SafeProcessHandle(IntPtr.Zero, false);
        }

        /// <summary>
        /// Retrieves the creation timestamp of the specified process.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="created">The created.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool GetCreated(Microsoft.Win32.SafeHandles.SafeProcessHandle process, out long created)
        {
            created = 0;

            return false;
        }

        /// <summary>
        /// Terminates the process associated with the specified safe process handle.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool Terminate(Microsoft.Win32.SafeHandles.SafeProcessHandle process) => false;

        /// <summary>
        /// Attempts to retrieve the exit code of the specified process handle.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="code">The code.</param>
        /// <returns>true if the condition is met; otherwise, false.</returns>
        public bool TryGetExitCode(Microsoft.Win32.SafeHandles.SafeProcessHandle process, out uint code)
        {
            code = 0;

            return false;
        }

        /// <summary>
        /// Retrieves a list of parent-child process identifier mappings.
        /// </summary>
        /// <returns>A collection of list items.</returns>
        public List<(int Pid, int ParentPid)> Parents() => new();
    }

    [Fact]
    public async Task ShutdownFailsAndSaysSoWhenTheProcessSurvivesEveryAttemptToEndItsTree()
    {
        var launcher = FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script);
        var fake = Spawn(launcher, Array.Empty<string>(), new Dictionary<string, string> { ["FAKE_OMP_IGNORE_EOF"] = "1" }, useJob: false, tree: new BlindTreeWalk());
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        var error = await Assert.ThrowsAsync<IOException>(() => fake.Process.ShutdownAsync(200));
        Assert.Contains("did not exit after its process tree was killed", error.Message);
        Assert.Contains("survived killing its process tree", fake.Logger.Text("warn"));
    }

    /// <summary>
    /// Represents a mock implementation of the IProcessNative interface used for testing failure scenarios by simulating flaky behavior across process lifecycle operations.
    /// </summary>
    private sealed class FlakyNative : IProcessNative
    {
        /// <summary>
        /// Gets or sets a value indicating whether job creation fails.
        /// </summary>
        public bool JobCreationFails { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether set limit fails.
        /// </summary>
        public bool SetLimitFails { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether assign fails.
        /// </summary>
        public bool AssignFails { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether resume fails.
        /// </summary>
        public bool ResumeFails { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether terminate fails.
        /// </summary>
        public bool TerminateFails { get; init; }

        /// <summary>
        /// Creates a new Windows job object handle via the native API, returning an empty handle if job creation is configured to fail.
        /// </summary>
        /// <returns>The safe job handle result.</returns>
        public SafeJobHandle CreateJob() => JobCreationFails ? new SafeJobHandle() : WindowsProcessNative.Instance.CreateJob();

        /// <summary>
        /// Configures the specified job handle to be terminated automatically when the process closes, provided that limit failures are not disabled.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool SetKillOnClose(SafeJobHandle job) => !SetLimitFails && WindowsProcessNative.Instance.SetKillOnClose(job);

        /// <summary>
        /// Assigns the specified process to the given job object using native Windows API calls.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="process">The process.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool AssignToJob(SafeJobHandle job, IntPtr process) => !AssignFails && WindowsProcessNative.Instance.AssignToJob(job, process);

        /// <summary>
        /// Resumes the execution of the specified thread using native Windows process operations.
        /// </summary>
        /// <param name="thread">The thread.</param>
        /// <returns>The int result.</returns>
        public int Resume(IntPtr thread) => ResumeFails ? -1 : WindowsProcessNative.Instance.Resume(thread);

        /// <summary>
        /// Terminates the specified job using the provided safe job handle and returns a value indicating whether the operation succeeded.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool TerminateJob(SafeJobHandle job) => !TerminateFails && WindowsProcessNative.Instance.TerminateJob(job);
    }

    [Theory]
    [InlineData(true, false, false, "Cannot create a job object for OMP")]
    [InlineData(false, true, false, "Cannot place OMP in a kill-on-close job object")]
    [InlineData(false, false, true, "Cannot place OMP in a kill-on-close job object")]
    public async Task OmpStillRunsWhenItsJobObjectCannotBeSetUpAndTheTreeIsWalkedInstead(bool create, bool limit, bool assign, string warning)
    {
        var native = new FlakyNative { JobCreationFails = create, SetLimitFails = limit, AssignFails = assign };
        var fake = Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), Array.Empty<string>(), native: native);
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        Assert.Contains(warning, fake.Logger.Text("warn"));
        await fake.Process.ShutdownAsync(3000);
        Assert.Single(fake.Closes);
    }

    [Fact]
    public async Task AProcessThatCannotBeResumedIsEndedAndReportedAsAFailedStart()
    {
        var fake = Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), Array.Empty<string>(), native: new FlakyNative { ResumeFails = true });
        await Wait.For(() => fake.Closes.Count > 0, 10000, "failed start");
        Assert.NotNull(fake.Closes[0].Error);
        Assert.Throws<InvalidOperationException>(() => fake.Process.Write("{}\n"));
    }

    [Fact]
    public async Task AJobThatCannotBeTerminatedFallsBackToWalkingTheTree()
    {
        var fake = Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), Array.Empty<string>(), new Dictionary<string, string> { ["FAKE_OMP_IGNORE_EOF"] = "1", ["FAKE_OMP_SPAWN_CHILD"] = "1" }, native: new FlakyNative { TerminateFails = true });
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        var pid = fake.Process.Pid!.Value;
        await fake.Process.ShutdownAsync(300);
        await Wait.For(() => !FakeOmp.IsAlive(pid), 3000, "process gone");
        Assert.Contains("Terminating OMP's job object failed", fake.Logger.Text("debug"));
    }

    [Fact]
    public async Task KillsLeftoverDescendantsWhenTheMainProcessExitsOnItsOwn()
    {
        var fake = SpawnFake(new Dictionary<string, string> { ["FAKE_OMP_SPAWN_CHILD"] = "1" });
        await Wait.For(() => fake.StdoutText().Contains("ready"), 10000, "ready");
        var grandchild = await GrandchildPidAsync(fake.Logger);
        fake.Process.Write("{\"id\":\"p\",\"type\":\"prompt\",\"message\":\"crash now\"}\n");
        await Wait.For(() => fake.Closes.Count > 0, 10000, "exit");
        await Wait.For(() => !FakeOmp.IsAlive(grandchild), 3000, "grandchild gone");
    }

    [Fact]
    public async Task ReportsASpawnFailureAsACloseWithAnError()
    {
        var missing = Path.Combine(_dir, "missing-omp.exe");
        var fake = Spawn(missing, Array.Empty<string>());
        await Wait.For(() => fake.Closes.Count > 0, 5000, "spawn failure");
        Assert.Contains("missing-omp.exe", fake.Closes[0].Error!.Message);
        Assert.Contains("Win32 error 2", fake.Closes[0].Error!.Message);
        Assert.Throws<InvalidOperationException>(() => fake.Process.Write("{}\n"));
        await fake.Process.ShutdownAsync(100);
    }

    [Fact]
    public async Task KillsTheWholeTreeWhenTheJobHandleIsReleasedAsWhenTheHostCrashes()
    {
        var fake = SpawnFake(new Dictionary<string, string> { ["FAKE_OMP_IGNORE_EOF"] = "1", ["FAKE_OMP_SPAWN_CHILD"] = "1" });
        var pids = new[] { fake.Process.Pid!.Value, await NodePidAsync(fake.Logger), await GrandchildPidAsync(fake.Logger) };
        Assert.All(pids, pid => Assert.True(FakeOmp.IsAlive(pid)));
        var job = (System.Runtime.InteropServices.SafeHandle?)typeof(OmpProcess)
            .GetField("_job", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(fake.Process);
        Assert.NotNull(job);
        job!.Dispose();
        await Wait.For(() => pids.All(pid => !FakeOmp.IsAlive(pid)), 5000, "process tree gone after the job handle closed");
    }

    [Fact]
    public async Task DoesNotLaunchAProcessWhoseShutdownWasRequestedBeforeStart()
    {
        var fake = Spawn(FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script), Array.Empty<string>(), start: false);
        await fake.Process.ShutdownAsync(100);
        fake.Process.Start();
        await Wait.For(() => fake.Closes.Count > 0, 5000, "close");
        Assert.Null(fake.Process.Pid);
        Assert.Contains("shut down before it started", fake.Closes[0].Error!.Message);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("fake-omp started", fake.Logger.Text("debug"));
    }
}
