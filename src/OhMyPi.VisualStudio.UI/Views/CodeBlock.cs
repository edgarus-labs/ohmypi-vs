using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The one view of a tool's text, code or prose in a row. Text is cut to its first lines and prose to a
/// preview height, with "show more" under it that opens the whole row; open, it sits in a viewport of at most
/// <see cref="ExpandedHeight"/> that scrolls inside, with "show all" to lift that, "collapse" to close the row and
/// "Copy all" for text past <see cref="ToolView.ExpandedLines"/> / <see cref="ToolView.ExpandedChars"/>. Code gets
/// the gutter, token colors and search marks of <see cref="CodeSegments"/>. A trailer sits between the content and
/// the links. A wheel turn at the edge of the inner viewport scrolls the chat.
/// </summary>
internal sealed class CodeBlock
{
    /// <summary>Tallest an open block is before it scrolls inside.</summary>
    internal const double ExpandedHeight = 500;

    /// <summary>Height a collapsed prose preview shows.</summary>
    private const double PreviewHeight = 96;

    private readonly ToolView.ToolRow _row;
    private readonly string? _language;
    private readonly Regex? _mark;
    private readonly Border _frame = new Border();
    private readonly ScrollViewer _viewport = new ScrollViewer { Focusable = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBox? _pre;
    private readonly RichTextBox? _code;
    private readonly ScrollViewer? _codeScroller;
    private readonly TextBlock? _gutter;
    private readonly FrameworkElement? _prose;
    private readonly Button _more;
    private readonly Button _less;
    private readonly Button _copyAll;
    private string _text = "";
    private string _original = "";
    private string _shown = "";
    private string? _longest;

    /// <param name="brushKey">Text color; null keeps the body color.</param>
    /// <param name="original">What copying yields when it differs from <paramref name="text"/> (text shown reformatted); null means the text itself.</param>
    /// <param name="language">The language whose tokens are colored; null shows plain text with clickable file paths unless the text is a numbered listing.</param>
    /// <param name="mark">Search matches to mark in the code; null marks nothing.</param>
    /// <param name="trailer">Content shown under the text, before its links.</param>
    public CodeBlock(ToolView.ToolRow row, string text, object? brushKey, string? original = null, string? language = null, Regex? mark = null, UIElement? trailer = null)
        : this(row, language, mark, trailer)
    {
        if (language is null && ToolFormat.SplitLineNumbers(text) is null)
        {
            _pre = Ui.Pre("", brushKey);
            _pre.TextWrapping = TextWrapping.Wrap;
            _pre.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            FileClicks.Attach(_pre, row.Context);
            DataObject.AddCopyingHandler(_pre, CopyOriginal);
            _frame.Child = _pre;
        }
        else
        {
            _code = Ui.Code("", language, brushKey);
            DataObject.AddCopyingHandler(_code, CopyOriginal);
            _gutter = new TextBlock { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            _gutter.SetResourceReference(TextElement.FontFamilyProperty, "Omp.MonoFont");
            _gutter.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Subtle);
            Ui.Small(_gutter, Ui.MonoScale);
            _codeScroller = new ScrollViewer { Content = _code, Focusable = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Ui.BubbleWheel(_codeScroller);
            _code.Loaded += (_, _) => FitWidth();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_codeScroller, 1);
            grid.Children.Add(_gutter);
            grid.Children.Add(_codeScroller);
            _frame.Child = grid;
        }

        SetText(text, original);
    }

    /// <summary>Prose rendered from <paramref name="source"/> (its lines count for the labels), cut to a preview height.</summary>
    public CodeBlock(ToolView.ToolRow row, FrameworkElement prose, string source)
        : this(row, language: null, mark: null, trailer: null)
    {
        _prose = prose;
        _frame.Child = prose;
        SetText(source);
    }

    private CodeBlock(ToolView.ToolRow row, string? language, Regex? mark, UIElement? trailer)
    {
        _row = row;
        _language = language;
        _mark = mark;
        _viewport.Content = _frame;
        _viewport.PreviewMouseWheel += PassWheelAtEdge;
        _more = Ui.Link("show more", row.Expand);
        _less = Ui.Link("collapse", row.Collapse);
        _copyAll = Ui.Link("Copy all", () => row.Context.Copy(_original));
        var links = new WrapPanel();
        foreach (var link in new[] { _more, _less, _copyAll })
        {
            link.Margin = new Thickness(0, 0, 12, 0);
            links.Children.Add(link);
        }

        var panel = new StackPanel();
        panel.Children.Add(_viewport);
        if (trailer is not null)
        {
            if (trailer is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 4, 0, 0);
            }

            panel.Children.Add(trailer);
        }

        panel.Children.Add(links);
        Element = panel;
        row.Register(this);
    }

