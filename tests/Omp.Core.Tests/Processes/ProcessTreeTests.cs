using Microsoft.Win32.SafeHandles;
using Omp.Core.Processes;
using Omp.Core.Tests.Support;
using System.ComponentModel;

namespace Omp.Core.Tests.Processes;

public class ProcessTreeTests
{
    private sealed class Native : ProcessTree.INative
    {
        public Dictionary<int, long> Created { get; } = new();

        public Dictionary<int, int> OpenErrors { get; } = new();

        public HashSet<int> CannotRead { get; } = new();

        public HashSet<int> CannotTerminate { get; } = new();

        public Dictionary<int, uint> ExitCodes { get; } = new();

        public Func<int, List<(int, int)>> Processes { get; set; } = _ => new();

        public List<int> Terminated { get; } = new();

        public int ParentCalls { get; private set; }

        public SafeProcessHandle Open(int pid, out int error)
        {
            if (OpenErrors.TryGetValue(pid, out error))
            {
                return new SafeProcessHandle(IntPtr.Zero, false);
            }

            error = 0;

            return new SafeProcessHandle((IntPtr)(pid + 1000), false);
        }

        public bool GetCreated(SafeProcessHandle process, out long created)
        {
            var pid = (int)process.DangerousGetHandle() - 1000;
            created = Created.TryGetValue(pid, out var value) ? value : 0;

            return !CannotRead.Contains(pid);
        }

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

        public bool TryGetExitCode(SafeProcessHandle process, out uint code)
        {
            var pid = (int)process.DangerousGetHandle() - 1000;

            return ExitCodes.TryGetValue(pid, out code);
        }

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
