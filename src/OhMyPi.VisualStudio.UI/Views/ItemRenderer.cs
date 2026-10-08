using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>The visual of one transcript item; a renderer failure becomes an error notice in its place.</summary>
internal static class ItemRenderer
{
    public static FrameworkElement Render(TranscriptItem item, RenderContext ctx)
    {
        try
        {
            switch (item)
            {
                case UserItem user: return User(user, ctx);
                case AssistantItem assistant: return Assistant(assistant, ctx);
                case ToolItem tool: return ToolView.Render(tool, ctx);
                case NoticeItem notice: return Notices.Line(notice.Level, notice.Text, ctx.Open, $"{notice.Id}:notice");
                case CommandOutputItem output: return CommandOutput(output, ctx);
                case TurnSummaryItem turn: return TurnSummary(turn);
                case ActivityItem activity: return new ActivityView(activity);
                default: return Notices.Line(NoticeLevel.Warning, $"Unsupported item {item.GetType().Name}");
            }
        }
        catch (Exception error)
        {
            var what = $"Failed to render {item.GetType().Name}";
            ctx.LogError(what, error);

            return Notices.Line(NoticeLevel.Error, $"{what}: {error.Message}");
        }
    }

    /// <summary>Gap between consecutive tool calls, and above a tool call that follows a message.</summary>
    private const double OperationGap = 14;
    private const double ToolAfterMessageGap = 12;

    /// <summary>Gap above a final answer: this, its rule and <see cref="AnswerRuleGap"/> below the rule.</summary>
    private const double AnswerGap = 12;
    internal const double AnswerRuleGap = 10;

    /// <summary>Vertical gap above an item: roomy between operations, roomier before the answer to them, tight before a turn's summary.</summary>
    public static double GapAbove(TranscriptItem item, TranscriptItem? previous)
    {
        if (previous is null)
        {
            return 0;
        }

        if (item is ToolItem)
        {
            return previous is ToolItem ? OperationGap : ToolAfterMessageGap;
        }

        if (item is AssistantItem && previous is ToolItem)
        {
            return AnswerGap;
        }

        if (item is TurnSummaryItem || item is ActivityItem)
        {
            return 6;
        }

        return 14;
    }

    /// <summary>Whether <paramref name="item"/> is the answer to the tool calls before it, so it is set off by a rule.</summary>
    public static bool IsAnswerToTools(TranscriptItem item, TranscriptItem? previous) => item is AssistantItem && previous is ToolItem;

    private static FrameworkElement CommandOutput(CommandOutputItem output, RenderContext ctx)
    {
        var text = Ui.Pre(output.Text);
        FileClicks.Attach(text, ctx);

        return Ui.Card(text, new Thickness(10, 6, 10, 6));
    }

    private static FrameworkElement TurnSummary(TurnSummaryItem turn)
    {
        var text = Ui.Small(Ui.Muted(Format.TurnSummary(turn), wrap: true), 0.9);
        text.ToolTip = $"{turn.InputTokens:N0} input · {turn.CacheReadTokens:N0} cache read · {turn.CacheWriteTokens:N0} cache write · {turn.OutputTokens:N0} output tokens";

        return text;
    }

