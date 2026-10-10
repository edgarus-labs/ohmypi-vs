using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Service;

/// <summary>Runs only with <c>OMP_REAL=1</c>: it starts the installed omp and sends a real (tiny) model request.</summary>
public sealed class RealOmpTests : IAsyncLifetime
{
    private readonly string _dir = TempDirectory.Create("omp-real-");
    private readonly MemoryLogger _logger = new(trace: true);
    private OmpService? _service;

    /// <summary>
    /// Gets a value indicating whether enabled.
    /// </summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("OMP_REAL") == "1";

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await Wait.Settle(_service.StopAsync());
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set OMP_REAL=1 to run against the installed omp (sends a real model request).")]
    public async Task StartsTheInstalledOmpListsModelsAndCompletesATinyPromptUntilTheSessionSettles()
    {
        var executable = ExecutableResolver.Locate(null);
        _service = new OmpService(new OmpServiceOptions
        {
            Executable = executable,
            ExtraArgs = new[] { "--session-dir", Path.Combine(_dir, "sessions") },
            Cwd = _dir,
            Logger = _logger,
            AutoRestart = false,
        });
        await _service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, _service.Connection.State);
        Assert.Equal(2, _service.Connection.ProtocolVersion);
        var pid = _service.Connection.Pid!.Value;

        var models = await _service.ListModelsAsync();
        Assert.NotEmpty(models);
        Assert.NotNull(_service.Session.Model);
        Assert.Contains(models, m => m.Provider == _service.Session.Model!.Provider && m.Id == _service.Session.Model.Id);

        var outcome = await _service.PromptAsync("Reply with exactly: ok");
        Assert.True(outcome.Status == PromptStatus.Completed, $"{outcome.Status}: {outcome.Error}");
        await Wait.For(() => _service.Session.Phase == SessionPhase.Idle, 120_000, "idle");
        await Wait.For(() => _logger.Text("trace").Contains("in {\"type\":\"session_settled\""), 120_000, "session_settled frame");
        var reply = _service.Transcript.OfType<AssistantItem>().Last();
        Assert.False(reply.Streaming);
        Assert.Contains("ok", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await _service.ListSessionsAsync(), s => s.FirstMessage == "Reply with exactly: ok");

        await _service.StopAsync();
        Assert.Equal(ConnectionState.Stopped, _service.Connection.State);
        await Wait.For(() => !FakeOmp.IsAlive(pid), 5000, "omp gone");
    }
}
