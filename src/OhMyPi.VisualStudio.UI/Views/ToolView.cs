using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>
    /// A tool call: a header row (status icon, name, primary argument, detail right-aligned) and below it, indented to
    /// the name, the parameters the header does not show and the result, falling back to the generic parts when the
    /// specialized renderer throws.
    /// </summary>
    internal static class ToolView
    {
        private const int PreviewLines = 6;
        private const int PreviewChars = 4000;

        /// <summary>Most lines and characters "show more" puts on screen; the rest stays one "Copy all" away.</summary>
        internal const int ExpandedLines = 2000;
        internal const int ExpandedChars = 200_000;

        /// <summary>Longest diff shown inline; the native diff shows any size.</summary>
        internal const int DiffLines = 2000;

        /// <summary>Diff lines shown before "show more".</summary>
        private const int DiffPreviewLines = 20;

        public static FrameworkElement Render(ToolItem item, RenderContext ctx) => new ToolRow(item, ctx);

        /// <summary>A tool row; while the tool runs, newer output goes into the open output box instead of a new row.</summary>
        private sealed class ToolRow : Border, ILiveView
        {
            private ToolItem _item;
            private TruncatedText? _liveOutput;

            public ToolRow(ToolItem item, RenderContext ctx)
            {
                _item = item;
                System.Windows.Automation.AutomationProperties.SetAutomationId(this, "tool-row");
                Child = Build(item, ctx, this);
            }

            public bool IsLive => _item.Status == ToolStatus.Running;

            /// <summary>Registers the output box of a running tool's body once the body is built.</summary>
            public void Track(TruncatedText output) => _liveOutput = output;

            public bool TryUpdate(TranscriptItem item)
            {
                if (!(item is ToolItem next) || next.Id != _item.Id || next.Status != ToolStatus.Running || _item.Status != ToolStatus.Running) return false;
                if (next.Name != _item.Name || !JToken.DeepEquals(next.Args, _item.Args)) return false;
                _item = next;
                _liveOutput?.SetText(next.Result?.Text ?? next.Partial ?? "");
                return true;
            }
        }

        /// <summary>Gap between the header and the first part below it, and between those parts.</summary>
        private const double PartGap = 6;

        private static FrameworkElement Build(ToolItem item, RenderContext ctx, ToolRow row)
        {
            var head = ToolFormat.Headline(item, ctx.Cwd);
            var icon = StatusIcon(head.State);
            var header = Header(item, head, ctx);
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(header, 1);
            layout.Children.Add(icon);
            layout.Children.Add(header);
            var body = Body(item, ctx, row);
            if (body != null)
            {
                Grid.SetRow(body, 1);
                Grid.SetColumn(body, 1);
                layout.Children.Add(body);
            }
            return layout;
        }

        private static TextBlock StatusIcon(ToolState state)
        {
            string glyph, tip;
            object brush;
            switch (state)
            {
                case ToolState.Running:
                    (glyph, brush, tip) = (Glyphs.Sync, ThemeKeys.Progress, "running");
                    break;
                case ToolState.Failed:
                    (glyph, brush, tip) = (Glyphs.Cancel, ThemeKeys.Error, "failed");
                    break;
                case ToolState.Background:
                    (glyph, brush, tip) = (Glyphs.Clock, ThemeKeys.Progress, "started in the background, still running");
                    break;
                default:
                    (glyph, brush, tip) = (Glyphs.Check, ThemeKeys.Success, "done");
                    break;
            }
            var icon = Ui.Icon(glyph, brush, 11);
            icon.Margin = new Thickness(0, 0, 6, 0);
            icon.ToolTip = tip;
            return icon;
        }

        /// <summary>The call's only file when the header's primary argument is exactly that file's link text: the header then is the link, so the path is not shown twice.</summary>
        private static ToolFile? HeaderFile(ToolItem item, RenderContext ctx, ToolHeadline head)
        {
            if (ToolFormat.PickRenderer(item.Name) != RendererKind.File) return null;
            var files = ToolFormat.Files(item, ctx.Cwd);
            return files.Count == 1 && ToolFormat.FileLinkLabel(files[0].Path, files[0].Line, ctx.Cwd) == head.Primary ? files[0] : null;
        }

        /// <summary>Name and primary argument on the left, the detail (hits, exit code, duration) at the right edge; the primary argument is what gives way.</summary>
        private static FrameworkElement Header(ToolItem item, ToolHeadline head, RenderContext ctx)
        {
            var header = new DockPanel { LastChildFill = true };
            if (head.Detail.Length > 0)
            {
                var detail = Ui.Muted(head.Detail);
                detail.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(detail, Dock.Right);
                header.Children.Add(detail);
            }
            var name = Ui.Text(head.Name, weight: FontWeights.SemiBold, small: true);
            name.MaxWidth = 180;
            name.Margin = new Thickness(0, 0, 8, 0);
            name.ToolTip = item.Name;
            DockPanel.SetDock(name, Dock.Left);
            header.Children.Add(name);
            var inHeader = HeaderFile(item, ctx, head);
            FrameworkElement primary;
            if (inHeader != null)
            {
                var link = (Button)FileLink(ctx, inHeader.Path, inHeader.Line);
                link.HorizontalAlignment = HorizontalAlignment.Left;
                primary = link;
            }
            else
            {
                var text = Ui.Muted(head.Primary);
                text.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.MonoFont");
                var full = ToolFormat.PickRenderer(item.Name) == RendererKind.Shell ? ToolFormat.ShellCommand(item) : head.Primary;
                if (full.Length > 0) text.ToolTip = full;
                primary = text;
            }
            header.Children.Add(primary);
            var state = ToolFormat.StateOf(item);
            Ui.AutomationName(header, $"{item.Name} {head.Primary} {StateName(state)} {head.Detail}");
            System.Windows.Automation.AutomationProperties.SetAutomationId(header, "tool-header");
            return header;
        }

        private static string StateName(ToolState state) =>
            state == ToolState.Running ? "running" : state == ToolState.Failed ? "failed" : state == ToolState.Background ? "running in the background" : "done";

        /// <summary>The parts under the header, one per row; null when the tool has nothing to show.</summary>
        private static StackPanel? Body(ToolItem item, RenderContext ctx, ToolRow row)
        {
            IEnumerable<UIElement?> parts;
            try
            {
                parts = Specialized(item, ctx, row).ToList();
            }
            catch (Exception error)
            {
                ctx.LogError($"Showing the {item.Name} tool row failed", error);
                var fallback = Ui.Text($"Specialized view failed ({error.Message}); showing raw arguments and output.", ThemeKeys.Muted, wrap: true, small: true);
                parts = new UIElement?[] { fallback }.Concat(Generic(item, ctx, row)).ToList();
            }
            var present = parts.Where(p => p != null).Cast<UIElement>().ToList();
            if (present.Count == 0) return null;
            var column = new StackPanel();
            foreach (var part in present)
            {
                if (part is FrameworkElement element) element.Margin = new Thickness(element.Margin.Left, PartGap, element.Margin.Right, element.Margin.Bottom);
                column.Children.Add(part);
            }
            System.Windows.Automation.AutomationProperties.SetAutomationId(column, "tool-body");
            return column;
        }

        private static IEnumerable<UIElement?> Specialized(ToolItem item, RenderContext ctx, ToolRow row)
        {
            switch (ToolFormat.PickRenderer(item.Name))
            {
                case RendererKind.File: return FileBody(item, ctx, row);
                case RendererKind.Search: return Generic(item, ctx, row);
                case RendererKind.Shell: return ShellBody(item, ctx, row);
                case RendererKind.Task: return TaskBody(item, ctx, row);
                case RendererKind.Lsp: return LspBody(item, ctx, row);
                case RendererKind.Mcp: return new UIElement?[] { Ui.Muted(item.Name) }.Concat(Generic(item, ctx, row));
                default: return Generic(item, ctx, row);
            }
        }

        private static IEnumerable<UIElement?> Generic(ToolItem item, RenderContext ctx, ToolRow row) => new[] { ArgsSection(item, ctx), OutputSection(item, ctx, row) };

        /// <summary>
        /// A link per file the tool names, plus Diff where the host can show a real one: the file is tracked, or the
        /// tool recorded its old text (passed to the host as the diff's left side).
        /// </summary>
        private static IEnumerable<UIElement?> FileBody(ToolItem item, RenderContext ctx, ToolRow row)
        {
            var inHeader = HeaderFile(item, ctx, ToolFormat.Headline(item, ctx.Cwd));
            var diff = ToolFormat.CollectDiff(item.Result?.Details);
            var changesFiles = diff != null || item.Name == "write" || item.Name == "edit" || item.Name == "ast_edit";
            var files = ToolFormat.Files(item, ctx.Cwd);
            var links = new List<UIElement?>();
            foreach (var file in files)
            {
                if (inHeader?.Path != file.Path) links.Add(FileLink(ctx, file.Path, file.Line));
                if (!changesFiles || item.Status == ToolStatus.Running || (file.OldText == null && !ctx.IsTracked(file.Path))) continue;
                var name = files.Count == 1 ? "Diff" : $"Diff {ToolFormat.FileLinkLabel(file.Path, null, ctx.Cwd)}";
                var link = Ui.Link(name, () => ctx.OpenDiff(file.Path, file.OldText), "Open native diff");
                links.Add(link);
            }
            var body = new List<UIElement?> { ActionsLine(links.ToArray()) };
            if (diff != null) body.Add(DiffSection(item, ctx, diff));
            if (item.Name != "read" && diff == null) body.Add(ArgsSection(item, ctx));
            if (item.Name == "read" || item.Result?.IsError == true || diff == null) body.Add(OutputSection(item, ctx, row));
            return body;
        }

        private static IEnumerable<UIElement?> ShellBody(ToolItem item, RenderContext ctx, ToolRow row)
        {
            var command = ToolFormat.ShellCommand(item);
            var body = new List<UIElement?>();
            if (!ToolFormat.FitsHeader(command)) body.Add(Quiet(ctx, $"{item.Id}:cmd:more", command, "Command"));
            var parameters = ToolFormat.ShellParameters(item);
            if (parameters.Length > 0) body.Add(Quiet(ctx, $"{item.Id}:params:more", parameters, "Parameters"));
            body.Add(OutputSection(item, ctx, row, tail: true));
            return body;
        }

        private static IEnumerable<UIElement?> TaskBody(ToolItem item, RenderContext ctx, ToolRow row)
        {
            var names = ToolFormat.TaskNames(item.Args?.Type == JTokenType.Object ? item.Args["tasks"] : null);
            var list = names.Count == 0 ? null : Ui.Column(0, names.Select(n => (UIElement)Ui.Link("• " + n, ctx.ShowAgents, "Show agents")).ToArray());
            return new[] { ActionsLine(Ui.Link("Agents", ctx.ShowAgents, "Show the Agents section")), list, OutputSection(item, ctx, row) };
        }

        private static IEnumerable<UIElement?> LspBody(ToolItem item, RenderContext ctx, ToolRow row)
        {
            var args = item.Args as JObject;
            var file = ToolFormat.Str(args?["file"]) ?? ToolFormat.Str(args?["path"]);
            var line = args?["line"]?.Type == JTokenType.Integer ? (int?)args["line"]!.Value<int>() : null;
            return new[] { ActionsLine(file != null ? FileLink(ctx, file, line) : null), ArgsSection(item, ctx), OutputSection(item, ctx, row) };
        }

        private static UIElement? ActionsLine(params UIElement?[] children)
        {
            var present = children.Where(c => c != null).ToArray();
            if (present.Length == 0) return null;
            var row = new WrapPanel();
            foreach (var child in present)
            {
                if (child is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 12, 0);
                row.Children.Add(child);
            }
            return row;
        }

        /// <summary>A link that opens <paramref name="path"/> (as OMP wrote it; the host resolves it) at <paramref name="line"/>.</summary>
        private static UIElement FileLink(RenderContext ctx, string path, int? line)
        {
            var label = ToolFormat.FileLinkLabel(path, line, ctx.Cwd);
            return Ui.Link(label, () => ctx.OpenFile(path, line), $"Open {(line.HasValue ? $"{path}:{line}" : path)}", mono: true);
        }

        /// <summary>The call's arguments as indented JSON; null for a call without any.</summary>
        private static UIElement? ArgsSection(ToolItem item, RenderContext ctx)
        {
            if (item.Args == null || (item.Args is JContainer container && !container.HasValues)) return null;
            return Quiet(ctx, $"{item.Id}:args:more", item.Args.ToString(Formatting.Indented), "Input");
        }

        /// <summary>Input text the header does not show: muted, never on the code background.</summary>
        private static UIElement Quiet(RenderContext ctx, string key, string text, string name)
        {
            var input = new TruncatedText(ctx, key, text, tail: false, ThemeKeys.Muted, code: true);
            Ui.AutomationName(input.Box, name);
            return input.Element;
        }

        /// <summary>
        /// The result: a diff gets its colored lines, one line shows as plain text and several lines as code on the code
        /// background; single-line JSON is indented for reading while copying still yields the original text.
        /// </summary>
        private static UIElement? OutputSection(ToolItem item, RenderContext ctx, ToolRow row, bool tail = false)
        {
            var text = item.Result?.Text ?? item.Partial ?? "";
            var running = item.Status == ToolStatus.Running;
            if (text.Length == 0 && !running) return null;
            var failed = item.Result?.IsError == true;
            if (!running && !failed && ToolFormat.LooksLikeDiff(text!)) return DiffView(ctx, $"{item.Id}:out:more", text!);
            var pretty = running ? null : JsonText.Pretty(text!);
            var output = new TruncatedText(ctx, $"{item.Id}:out:more", pretty ?? text!, tail, failed ? ThemeKeys.Error : running ? ThemeKeys.Muted : null, boxed: true, original: pretty == null ? null : text);
            Ui.AutomationName(output.Box, "Result");
            if (running) row.Track(output);
            return output.Element;
        }

        /// <summary>
        /// Selectable text cut to its head (or tail) with a "show more" link. Expanded text is capped at
        /// <see cref="ExpandedLines"/> / <see cref="ExpandedChars"/>, with "Copy all" for the rest.
        /// </summary>
        internal sealed class TruncatedText
        {
            private readonly RenderContext _ctx;
            private readonly string _key;
            private readonly bool _tail;
            private readonly bool _boxed;
            private readonly bool _code;
            private readonly Border _frame = new Border();
            private readonly TextBox _pre;
            private readonly Button _more;
            private readonly Button _copyAll;
            private string _text = "";
            private string _original = "";
            private bool _cut;

            /// <param name="brushKey">Text color; null keeps the body color.</param>
            /// <param name="boxed">Whether text of several lines sits on the code background; one line, and any text when false, stays plain.</param>
            /// <param name="original">What copying yields when it differs from <paramref name="text"/> (text shown reformatted); null means the text itself.</param>
            /// <param name="code">Whether even one line is code that scrolls sideways instead of wrapping.</param>
            public TruncatedText(RenderContext ctx, string key, string text, bool tail, object? brushKey, bool boxed = false, string? original = null, bool code = false)
            {
                _ctx = ctx;
                _key = key;
                _tail = tail;
                _boxed = boxed;
                _code = code;
                _pre = Ui.Pre("", brushKey);
                FileClicks.Attach(_pre, ctx);
                DataObject.AddCopyingHandler(_pre, CopyOriginal);
                _frame.Child = _pre;
                _more = Ui.Link("show more", () =>
                {
                    _ctx.Open.SetOpen(_key, true);
                    Show();
                });
                _more.HorizontalAlignment = HorizontalAlignment.Left;
                _copyAll = Ui.Link("Copy all", () => _ctx.Copy(_original));
                _copyAll.HorizontalAlignment = HorizontalAlignment.Left;
                var panel = new StackPanel();
                if (tail)
                {
                    panel.Children.Add(_more);
                    panel.Children.Add(_copyAll);
                    panel.Children.Add(_frame);
                }
                else
                {
                    panel.Children.Add(_frame);
                    panel.Children.Add(_more);
                    panel.Children.Add(_copyAll);
                }
                Element = panel;
                SetText(text, original);
            }

            public FrameworkElement Element { get; }

            /// <summary>The selectable text box, for naming it to assistive technology.</summary>
            public TextBox Box => _pre;

            /// <summary>Replaces the text; growth of what is on screen is appended, so a selection in it survives.</summary>
            public void SetText(string text, string? original = null)
            {
                _text = text.TrimEnd('\r', '\n');
                _original = original ?? text;
                Show();
            }

            /// <summary>Copying everything that is shown yields the original text rather than its reformatted display.</summary>
            private void CopyOriginal(object sender, DataObjectCopyingEventArgs e)
            {
                if (_cut || _original == _text || _pre.SelectionLength == 0 || _pre.SelectionLength != _pre.Text.Length) return;
                e.DataObject.SetData(DataFormats.UnicodeText, _original);
                e.DataObject.SetData(DataFormats.Text, _original);
            }

            private void Show()
            {
                var lines = _text.Split('\n');
                var expanded = _ctx.Open.IsOpen(_key) == true;
                var shown = expanded ? Cut(_text, lines, ExpandedLines, ExpandedChars) : Cut(_text, lines, PreviewLines, PreviewChars);
                if (shown.Length > _pre.Text.Length && shown.StartsWith(_pre.Text, StringComparison.Ordinal)) _pre.AppendText(shown.Substring(_pre.Text.Length));
                else if (shown != _pre.Text) _pre.Text = shown;
                _cut = shown.Length < _text.Length;
                Element.Visibility = _text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                Frame(lines.Length > 1 || _code);
                _more.Visibility = _cut && !expanded ? Visibility.Visible : Visibility.Collapsed;
                SetLabel(_more, $"show more ({lines.Length} lines)");
                _copyAll.Visibility = _cut && expanded ? Visibility.Visible : Visibility.Collapsed;
                SetLabel(_copyAll, $"Copy all {lines.Length} lines");
            }

            /// <summary>One line reads as plain wrapping text; several lines are code: no wrapping, scrolled sideways, on the code background when boxed.</summary>
            private void Frame(bool multiline)
            {
                _pre.TextWrapping = multiline ? TextWrapping.NoWrap : TextWrapping.Wrap;
                _pre.HorizontalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                if (_boxed && multiline) _frame.Styled("Omp.CodeBlock");
                else
                {
                    _frame.ClearValue(FrameworkElement.StyleProperty);
                    _frame.ClearValue(Border.PaddingProperty);
                }
            }

            private string Cut(string text, string[] lines, int maxLines, int maxChars)
            {
                if (lines.Length <= maxLines && text.Length <= maxChars) return text;
                var shown = string.Join("\n", _tail ? lines.Skip(Math.Max(0, lines.Length - maxLines)) : lines.Take(maxLines));
                if (shown.Length > maxChars) shown = _tail ? shown.Substring(shown.Length - maxChars) : shown.Substring(0, maxChars);
                return shown;
            }

            private static void SetLabel(Button link, string label)
            {
                ((TextBlock)link.Content).Text = label;
                Ui.AutomationName(link, label);
            }
        }

        private static UIElement? DiffSection(ToolItem item, RenderContext ctx, string diff) => DiffView(ctx, $"{item.Id}:diff:more", diff);

        /// <summary>
        /// A unified diff as selectable lines on full-width green (added) and red (removed) backgrounds, the text in
        /// the body color with only the +/- sign colored: the first <see cref="DiffPreviewLines"/>
        /// with "show more" (remembered under <paramref name="key"/>), which shows up to <see cref="DiffLines"/>, with "Copy all" past that.
        /// </summary>
        private static UIElement DiffView(RenderContext ctx, string key, string diff)
        {
            var lines = diff.TrimEnd('\n').Split('\n');
            var host = new ContentControl { Focusable = false };

            void Show()
            {
                var limit = ctx.Open.IsOpen(key) == true ? DiffLines : DiffPreviewLines;
                var shown = lines.Length > limit ? lines.Take(limit).ToArray() : lines;
                var block = Ui.SizedProse(inlines => Ui.AddLines(inlines, string.Join("\n", shown.Select(line => line.Length == 0 ? " " : line))), wrap: false);
                block.SetResourceReference(TextElement.FontFamilyProperty, "Omp.MonoFont");
                var scroller = new ScrollViewer { Content = block, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
                Ui.BubbleWheel(scroller);
                var box = ((Panel)block).Children.OfType<RichTextBox>().Single();
                box.Document.Blocks.Clear();
                var spans = ToolFormat.ChangedSpans(shown);
                for (var i = 0; i < shown.Length; i++) box.Document.Blocks.Add(DiffLine(shown[i], spans[i]));
                box.SetBinding(FrameworkElement.MinWidthProperty, new Binding(nameof(ScrollViewer.ViewportWidth)) { Source = scroller });
                var sizer = ((Panel)block).Children.OfType<TextBlock>().Single();
                box.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = sizer, Converter = new Widen(2 * DiffInset + 4) });
                var framed = new Border { Child = scroller, Padding = new Thickness(0, 6, 0, 6) }.Styled("Omp.CodeBlock");
                if (shown.Length == lines.Length)
                {
                    host.Content = framed;
                    return;
                }
                UIElement more = limit == DiffPreviewLines
                    ? Ui.Link($"show more ({lines.Length} lines)", () =>
                    {
                        ctx.Open.SetOpen(key, true);
                        Show();
                    })
                    : Ui.Column(2, Ui.Muted($"Showing the first {DiffLines} of {lines.Length} lines."), Ui.Link($"Copy all {lines.Length} diff lines", () => ctx.Copy(diff)));
                if (more is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Left;
                host.Content = Ui.Column(2, framed, more);
            }

            Show();
            return host;
        }

        /// <summary>Horizontal room inside the tint of a diff line, around its text.</summary>
        private const double DiffInset = 8;

        /// <summary>Adds a fixed width to a bound size.</summary>
        private sealed class Widen : IValueConverter
        {
            private readonly double _extra;

            public Widen(double extra) => _extra = extra;

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (double)value + _extra;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }

        /// <param name="changed">Within the text after the sign, the part that differs from the paired line; drawn on a stronger tint.</param>
        private static Paragraph DiffLine(string line, (int Start, int Length)? changed)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(DiffInset, 0, DiffInset, 0) };
            var kind = ToolFormat.DiffLineClass(line);
            if (kind == DiffLineKind.Add || kind == DiffLineKind.Delete)
            {
                var added = kind == DiffLineKind.Add;
                var color = added ? ThemeKeys.Success : ThemeKeys.Error;
                paragraph.SetResourceReference(TextElement.BackgroundProperty, added ? ThemeKeys.DiffAddedLine : ThemeKeys.DiffRemovedLine);
                var sign = new Run(line.Substring(0, 1));
                sign.SetResourceReference(TextElement.ForegroundProperty, color);
                paragraph.Inlines.Add(sign);
                var text = line.Substring(1);
                if (changed.HasValue)
                {
                    var (start, length) = changed.Value;
                    if (start > 0) paragraph.Inlines.Add(new Run(text.Substring(0, start)));
                    var word = new Run(text.Substring(start, length));
                    word.SetResourceReference(TextElement.BackgroundProperty, added ? ThemeKeys.DiffAddedWord : ThemeKeys.DiffRemovedWord);
                    paragraph.Inlines.Add(word);
                    if (start + length < text.Length) paragraph.Inlines.Add(new Run(text.Substring(start + length)));
                }
                else if (text.Length > 0)
                {
                    paragraph.Inlines.Add(new Run(text));
                }
                return paragraph;
            }
            var run = new Run(line.Length == 0 ? " " : line);
            if (kind == DiffLineKind.Hunk || kind == DiffLineKind.Meta) run.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Muted);
            paragraph.Inlines.Add(run);
            return paragraph;
        }
    }
}
