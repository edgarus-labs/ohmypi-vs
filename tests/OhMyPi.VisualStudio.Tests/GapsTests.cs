using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Omp.Core;
using Omp.Core.Changes;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;

namespace OhMyPi.VisualStudio.Tests;

public class DiffSourceTests
{
    [Fact]
    public void TwoSourcesAreEqualWhenTheyHoldTheSameContentAndState()
    {
        var a = new DiffSource(false, "before");
        Assert.True(a.Equals(new DiffSource(false, "before")));
        Assert.True(a.Equals((object)new DiffSource(false, "before")));
        Assert.False(a.Equals(new DiffSource(true, "before")));
        Assert.False(a.Equals(new DiffSource(false, "other")));
        Assert.False(a.Equals("before"));
        Assert.Equal(a.GetHashCode(), new DiffSource(false, "before").GetHashCode());
        Assert.Equal("Deleted=False, Before=before", a.ToString());
    }
}

public class QuotedArgumentsTests
{
    [Fact]
    public void ADoubledQuoteInsideQuotesIsALiteralQuote()
    {
        Assert.Equal(new[] { "a \"b\" c", "d" }, CommandLine.Split("\"a \"\"b\"\" c\" d"));
        Assert.Empty(CommandLine.Split(null));
    }
}

public class FilterPreferencesTests
{
    private sealed class FakeStore : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void TheFavoritesAndRecentFiltersAreRememberedSeparately()
    {
        var store = new FakeStore();
        var preferences = new ModelPreferenceStore(store);
        Assert.False(preferences.FavoritesOnly);
        Assert.False(preferences.RecentOnly);
        preferences.FavoritesOnly = true;
        Assert.True(new ModelPreferenceStore(store).FavoritesOnly);
        Assert.False(new ModelPreferenceStore(store).RecentOnly);
        preferences.RecentOnly = true;
        preferences.FavoritesOnly = false;
        var reopened = new ModelPreferenceStore(store);
        Assert.False(reopened.FavoritesOnly);
        Assert.True(reopened.RecentOnly);
    }
}

public sealed class StaleBaselinesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "ohmypi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
    }

    [Fact]
    public void ARootThatCannotBeDeletedIsReportedAndTheSweepContinues()
    {
        var locked = Path.Combine(_parent, "111-a", "1");
        Directory.CreateDirectory(locked);
        var other = Directory.CreateDirectory(Path.Combine(_parent, "222-b")).FullName;
        using (new FileStream(Path.Combine(locked, "a.cs"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var failures = DiffBaselines.SweepStale(_parent, _ => false);
            var failure = Assert.Single(failures);
            Assert.EndsWith("111-a", failure.Root);
            Assert.False(Directory.Exists(other));
        }
    }
}

public sealed class FailingTrackingTests : IDisposable
{
    private sealed class Logger : IOmpLogger
    {
        public readonly ConcurrentQueue<string> Errors = new();
        public void Error(string message, Exception? error = null) => Errors.Enqueue(message);
        public void Warn(string message, Exception? error = null) { }
        public void Info(string message, Exception? error = null) { }
        public void Debug(string message, Exception? error = null) { }
        public bool TraceEnabled => false;
        public void Trace(string direction, string frame) { }
    }

    private const string Cwd = @"D:\work\repo";
    private readonly FakeOmpService _service = new() { Cwd = Cwd };
    private readonly Logger _logger = new();
    private readonly ChangeFeed _feed;

    public FailingTrackingTests()
    {
        var model = new ChangeModel(_ => throw new InvalidOperationException("disk gone"), _logger);
        _feed = new ChangeFeed(_service, model, new WorkspaceScope(Cwd, new[] { Cwd }), _logger);
    }

    public void Dispose() => _feed.Dispose();

    [Fact]
    public async Task AToolEventThatCannotBeTrackedIsLoggedAndTheFeedKeepsWorking()
    {
        _service.RaiseToolExecution(new ToolExecutionEvent
        {
            Phase = ToolExecutionPhase.Start,
            ToolCallId = "t1",
            Name = "write",
            Args = new JObject { ["path"] = "a.txt", ["content"] = "x" },
        });
        await _feed.WhenIdleAsync();
        Assert.Contains("Change tracking failed", _logger.Errors);
    }
}

public class DismissalStoreTests
{
    private sealed class FakeStore : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void ADismissedKeyStaysDismissedAcrossStoresOverTheSameData()
    {
        var data = new FakeStore();
        var store = new DismissalStore(data);
        Assert.False(store.IsDismissed("todos:1"));
        store.Dismiss("todos:1");
        store.Dismiss("todos:2");
        store.Dismiss("todos:1");
        Assert.True(store.IsDismissed("todos:1"));
        Assert.True(new DismissalStore(data).IsDismissed("todos:2"));
        Assert.False(new DismissalStore(data).IsDismissed("todos:3"));
        Assert.Equal("todos:1\ntodos:2", data.Values["Dismissed"]);
    }

    [Fact]
    public void OnlyTheNewestKeysAreKept()
    {
        var data = new FakeStore();
        var store = new DismissalStore(data);
        for (var i = 0; i < DismissalStore.Limit + 5; i++) store.Dismiss("k" + i);
        Assert.False(store.IsDismissed("k0"));
        Assert.True(store.IsDismissed("k" + (DismissalStore.Limit + 4)));
        Assert.Equal(DismissalStore.Limit, data.Values["Dismissed"].Split('\n').Length);
    }
}

public class DiffPaletteTests
{
    [Fact]
    public void LinesAreTranslucentGreenAndRedAndLighterOnLightThemes()
    {
        Assert.Equal(new Argb(0x73, 0, 0xFF, 0), DiffPalette.AddedLine(true));
        Assert.Equal(new Argb(0x38, 0, 0xFF, 0), DiffPalette.AddedLine(false));
        Assert.Equal(new Argb(0x8C, 0xFF, 0, 0), DiffPalette.RemovedLine(true));
        Assert.Equal(new Argb(0x30, 0xFF, 0, 0), DiffPalette.RemovedLine(false));
        Assert.NotEqual(DiffPalette.AddedLine(true), DiffPalette.RemovedLine(true));
        Assert.True(DiffPalette.AddedLine(true).Equals((object)new Argb(0x73, 0, 0xFF, 0)));
        Assert.False(DiffPalette.AddedLine(true).Equals("x"));
        Assert.Equal(DiffPalette.AddedLine(true).GetHashCode(), new Argb(0x73, 0, 0xFF, 0).GetHashCode());
    }

    [Fact]
    public void AChangedWordWithoutAFillIsItsLineLessTransparentButNeverPastOpaque()
    {
        Assert.Equal(new Argb(0xB3, 0, 0xFF, 0), DiffPalette.Stronger(DiffPalette.AddedLine(true)));
        Assert.Equal(new Argb(255, 1, 2, 3), DiffPalette.Stronger(new Argb(0xF0, 1, 2, 3)));
    }

    [Theory]
    [InlineData(30, 30, 30, true)]
    [InlineData(255, 255, 255, false)]
    [InlineData(0, 0, 255, true)]
    [InlineData(0, 255, 0, false)]
    public void ThemeDarknessFollowsTheLuminanceOfTheBackground(byte r, byte g, byte b, bool dark) => Assert.Equal(dark, DiffPalette.IsDark(r, g, b));
}
