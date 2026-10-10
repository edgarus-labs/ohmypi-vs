using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Collections.Generic;
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
/// The one view of a tool's text, code or prose in a row. Text is cut to its first lines and prose to a preview
/// height, with "show more" under it that opens the whole row; open, it sits in a viewport of at most
/// <see cref="ExpandedHeight"/> that scrolls inside, with "collapse" to close the row (unless the row closes to its
/// header, which then offers its own) and "Copy all" when text past <see cref="ToolView.ExpandedLines"/> /
/// <see cref="ToolView.ExpandedChars"/> is withheld. A numbered listing is split into gutter and code once, so the
/// preview and the open text number their lines alike; code gets the gutter, token colors and search marks of
/// <see cref="CodeSegments"/>. File paths in plain text and code open on click. A trailer sits between the content
/// and the links. A wheel turn at the edge of the inner viewport scrolls the chat.
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
    private bool _hasOriginal;
    private ToolFormat.Listing? _listing;
    private string _body = "";
    private string[] _lines = [""];
    private string _shown = "";
    private string[] _shownNumbers = [];
    private string? _longest;

    /// <param name="brushKey">Text color; null keeps the body color.</param>
    /// <param name="original">What copying yields when it differs from <paramref name="text"/> (text shown reformatted); null means what is shown.</param>
    /// <param name="language">The language whose tokens are colored; with neither a language nor <paramref name="mark"/>, text that is not a numbered listing shows as plain wrapping text.</param>
    /// <param name="mark">Search matches to mark in the code; null marks nothing.</param>
    /// <param name="trailer">Content shown under the text, before its links.</param>
    public CodeBlock(ToolView.ToolRow row, string text, object? brushKey, string? original = null, string? language = null, Regex? mark = null, UIElement? trailer = null)
        : this(row, language, mark, trailer)
    {
        var listing = ToolFormat.SplitLineNumbers(text);
        if (language is null && mark is null && listing is null)
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
            FileClicks.Attach(_code, row.Context);
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

        Apply(text, original, listing);
    }

    /// <summary>Prose rendered from <paramref name="source"/> (its lines count for the labels), cut to a preview height.</summary>
    public CodeBlock(ToolView.ToolRow row, FrameworkElement prose, string source)
        : this(row, language: null, mark: null, trailer: null)
    {
        _prose = prose;
        _frame.Child = prose;
        SetText(source);
    }

    /// <summary>
    /// Initializes a new instance of the CodeBlock class with the specified tool row, programming language, marking regular expression, and optional trailer element.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="language">The language.</param>
    /// <param name="mark">The mark.</param>
    /// <param name="trailer">The trailer.</param>
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

    /// <summary>
    /// Gets the element.
    /// </summary>
    public FrameworkElement Element { get; }

    /// <summary>The selectable text box, for naming it to assistive technology.</summary>
    public FrameworkElement Box => (FrameworkElement?)_pre ?? (FrameworkElement?)_code ?? _prose!;

    /// <summary>Whether less than the whole content is on screen.</summary>
    public bool Cut { get; private set; }

    /// <summary>Re-applies the row's expand state to what is shown.</summary>
    public void Refresh() => Show();

    /// <summary>Replaces the text; growth of plain text on screen is appended, so a selection in it survives.</summary>
    public void SetText(string text, string? original = null) => Apply(text, original, _code is not null ? ToolFormat.SplitLineNumbers(text) : null);

    /// <param name="listing">The numbered listing <paramref name="text"/> is, split once for preview and open text alike; null for other text.</param>
    private void Apply(string text, string? original, ToolFormat.Listing? listing)
    {
        _text = text.TrimEnd('\r', '\n');
        _original = original ?? text;
        _hasOriginal = original is not null;
        _listing = listing;
        _body = listing?.Code ?? _text;
        _lines = _body.Split('\n');

        Show();
    }

    /// <summary>
    /// Fills a box made by <see cref="Ui.Code"/>: tokens take their colors, comments turn italic and search matches
    /// stand out on the raised code surface (in high contrast, in the system highlight colors). The
    /// <paramref name="numbers"/> of a numbered listing go into <paramref name="gutter"/>, a column beside the box
    /// that selection never touches, and its lines stop wrapping so each keeps its number.
    /// </summary>
    /// <param name="numbers">The gutter number of each line of <paramref name="code"/>; null for code without line numbers.</param>
    /// <returns>The longest line of a numbered listing, which sets the box's width; null when the text wraps instead.</returns>
    public static string? Fill(RichTextBox box, string code, IReadOnlyList<string>? numbers, string? language, Regex? mark = null, TextBlock? gutter = null)
    {
        var inlines = ((Paragraph)box.Document.Blocks.FirstBlock).Inlines;
        inlines.Clear();
        var lines = CodeSegments.Build(code, numbers, language, mark);
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
                    if (SystemParameters.HighContrast)
                    {
                        run.SetResourceReference(TextElement.BackgroundProperty, SystemColors.HighlightBrushKey);
                        run.SetResourceReference(TextElement.ForegroundProperty, SystemColors.HighlightTextBrushKey);
                    }
                    else
                    {
                        run.SetResourceReference(TextElement.BackgroundProperty, ThemeKeys.CodeSurfaceBorder);
                    }

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

    /// <summary>Copying everything that is shown yields the original text given for it rather than its reformatted display.</summary>
    private void CopyOriginal(object sender, DataObjectCopyingEventArgs e)
    {
        if (Cut || !_hasOriginal || !WholeSelected())
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

    /// <summary>
    /// Updates the visibility, height, and label states of the viewport and its associated UI controls based on the current expansion state and content length.
    /// </summary>
    private void Show()
    {
        var expanded = _row.Expanded;
        if (_prose is not null)
        {
            Cut = !expanded && _lines.Length > ToolView.PreviewLines;
        }
        else
        {
            ShowText(expanded);
        }

        _viewport.MaxHeight = expanded ? ExpandedHeight : _prose is not null && Cut ? PreviewHeight : double.PositiveInfinity;
        _viewport.VerticalScrollBarVisibility = expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Element.Visibility = _text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _more.Visibility = Cut && !expanded ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_more, $"show more ({_lines.Length} lines)");
        var longerThanPreview = _lines.Length > ToolView.PreviewLines || (_prose is null && _body.Length > ToolView.PreviewChars);
        _less.Visibility = expanded && longerThanPreview && !_row.ClosesToHeader ? Visibility.Visible : Visibility.Collapsed;
        _copyAll.Visibility = Cut && expanded ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_copyAll, $"Copy all {_lines.Length} lines");
    }

    /// <summary>
    /// Shows the code or text up to the preview or open limits; a listing keeps the numbers of the lines shown. Code
    /// is filled again only when its text or numbers change, so an update that leaves them alone keeps a selection.
    /// </summary>
    private void ShowText(bool expanded)
    {
        var shown = expanded
            ? CutTo(_body, _lines, ToolView.ExpandedLines, ToolView.ExpandedChars)
            : CutTo(_body, _lines, ToolView.PreviewLines, ToolView.PreviewChars);
        if (_pre is not null)
        {
            if (shown.Length > _shown.Length && shown.StartsWith(_shown, StringComparison.Ordinal))
            {
                _pre.AppendText(shown.Substring(_shown.Length));
            }
            else if (shown != _shown)
            {
                _pre.Text = shown;
            }
        }
        else
        {
            var numbers = _listing?.Numbers.Take(shown.Count(c => c == '\n') + 1).ToArray() ?? [];
            if (shown != _shown || !numbers.SequenceEqual(_shownNumbers))
            {
                _longest = Fill(_code!, shown, numbers.Length > 0 ? numbers : null, _language, _mark, _gutter);
                _shownNumbers = numbers;
                FitWidth();
            }
        }

        _shown = shown;
        Cut = shown.Length < _body.Length;
    }

    /// <summary>
    /// Truncates the specified text to a maximum number of lines and characters to ensure it fits within defined display constraints.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="lines">The collection of lines.</param>
    /// <param name="maxLines">The max lines.</param>
    /// <param name="maxChars">The max chars.</param>
    /// <returns>The string result.</returns>
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

    /// <summary>
    /// Updates the text content of the specified button&apos;s label and assigns the corresponding automation name for accessibility.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="label">The label.</param>
    private static void SetLabel(Button link, string label)
    {
        ((TextBlock)link.Content).Text = label;
        Ui.AutomationName(link, label);
    }
}