    public FrameworkElement Element { get; }

    /// <summary>The selectable text box, for naming it to assistive technology.</summary>
    public FrameworkElement Box => (FrameworkElement?)_pre ?? (FrameworkElement?)_code ?? _prose!;

    /// <summary>Whether less than the whole content is on screen.</summary>
    public bool Cut { get; private set; }

    /// <summary>Re-applies the row's expand state to what is shown.</summary>
    public void Refresh() => Show();

    /// <summary>Replaces the text; growth of what is on screen is appended, so a selection in it survives.</summary>
    public void SetText(string text, string? original = null)
    {
        _text = text.TrimEnd('\r', '\n');
        _original = original ?? text;
        Show();
    }

    /// <summary>
    /// Fills a box made by <see cref="Ui.Code"/>: tokens take their colors, comments turn italic and search matches
    /// sit on the raised code surface. A numbered listing's numbers go into <paramref name="gutter"/>, a column
    /// beside the box that selection never touches, and its lines stop wrapping so each keeps its number; without
    /// a gutter the numbers sit dimmed in front of each line.
    /// </summary>
    /// <returns>The longest line of a numbered listing, which sets the box's width; null when the text wraps instead.</returns>
    public static string? Fill(RichTextBox box, string text, string? language, Regex? mark = null, TextBlock? gutter = null)
    {
        var inlines = ((Paragraph)box.Document.Blocks.FirstBlock).Inlines;
        inlines.Clear();
        var lines = CodeSegments.Build(text, language, mark);
        var numbered = lines.Any(l => l.Number.Length > 0);
        string? longest = null;
        if (gutter is not null)
        {
            gutter.Text = string.Join("\n", lines.Select(l => l.Number));
            gutter.Visibility = numbered ? Visibility.Visible : Visibility.Collapsed;
            if (box.Parent is ScrollViewer scroller)
            {
                scroller.HorizontalScrollBarVisibility = numbered ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            }

            if (numbered)
            {
                longest = lines.Select(l => string.Concat(l.Segments.Select(s => s.Text))).OrderByDescending(l => l.Length).First();
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new LineBreak());
            }

            if (numbered && gutter is null)
            {
                var cell = new Run(lines[i].Number + "  ");
                cell.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Subtle);
                inlines.Add(cell);
            }

