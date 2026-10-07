using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>What links in rendered Markdown do: web URLs open in the browser, file references in the editor.</summary>
    internal sealed class MarkdownLinks
    {
        public MarkdownLinks(Action<string> openUrl, Func<string, FileTarget?> resolveFile, Action<string, int?> openFile)
        {
            OpenUrl = openUrl;
            ResolveFile = resolveFile;
            OpenFile = openFile;
        }

        public Action<string> OpenUrl { get; }
        /// <summary>The existing file a code span or link target names, or null.</summary>
        public Func<string, FileTarget?> ResolveFile { get; }
        public Action<string, int?> OpenFile { get; }
    }

    /// <summary>
    /// Renders the parsed Markdown subset as native WPF elements with selectable prose; raw HTML is never interpreted.
    /// <see cref="Update"/> re-renders only the blocks that changed, so a streaming answer keeps the visuals (and any
    /// selection) of the blocks it already finished.
    /// </summary>
    internal static class MarkdownView
    {
        private const double BlockGap = 10;
        private const double HeadingGap = 14;
        private const double LineSpacing = 1.4;
        private const double InlineCodeScale = 0.9;

        public static FrameworkElement Render(string markdown, MarkdownLinks links, Action<string> copy)
        {
            var panel = new StackPanel();
            Update(panel, markdown, links, copy);
            return panel;
        }

        /// <summary>Brings a panel made by <see cref="Render"/> up to date with <paramref name="markdown"/>.</summary>
        public static void Update(FrameworkElement rendered, string markdown, MarkdownLinks links, Action<string> copy)
        {
            var panel = (StackPanel)rendered;
            var blocks = Markdown.Parse(markdown);
            var keys = blocks.Select(Markdown.Key).ToList();
            var keep = 0;
            while (keep < blocks.Count && keep < panel.Children.Count && Equals(((FrameworkElement)panel.Children[keep]).Tag, keys[keep])) keep++;
            while (panel.Children.Count > keep) panel.Children.RemoveAt(panel.Children.Count - 1);
            for (var i = keep; i < blocks.Count; i++)
            {
                var element = RenderBlock(blocks[i], links, copy);
                element.Tag = keys[i];
                element.Margin = new Thickness(0, i == 0 ? 0 : blocks[i] is MdHeading ? HeadingGap : BlockGap, 0, 0);
                panel.Children.Add(element);
            }
        }

        private static FrameworkElement RenderBlock(MdBlock block, MarkdownLinks links, Action<string> copy)
        {
            switch (block)
            {
                case MdParagraph paragraph:
                    return Airy(Ui.Prose(inlines =>
                    {
                        for (var i = 0; i < paragraph.Lines.Count; i++)
                        {
                            if (i > 0) inlines.Add(new LineBreak());
                            AddInlines(inlines, paragraph.Lines[i], links);
                        }
                    }));
                case MdHeading heading:
                {
                    var text = Airy(Ui.Prose(inlines => AddInlines(inlines, heading.Inlines, links)));
                    text.FontWeight = FontWeights.SemiBold;
                    var scale = heading.Level == 3 ? 1.35 : heading.Level == 4 ? 1.2 : heading.Level == 5 ? 1.1 : 1.0;
                    if (scale > 1) Ui.Small(text, scale);
                    return text;
                }
                case MdCodeBlock code:
                    return CodeBlock(code, copy);
                case MdList list:
                    return List(list, links);
                case MdTable table:
                    return Table(table, links);
                default:
                    return new TextBlock();
            }
        }

        public static void AddInlines(InlineCollection target, IReadOnlyList<MdInline> inlines, MarkdownLinks links)
        {
            foreach (var inline in inlines)
            {
                switch (inline.Kind)
                {
                    case MdInlineKind.Code:
                    {
                        var run = Ui.Small(new Run(inline.Text), InlineCodeScale);
                        run.SetResourceReference(TextElement.FontFamilyProperty, "Omp.MonoFont");
                        var file = links.ResolveFile(inline.Text);
                        if (file == null)
                        {
                            run.SetResourceReference(TextElement.BackgroundProperty, ThemeKeys.Card);
                            target.Add(run);
                        }
                        else
                        {
                            target.Add(FileLink(run, file, links));
                        }
                        break;
                    }
                    case MdInlineKind.Bold:
                        target.Add(Emphasis(new Bold(), inline, links));
                        break;
                    case MdInlineKind.Italic:
                        target.Add(Emphasis(new Italic(), inline, links));
                        break;
                    case MdInlineKind.Link:
                        target.Add(LinkInline(inline, links));
                        break;
                    default:
                        target.Add(new Run(inline.Text));
                        break;
                }
            }
        }

        /// <summary>Gives prose lines <see cref="LineSpacing"/> times its font size, following font and zoom changes.</summary>
        private static RichTextBox Airy(RichTextBox box)
        {
            box.Document.SetBinding(FlowDocument.LineHeightProperty, new Binding(nameof(Control.FontSize)) { Source = box, Converter = LineHeightOfFont.Instance });
            return box;
        }

        private sealed class LineHeightOfFont : IValueConverter
        {
            public static readonly LineHeightOfFont Instance = new LineHeightOfFont();

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (double)value * LineSpacing;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }

        private static Span Emphasis(Span span, MdInline inline, MarkdownLinks links)
        {
            if (inline.Children.Count > 0) AddInlines(span.Inlines, inline.Children, links);
            else span.Inlines.Add(new Run(inline.Text));
            return span;
        }

        private static Inline LinkInline(MdInline inline, MarkdownLinks links)
        {
            var url = inline.Url ?? "";
            if (!IsWebUrl(url))
            {
                var file = links.ResolveFile(url);
                return file == null ? new Run($"{inline.Text} ({url})") : FileLink(new Run(inline.Text), file, links);
            }
            var link = new Hyperlink(new Run(inline.Text)) { ToolTip = url };
            link.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Link);
            link.Click += (_, __) => links.OpenUrl(url);
            return link;
        }

        private static Hyperlink FileLink(Run content, FileTarget file, MarkdownLinks links)
        {
            var link = new Hyperlink(content) { ToolTip = file.Line == null ? $"Open {file.Path}" : $"Open {file.Path}:{file.Line}" };
            link.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Link);
            link.Click += (_, __) => links.OpenFile(file.Path, file.Line);
            return link;
        }

        public static bool IsWebUrl(string url) => TryWebUri(url, out _);

        /// <summary>Parses an absolute http(s) URL; <paramref name="uri"/> carries the normalized form to hand to the browser.</summary>
        public static bool TryWebUri(string url, out Uri? uri) =>
            Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri!.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static FrameworkElement CodeBlock(MdCodeBlock code, Action<string> copy)
        {
            var text = Ui.Pre(code.Code);
            text.TextWrapping = TextWrapping.NoWrap;
            text.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            var copyButton = Ui.IconButton(Glyphs.Copy, "Copy code", () => copy(code.Code));
            copyButton.Width = 22;
            copyButton.Height = 22;
            copyButton.HorizontalAlignment = HorizontalAlignment.Right;
            copyButton.VerticalAlignment = VerticalAlignment.Top;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(copyButton, Dock.Right);
            header.Children.Add(copyButton);
            header.Children.Add(Ui.Muted(code.Language ?? "", small: true));
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(text);
            return Ui.Card(stack, new Thickness(10, 2, 4, 8));
        }

        private static FrameworkElement List(MdList list, MarkdownLinks links)
        {
            var panel = new StackPanel();
            var number = list.Start;
            foreach (var item in list.Items)
            {
                var grid = new Grid { Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 2, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = Ui.Text(list.Ordered ? $"{number++}." : "•", ThemeKeys.Muted);
                marker.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(marker);
                var content = new StackPanel();
                content.Children.Add(Airy(Ui.Prose(inlines => AddInlines(inlines, item.Inlines, links))));
                foreach (var child in item.Children)
                {
                    var nested = List(child, links);
                    nested.Margin = new Thickness(0, 2, 0, 0);
                    content.Children.Add(nested);
                }
                Grid.SetColumn(content, 1);
                grid.Children.Add(content);
                panel.Children.Add(grid);
            }
            return panel;
        }

        private static FrameworkElement Table(MdTable table, MarkdownLinks links)
        {
            var grid = new Grid();
            var columns = table.Header.Count;
            foreach (var row in table.Rows) columns = Math.Max(columns, row.Count);
            for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>> { table.Header };
            rows.AddRange(table.Rows);
            for (var r = 0; r < rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var cellRow = rows[r][c];
                    var align = c < table.Aligns.Count ? table.Aligns[c] : MdAlign.Left;
                    var alignment = align == MdAlign.Right ? TextAlignment.Right : align == MdAlign.Center ? TextAlignment.Center : TextAlignment.Left;
                    var cell = Ui.SizedProse(inlines => AddInlines(inlines, cellRow, links), wrap: true, maxWidth: 360, alignment: alignment);
                    if (r == 0) cell.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                    var border = new Border
                    {
                        Child = cell,
                        Padding = new Thickness(8, 3, 8, 3),
                        BorderThickness = new Thickness(0, 0, c < columns - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    }.Theme(Border.BorderBrushProperty, ThemeKeys.CardBorder);
                    Grid.SetRow(border, r);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }
            }
            var frame = new Border { Child = grid, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left }
                .Theme(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            var scroller = new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Ui.BubbleWheel(scroller);
            return scroller;
        }
    }
}