    private static FrameworkElement User(UserItem item, RenderContext ctx)
    {
        var parts = PromptFormatter.SplitUserMessage(item.Text);
        var children = new List<UIElement?>();
        if (parts.Text.Length > 0)
        {
            children.Add(Ui.Prose(parts.Text));
        }

        for (var i = 0; i < parts.Pasted.Count; i++)
        {
            var pasted = parts.Pasted[i];
            children.Add(Collapsible.Create(ctx, $"{item.Id}:pasted:{i}", Ui.Muted($"{pasted.Name} · {Chrome.LineCount(pasted.Text)}"), () => Ui.Pre(pasted.Text)));
        }
        if (parts.Files.Count > 0)
        {
            var chips = new WrapPanel();
            foreach (var file in parts.Files)
            {
                var chip = Ui.Link("@" + file, () => ctx.OpenFile(file, null), $"Open {file}", mono: true);
                chip.Margin = new Thickness(0, 2, 10, 0);
                chips.Children.Add(chip);
            }
            children.Add(chips);
        }
        if (!string.IsNullOrEmpty(parts.EditorContext))
        {
            children.Add(Collapsible.Create(ctx, $"{item.Id}:ctx", Ui.Muted("editor context"), () => Ui.Pre(parts.EditorContext!)));
        }
        if (item.ImageCount > 0)
        {
            children.Add(Ui.Muted($"+{item.ImageCount} image{(item.ImageCount == 1 ? "" : "s")}"));
        }

        var card = new Border { Child = Ui.Column(4, [.. children]), Padding = new Thickness(12, 4, 10, 4), BorderThickness = new Thickness(3, 0, 0, 0) }
            .Theme(Border.BorderBrushProperty, ThemeKeys.Accent);
        Ui.AutomationName(card, "You");

        return WithCopy(card, item.Text, ctx);
    }

    private static FrameworkElement Assistant(AssistantItem item, RenderContext ctx) => new AssistantView(item, ctx);

    /// <summary>
    /// An assistant message: thinking, the Markdown answer, an error and the usage line, with a copy button once
    /// it is complete. While it streams each update changes only the parts that differ.
    /// </summary>
    private sealed class AssistantView : Grid, ILiveView, ISeparatedView
    {
        private readonly RenderContext _ctx;
        private readonly StackPanel _column = new StackPanel();
        private readonly ContentControl _thinking = new ContentControl { Focusable = false };
        private readonly FrameworkElement _answer;
        private readonly ContentControl _error = new ContentControl { Focusable = false };
        private readonly TextBlock _meta = Ui.Muted("");
        private readonly Button _copy;
        private readonly DockPanel _footer = new DockPanel { LastChildFill = true };
        private AssistantItem _item;
        private string? _thinkingShown;
        private string? _errorShown;
        private bool _separated;

