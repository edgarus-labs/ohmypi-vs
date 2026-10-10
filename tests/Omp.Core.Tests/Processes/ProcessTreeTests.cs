using Microsoft.Win32.SafeHandles;
using Omp.Core.Processes;
using Omp.Core.Tests.Support;
using System.ComponentModel;

namespace Omp.Core.Tests.Processes;

public class ProcessTreeTests
{
    /// <summary>
    /// Provides a native implementation of the process tree operations for managing process lifecycles, tracking exit codes, and resolving parent-child process relationships.
    /// </summary>
    private sealed class Native : ProcessTree.INative
    {
        /// <summary>
        /// Gets the created.
        /// </summary>
        public Dictionary<int, long> Created { get; } = new();

        /// <summary>
        /// Gets the open errors.
        /// </summary>
        public Dictionary<int, int> OpenErrors { get; } = new();

        /// <summary>
        /// Gets the cannot read.
        /// </summary>
        public HashSet<int> CannotRead { get; } = new();

        /// <summary>
        /// Gets the cannot terminate.
        /// </summary>
        public HashSet<int> CannotTerminate { get; } = new();

        /// <summary>
        /// Gets the exit codes.
        /// </summary>
        public Dictionary<int, uint> ExitCodes { get; } = new();

        /// <summary>
        /// Gets or sets the processes.
        /// </summary>
        public Func<int, List<(int, int)>> Processes { get; set; } = _ => new();

        /// <summary>
        /// Gets the collection of terminated.
        /// </summary>
        public List<int> Terminated { get; } = new();

        /// <summary>
        /// Gets or sets the parent calls.
        /// </summary>
        public int ParentCalls { get; private set; }

        /// <summary>
        /// Opens a process handle for the specified process identifier and outputs any associated error code.
        /// </summary>
        /// <param name="pid">The unique identifier of the p.</param>
        /// <param name="error">The error.</param>
        /// <returns>The safe process handle result.</returns>
        public SafeProcessHandle Open(int pid, out int error)
        {
            if (OpenErrors.TryGetValue(pid, out error))
            {
                return new SafeProcessHandle(IntPtr.Zero, false);
            }

            error = 0;

            return new SafeProcessHandle((IntPtr)(pid + 1000), false);
        }

        /// <summary>
        /// Retrieves the creation timestamp of the specified process and indicates whether the process information was successfully accessed.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="created">The created.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool GetCreated(SafeProcessHandle process, out long created)
        {
            var pid = (int)process.DangerousGetHandle() - 1000;
            created = Created.TryGetValue(pid, out var value) ? value : 0;

            return !CannotRead.Contains(pid);
        }

        /// <summary>
        /// Terminates the specified process if it is not contained within the restricted list and tracks the operation in the termination collection.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool Terminate(SafeProcessHandle process)
        {
            var pid = (int)process.DangerousGetHandle() - 1000;
            if (CannotTerminate.Contains(pid))
            {
                return false;
            }

            Terminated.Add(pid);

            return true;
        }

        /// <summary>
        /// Attempts to retrieve the exit code associated with the specified process handle by mapping the process identifier to the internal exit code registry.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="code">The code.</param>
        /// <returns>true if the condition is met; otherwise, false.</returns>
        public bool TryGetExitCode(SafeProcessHandle process, out uint code)
        {
            var pid = (int)process.DangerousGetHandle() - 1000;

            return ExitCodes.TryGetValue(pid, out code);
        }

        /// <summary>
        /// Retrieves a list of parent-child process identifier pairs.
        /// </summary>
        /// <returns>A collection of list items.</returns>
        public List<(int Pid, int ParentPid)> Parents() => Processes(++ParentCalls);
    }

    private readonly MemoryLogger _logger = new();
    private readonly Native _native = new();

    [Fact]
    public void TheRootAndEveryDescendantAreEndedAndAnUnrelatedProcessIsLeftAlone()
    {
        _native.Created[1] = 100;
        _native.Created[2] = 110;
        _native.Created[3] = 120;
        _native.Created[4] = 50;
        _native.Created[5] = 130;
        _native.Processes = _ => new() { (2, 1), (3, 2), (4, 1), (5, 99), (1, 1), (3, 2) };
        ProcessTree.Kill(1, _logger, _native);
        Assert.Equal(new[] { 1, 2, 3 }, _native.Terminated.OrderBy(pid => pid));
        Assert.Empty(_logger.Records);
    }

    [Fact]
    public void AProcessThatIsAlreadyGoneIsLeftWithoutAWarningAndOtherOpenFailuresAreWarned()
    {
        _native.OpenErrors[1] = 87;
        ProcessTree.Kill(1, _logger, _native);
        Assert.Empty(_logger.Records);
        Assert.Empty(_native.Terminated);

        _native.OpenErrors[2] = 5;
        ProcessTree.Kill(2, _logger, _native);
        Assert.Contains("Cannot open process 2 to end it", _logger.Text("warn"));
    }

    [Fact]
    public void AProcessWhoseStartTimeCannotBeReadIsNotEnded()
    {
        _native.CannotRead.Add(1);
        ProcessTree.Kill(1, _logger, _native);
        Assert.Contains("Cannot read the start time of process 1", _logger.Text("warn"));
        Assert.Empty(_native.Terminated);
    }

    [Fact]
    public void AFailedTerminationIsOnlyWarnedWhenTheProcessIsStillRunning()
    {
        _native.CannotTerminate.Add(1);
        _native.ExitCodes[1] = 1;
        ProcessTree.Kill(1, _logger, _native);
        Assert.Empty(_logger.Records);

        _native.CannotTerminate.Add(2);
        _native.ExitCodes[2] = 259;
        ProcessTree.Kill(2, _logger, _native);
        Assert.Contains("Cannot end process 2", _logger.Text("warn"));

        _native.CannotTerminate.Add(3);
        ProcessTree.Kill(3, _logger, _native);
        Assert.Contains("Cannot end process 3", _logger.Text("warn"));
    }

    [Fact]
    public void AListingThatFailsIsWarnedAboutAndTheRootStaysEnded()
    {
        _native.Processes = _ => throw new Win32Exception(5);
        ProcessTree.Kill(1, _logger, _native);
        Assert.Equal(new[] { 1 }, _native.Terminated);
        Assert.Contains("Cannot list the processes of the tree of 1", _logger.Text("warn"));
    }

    [Fact]
    public void ATreeThatKeepsGrowingStopsAfterTheRoundLimitWithAWarning()
    {
        var next = 10;
        _native.Processes = _ =>
        {
            var pid = next++;
            _native.Created[pid] = 1000 + pid;

            return new() { (pid, 1) };
        };
        _native.Created[1] = 100;
        ProcessTree.Kill(1, _logger, _native);
        Assert.Equal(9, _native.Terminated.Count);
        Assert.Contains("Process 1 kept starting children while its tree was being ended", _logger.Text("warn"));
    }
}
