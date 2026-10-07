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

public class ChatPaletteTests
{
    private static readonly Argb Dark = new Argb(0xFF, 0x1E, 0x1E, 0x1E);
    private static readonly Argb Light = new Argb(0xFF, 0xF5, 0xF5, 0xF5);

    private static readonly Argb LightText = new Argb(0xFF, 0xF1, 0xF1, 0xF1);
    private static readonly Argb DarkText = new Argb(0xFF, 0x1E, 0x1E, 0x1E);

    [Fact]
    public void CodeSurfaceLeansTowardWhiteOnDarkAndTowardBlackOnLightBackgrounds()
    {
        var onDark = ChatPalette.CodeSurface(Dark, LightText, false).Surface;
        var onLight = ChatPalette.CodeSurface(Light, DarkText, false).Surface;
        Assert.True(onDark.R > Dark.R && onDark.G > Dark.G && onDark.B > Dark.B);
        Assert.True(onLight.R < Light.R && onLight.G < Light.G && onLight.B < Light.B);
    }

    [Theory]
    [InlineData(0x00, 0xFF)]
    [InlineData(0xFF, 0x00)]
    public void In_high_contrast_code_boxes_keep_the_theme_background_and_take_the_text_color_as_border(byte background, byte text)
    {
        var window = new Argb(0xFF, background, background, background);
        var foreground = new Argb(0xFF, text, text, text);
        var (surface, border) = ChatPalette.CodeSurface(window, foreground, true);
        Assert.Equal(window, surface);
        Assert.Equal(foreground, border);
    }

    [Fact]
    public void CodeSurfaceBorderIsFartherFromTheBackgroundThanTheSurfaceAndBothAreOpaque()
    {
        foreach (var background in new[] { Dark, Light, new Argb(0x80, 0x10, 0x40, 0xC0) })
        {
            var (surface, border) = ChatPalette.CodeSurface(background, LightText, false);
            Assert.Equal(0xFF, surface.A);
            Assert.Equal(0xFF, border.A);
            Assert.True(Math.Abs(border.R - background.R) > Math.Abs(surface.R - background.R));
            Assert.True(Math.Abs(border.B - background.B) > Math.Abs(surface.B - background.B));
        }
    }

    [Fact]
    public void DiffLinesAreTranslucentAndTheirChangedWordsStronger()
    {
        foreach (var dark in new[] { true, false })
        {
            Assert.True(ChatPalette.AddedLine(dark).A < 0xFF);
            Assert.True(ChatPalette.RemovedLine(dark).A < 0xFF);
            Assert.True(ChatPalette.AddedWord(dark).A > ChatPalette.AddedLine(dark).A);
            Assert.True(ChatPalette.RemovedWord(dark).A > ChatPalette.RemovedLine(dark).A);
            Assert.NotEqual(ChatPalette.AddedLine(dark), ChatPalette.RemovedLine(dark));
        }
    }

    [Theory]
    [InlineData(30, 30, 30, true)]
    [InlineData(255, 255, 255, false)]
    [InlineData(0, 0, 255, true)]
    [InlineData(0, 255, 0, false)]
    public void ThemeDarknessFollowsTheLuminanceOfTheBackground(byte r, byte g, byte b, bool dark)
    {
        Assert.Equal(dark, ChatPalette.IsDark(r, g, b));
        Assert.Equal(dark, ChatPalette.IsDark(new Argb(0xFF, r, g, b)));
    }
}