        private readonly Border _rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 0, 0, AnswerRuleGap), Visibility = Visibility.Collapsed }
            .Theme(Border.BorderBrushProperty, ThemeKeys.Divider);

        public AssistantView(AssistantItem item, RenderContext ctx)
        {
            _ctx = ctx;
            _item = item;
            _answer = MarkdownView.Render("", ctx.Links, ctx.Copy);
            _meta.Opacity = 0;
            _copy = CopyButton(() => _ctx.Copy(_item.Text));
            DockPanel.SetDock(_copy, Dock.Right);
            _footer.Children.Add(_copy);
            _footer.Children.Add(_meta);
            foreach (var part in new UIElement[] { _thinking, _answer, _error, _footer })
            {
                _column.Children.Add(part);
            }

            Background = System.Windows.Media.Brushes.Transparent;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetRow(_column, 1);
            Children.Add(_rule);
            Children.Add(_column);
            MouseEnter += (_, __) => Hover(true);
            MouseLeave += (_, __) => Hover(IsKeyboardFocusWithin);
            IsKeyboardFocusWithinChanged += (_, __) => Hover(IsKeyboardFocusWithin || IsMouseOver);
            Apply(item);
        }

        public bool IsLive => _item.Streaming;

        public bool TryUpdate(TranscriptItem item)
        {
            if (!(item is AssistantItem next) || next.Id != _item.Id)
            {
                return false;
            }

            Apply(next);

            return true;
        }

        private void Hover(bool on)
        {
            _meta.Opacity = on ? 1 : 0;
            _copy.Opacity = on ? 1 : 0;
        }

        /// <summary>Draws the rule above the answer when the message follows tool calls, i.e. it is the answer to them.</summary>
        public void SetSeparated(bool separated)
        {
            _separated = separated;
            _rule.Visibility = _separated && _item.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Apply(AssistantItem item)
        {
            _item = item;
            _rule.Visibility = _separated && item.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var lines = item.Thinking.Split('\n').Count(l => l.Trim().Length > 0);
            var label = item.Streaming && item.Text.Length == 0 ? "Thinking…" : $"Thinking · {lines} line{(lines == 1 ? "" : "s")}";
            var thinking = item.Thinking.Length == 0 ? "" : label + "\u0001" + item.Thinking;
            if (thinking != _thinkingShown)
            {
                _thinkingShown = thinking;
                var text = item.Thinking;
                _thinking.Content = text.Length == 0 ? null : Collapsible.Create(_ctx, $"{item.Id}:thinking", Ui.Muted(label), () =>
                {
                    var prose = Ui.Prose(text, ThemeKeys.Muted, small: true);
                    var frame = new Border { Child = prose, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2) };
                    frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);

                    return frame;
                });
            }
            MarkdownView.Update(_answer, item.Text, _ctx.Links, _ctx.Copy);
            var error = item.ErrorMessage ?? "";
            if (error != _errorShown)
            {
                _errorShown = error;
                _error.Content = error.Length == 0 ? null : Notices.Line(NoticeLevel.Error, error, _ctx.Open, $"{item.Id}:error");
            }
            _meta.Text = Meta(item);
            _thinking.Visibility = _thinking.Content is null ? Visibility.Collapsed : Visibility.Visible;
            _answer.Visibility = item.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _error.Visibility = _error.Content is null ? Visibility.Collapsed : Visibility.Visible;
            _meta.Visibility = _meta.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _copy.Visibility = item.Text.Length > 0 && !item.Streaming ? Visibility.Visible : Visibility.Collapsed;
            _footer.Visibility = _meta.Visibility == Visibility.Visible || _copy.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            var first = true;
            foreach (FrameworkElement part in _column.Children)
            {
                if (part.Visibility != Visibility.Visible)
                {
                    continue;
                }

                part.Margin = new Thickness(ReferenceEquals(part, _thinking) ? 0 : ToolView.IconColumn, first ? 0 : 4, 0, 0);
                first = false;
            }
        }
    }

    private static string Meta(AssistantItem item)
    {
        if (item.Streaming)
        {
            return "";
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.Model))
        {
            parts.Add(item.Model!);
        }

        if (item.Usage is not null)
        {
            parts.Add($"{item.Usage.Input.ToString("N0", CultureInfo.CurrentCulture)} in / {item.Usage.Output.ToString("N0", CultureInfo.CurrentCulture)} out");
            if (item.Usage.CostUsd.HasValue)
            {
                parts.Add(Format.FormatCost(item.Usage.CostUsd.Value));
            }
        }
        if (!string.IsNullOrEmpty(item.StopReason) && item.StopReason != "stop" && item.StopReason != "toolUse")
        {
            parts.Add(item.StopReason!);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Puts the message's copy button below it, at the right edge, shown while the pointer or focus is on the message.</summary>
    private static FrameworkElement WithCopy(FrameworkElement content, string text, RenderContext ctx)
    {
        var copy = CopyButton(() => ctx.Copy(text));
        copy.Margin = new Thickness(0, 2, 0, 0);
        var column = new StackPanel { Background = System.Windows.Media.Brushes.Transparent };
        column.Children.Add(content);
        column.Children.Add(copy);
        column.MouseEnter += (_, __) => copy.Opacity = 1;
        column.MouseLeave += (_, __) => copy.Opacity = copy.IsKeyboardFocused ? 1 : 0;
        copy.GotKeyboardFocus += (_, __) => copy.Opacity = 1;
        copy.LostKeyboardFocus += (_, __) => copy.Opacity = column.IsMouseOver ? 1 : 0;

        return column;
    }

    /// <summary>The dim button under a message that copies all of it; hidden until the message is hovered or focused.</summary>
    private static Button CopyButton(Action copy)
    {
        var button = Ui.IconButton(Glyphs.Copy, "Copy message", copy);
        button.Width = 22;
        button.Height = 22;
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Subtle);
        button.Opacity = 0;

        return button;
    }
}