            foreach (var segment in lines[i].Segments)
            {
                if (segment.Text.Length == 0)
                {
                    continue;
                }

                var run = new Run(segment.Text);
                var brush = Ui.TokenBrush(segment.Kind);
                if (brush is not null)
                {
                    run.SetResourceReference(TextElement.ForegroundProperty, brush);
                }

                if (segment.Kind == CodeTokenKind.Comment)
                {
                    run.FontStyle = FontStyles.Italic;
                }

                if (segment.Marked)
                {
                    run.SetResourceReference(TextElement.BackgroundProperty, ThemeKeys.CodeSurfaceBorder);
                    run.FontWeight = FontWeights.SemiBold;
                }

                inlines.Add(run);
            }
        }

        return longest;
    }

    /// <summary>A numbered listing never wraps: the document takes the width of its longest line in the box's typeface, so the code column scrolls sideways instead.</summary>
    private void FitWidth()
    {
        if (_code is null || !_code.IsLoaded)
        {
            return;
        }

        if (_longest is null)
        {
            _code.Document.PageWidth = double.NaN;

            return;
        }

        var typeface = new Typeface(_code.FontFamily, _code.FontStyle, _code.FontWeight, _code.FontStretch);
        var measured = new FormattedText(_longest, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, _code.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(_code).PixelsPerDip);
        _code.Document.PageWidth = Math.Ceiling(measured.WidthIncludingTrailingWhitespace * 1.02) + 8;
    }

    /// <summary>Copying everything that is shown yields the original text rather than its reformatted display.</summary>
    private void CopyOriginal(object sender, DataObjectCopyingEventArgs e)
    {
        if (Cut || _original == _text || !WholeSelected())
        {
            return;
        }

        e.DataObject.SetData(DataFormats.UnicodeText, _original);
        e.DataObject.SetData(DataFormats.Text, _original);
    }

    /// <summary>Whether the selection spans everything on screen, so a copy should yield the original text.</summary>
    private bool WholeSelected()
    {
        if (_pre is not null)
        {
            return _pre.SelectionLength > 0 && _pre.SelectionLength == _pre.Text.Length;
        }

        var all = new TextRange(_code!.Document.ContentStart, _code.Document.ContentEnd).Text;

        return !_code.Selection.IsEmpty && _code.Selection.Text == all;
    }

    /// <summary>A wheel turn with nothing to scroll here, or at the top or bottom of the inner viewport, scrolls the chat instead.</summary>
    private void PassWheelAtEdge(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var atEdge = e.Delta > 0 ? _viewport.VerticalOffset <= 0 : _viewport.VerticalOffset >= _viewport.ScrollableHeight;
        if (!atEdge)
        {
            return;
        }

        e.Handled = true;
        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = _viewport };
        (_viewport.Parent as UIElement)?.RaiseEvent(forwarded);
    }

    private void Show()
    {
        var lines = _text.Split('\n');
        var expanded = _row.Expanded;
        if (_prose is not null)
        {
            Cut = lines.Length > ToolView.PreviewLines;
        }
        else
        {
            ShowText(lines, expanded);
        }

        _viewport.MaxHeight = expanded ? ExpandedHeight : _prose is not null && Cut ? PreviewHeight : double.PositiveInfinity;
        _viewport.VerticalScrollBarVisibility = expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Element.Visibility = _text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _more.Visibility = Cut && !expanded ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_more, $"show more ({lines.Length} lines)");
        _less.Visibility = expanded && (lines.Length > ToolView.PreviewLines || _text.Length > ToolView.PreviewChars) ? Visibility.Visible : Visibility.Collapsed;
        _copyAll.Visibility = Cut && expanded ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_copyAll, $"Copy all {lines.Length} lines");
    }

    private void ShowText(string[] lines, bool expanded)
    {
        var shown = expanded
            ? CutTo(_text, lines, ToolView.ExpandedLines, ToolView.ExpandedChars)
            : CutTo(_text, lines, ToolView.PreviewLines, ToolView.PreviewChars);
        if (_pre is not null && shown.Length > _shown.Length && shown.StartsWith(_shown, StringComparison.Ordinal))
        {
            _pre.AppendText(shown.Substring(_shown.Length));
        }
        else if (shown != _shown)
        {
            if (_pre is not null)
            {
                _pre.Text = shown;
            }
            else
            {
                _longest = Fill(_code!, shown, _language, _mark, _gutter);
                FitWidth();
            }
        }

        _shown = shown;
        Cut = shown.Length < _text.Length;
    }

    private static string CutTo(string text, string[] lines, int maxLines, int maxChars)
    {
        if (lines.Length <= maxLines && text.Length <= maxChars)
        {
            return text;
        }

        var shown = string.Join("\n", lines.Take(maxLines));
        if (shown.Length > maxChars)
        {
            shown = shown.Substring(0, maxChars);
        }

        return shown;
    }

    private static void SetLabel(Button link, string label)
    {
        ((TextBlock)link.Content).Text = label;
        Ui.AutomationName(link, label);
    }
}
