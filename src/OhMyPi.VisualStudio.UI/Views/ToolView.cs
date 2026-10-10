using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// A tool call: a header row (status icon, name, primary argument, detail right-aligned) and below it, indented to
/// the name, a short dim preview of the result. Its texts open together on "show more", remembered per call; a
/// finished read shows only its header until opened. The generic parts stand in when the specialized renderer
/// throws.
/// </summary>
internal static class ToolView
{
    internal const int PreviewLines = 5;
    internal const int PreviewChars = 2000;

    /// <summary>Width of the status-icon column at the left of a tool row, which the answer under the tools lines up with.</summary>
    internal const double IconColumn = 24;

    /// <summary>Size of monospace text in tool rows relative to the body text.</summary>
    private const double ToolTextScale = 0.9;

    /// <summary>Most lines and characters "show more" puts on screen; the rest stays one "Copy all" away.</summary>
    internal const int ExpandedLines = 2000;
    internal const int ExpandedChars = 200_000;

    /// <summary>Longest diff shown inline; the native diff shows any size.</summary>
    internal const int DiffLines = 2000;

    /// <summary>Diff lines shown before "show more".</summary>
    private const int DiffPreviewLines = 20;

    public static FrameworkElement Render(ToolItem item, RenderContext ctx) => new ToolRow(item, ctx);

    /// <summary>
    /// A tool row; while the tool runs, newer output goes into the open output box instead of a new row. Its texts
    /// and the sections shown only while open follow one remembered key.
    /// </summary>
    internal sealed class ToolRow : Border, ILiveView
    {
        private readonly RenderContext _ctx;
        private readonly List<CodeBlock> _texts = new List<CodeBlock>();
        private readonly List<UIElement> _details = new List<UIElement>();
        private readonly List<UIElement> _closedOnly = new List<UIElement>();
        private ToolItem _item;
        private CodeBlock? _liveOutput;

        public ToolRow(ToolItem item, RenderContext ctx)
        {
            _ctx = ctx;
            _item = item;
            Key = $"{item.Id}:open";
            System.Windows.Automation.AutomationProperties.SetAutomationId(this, "tool-row");
            Child = Build(item, ctx, this);
        }

        /// <summary>The remembered expand state of the whole row.</summary>
        public string Key { get; }

        public RenderContext Context => _ctx;

        public bool Expanded => _ctx.Open.IsOpen(Key) == true;

        public bool IsLive => _item.Status == ToolStatus.Running;

        /// <summary>Registers the output box of a running tool's body once the body is built.</summary>
        public void Track(CodeBlock output) => _liveOutput = output;

        /// <summary>Whether the row hides sections while closed; it then closes by its own "collapse", so its texts offer none.</summary>
        public bool ClosesToHeader => _details.Count > 0;

        /// <summary>Registers a text that is cut while the row is collapsed.</summary>
        public void Register(CodeBlock text) => _texts.Add(text);

        /// <summary>Registers a section shown only while the row is expanded.</summary>
        public void RegisterDetail(UIElement detail)
        {
            detail.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
            _details.Add(detail);
        }

        /// <summary>Registers an element shown only while the row is closed, such as the link that opens it.</summary>
        public void RegisterClosedOnly(UIElement element)
        {
            element.Visibility = Expanded ? Visibility.Collapsed : Visibility.Visible;
            _closedOnly.Add(element);
        }

        /// <summary>Opens the whole row: every cut text shows up to its expanded limit and the sections shown only while open appear.</summary>
        public void Expand()
        {
            _ctx.Open.SetOpen(Key, true);
            foreach (var text in _texts)
            {
                text.Refresh();
            }

            foreach (var detail in _details)
            {
                detail.Visibility = Visibility.Visible;
            }

            foreach (var element in _closedOnly)
            {
                element.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Closes the row again: texts go back to their preview and the sections shown only while open hide.</summary>
        public void Collapse()
        {
            _ctx.Open.SetOpen(Key, false);
            foreach (var text in _texts)
            {
                text.Refresh();
            }

            foreach (var detail in _details)
            {
                detail.Visibility = Visibility.Collapsed;
            }

            foreach (var element in _closedOnly)
            {
                element.Visibility = Visibility.Visible;
            }
        }

        public bool TryUpdate(TranscriptItem item)
        {
            if (!(item is ToolItem next) || next.Id != _item.Id || next.Status != ToolStatus.Running || _item.Status != ToolStatus.Running)
            {
                return false;
            }

            if (next.Name != _item.Name || !JToken.DeepEquals(next.Args, _item.Args))
            {
                return false;
            }

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
        var card = new Border { Margin = new Thickness(0, PartGap, 0, 0) }.Styled(head.State == ToolState.Failed ? "Omp.OutputBlockFailed" : "Omp.OutputBlock");
        var headerOnly = item.Name == "read" && head.State == ToolState.Done;
        if (headerOnly)
        {
            row.RegisterDetail(card);
        }

        var body = Body(item, ctx, row);
        if (body is not null)
        {
            card.Child = body;
            if (!headerOnly)
            {
                card.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = body });
            }

            Grid.SetRow(card, 1);
            Grid.SetColumn(card, 1);
            layout.Children.Add(card);
            if (headerOnly)
            {
                var lines = ToolFormat.ProseText(item.Result?.Text ?? "").TrimEnd('\r', '\n').Split('\n').Length;
                var open = Ui.Link($"show more ({lines} lines)", row.Expand);
                var close = Ui.Link("collapse", row.Collapse);
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var link in new[] { open, close })
                {
                    link.HorizontalAlignment = HorizontalAlignment.Left;
                    link.Margin = new Thickness(0, 2, 0, 0);
                    Grid.SetRow(link, 2);
                    Grid.SetColumn(link, 1);
                    layout.Children.Add(link);
                }

                row.RegisterClosedOnly(open);
                row.RegisterDetail(close);
            }
        }

        return layout;
    }

    /// <summary>
    /// Creates a TextBlock icon configured with a glyph, color, and tooltip based on the specified tool state.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The text block result.</returns>
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
        icon.Width = IconColumn - 6;
        icon.Margin = new Thickness(0, 0, 6, 0);
        icon.ToolTip = tip;

        return icon;
    }

    /// <summary>
    /// The call's only file when the header's primary argument is that file's link text, alone or followed by a
    /// selector (<c>:12-20</c>, <c>:raw</c>): the header then is the link, so the path is not shown twice. A
    /// selector keeps the link labeled with the whole argument; a bare path is labeled with the line it opens.
    /// </summary>
    private static ToolFile? HeaderFile(ToolItem item, RenderContext ctx, ToolHeadline head)
    {
        if (ToolFormat.PickRenderer(item.Name) != RendererKind.File)
        {
            return null;
        }

        var files = ToolFormat.Files(item, ctx.Cwd);
        if (files.Count != 1)
        {
            return null;
        }

        var label = ToolFormat.FileLinkLabel(files[0].Path, null, ctx.Cwd);

        return head.Primary == label || head.Primary.StartsWith(label + ":", StringComparison.Ordinal) ? files[0] : null;
    }

    /// <summary>Name in a pill and primary argument on the left, the detail (hits, exit code, duration) at the right edge; the primary argument is what gives way.</summary>
    private static FrameworkElement Header(ToolItem item, ToolHeadline head, RenderContext ctx)
    {
        var header = new DockPanel { LastChildFill = true };
        if (head.Detail.Length > 0)
        {
            var detail = Ui.Subtle(head.Detail);
            detail.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(detail, Dock.Right);
            header.Children.Add(detail);
        }
        var name = Ui.Small(Ui.Text(head.Name, weight: FontWeights.SemiBold), 0.9);
        var pill = new Border { Child = name, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }
            .Theme(Border.BackgroundProperty, ThemeKeys.CodeSurface)
            .Theme(Border.BorderBrushProperty, ThemeKeys.CodeSurfaceBorder);
        pill.MaxWidth = 180;
        pill.Margin = new Thickness(0, 0, 8, 0);
        pill.ToolTip = item.Name;
        DockPanel.SetDock(pill, Dock.Left);
        header.Children.Add(pill);
        var inHeader = HeaderFile(item, ctx, head);
        FrameworkElement primary;
        if (inHeader is not null)
        {
            var bare = head.Primary == ToolFormat.FileLinkLabel(inHeader.Path, null, ctx.Cwd);
            var link = (Button)FileLink(ctx, inHeader.Path, inHeader.Line, bare ? null : head.Primary);
            link.HorizontalAlignment = HorizontalAlignment.Left;
            primary = link;
        }
        else
        {
            var shell = ToolFormat.PickRenderer(item.Name) == RendererKind.Shell;
            var described = shell && ToolFormat.Description(item) is not null;
            var text = Ui.Text(head.Primary, ThemeKeys.Muted, small: true);
            if (!described)
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.MonoFont");
                Ui.Small(text, ToolTextScale);
            }

            var full = shell ? ToolFormat.ShellCommand(item) : head.Primary;
            if (full.Length > 0)
            {
                text.ToolTip = full;
            }

            primary = text;
        }
        header.Children.Add(primary);
        var state = ToolFormat.StateOf(item);
        Ui.AutomationName(header, $"{item.Name} {head.Primary} {StateName(state)} {head.Detail}");
        System.Windows.Automation.AutomationProperties.SetAutomationId(header, "tool-header");

        return header;
    }

    /// <summary>
    /// Converts a ToolState enumeration value into its corresponding human-readable string representation.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The string result.</returns>
    private static string StateName(ToolState state) =>
        state == ToolState.Running ? "running" : state == ToolState.Failed ? "failed" : state == ToolState.Background ? "running in the background" : "done";

    /// <summary>
    /// The parts under the header, one per row: the first error line and the parts the specialized view gives,
    /// each rule between them hidden with the part under it, and the whole body hidden with its only part; null when
    /// the tool has nothing to show.
    /// </summary>
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
        var firstErrorLine = ToolFormat.Headline(item, ctx.Cwd).Error;
        var present = new UIElement?[] { firstErrorLine is null ? null : Ui.Text(firstErrorLine, ThemeKeys.Foreground, wrap: true, small: true) }
            .Concat(parts).Where(p => p != null).Cast<UIElement>().ToList();
        if (present.Count == 0)
        {
            return null;
        }

        var column = new StackPanel();
        foreach (var part in present)
        {
            if (column.Children.Count > 0)
            {
                var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-10, PartGap, -10, PartGap) }
                    .Theme(Border.BorderBrushProperty, ThemeKeys.CodeSurfaceBorder);
                rule.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = part });
                column.Children.Add(rule);
            }

            column.Children.Add(part);
        }
        if (present.Count == 1)
        {
            column.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = present[0] });
        }

        System.Windows.Automation.AutomationProperties.SetAutomationId(column, "tool-body");

        return column;
    }

    /// <summary>
    /// Returns a collection of UI elements tailored to the specific renderer kind associated with the provided tool item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="row">The row.</param>
    /// <returns>A collection of ienumerable items.</returns>
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

    /// <summary>
    /// Generates a collection of UI elements comprising the arguments and output sections for a specified tool item within the given render context and row.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="row">The row.</param>
    /// <returns>A collection of ienumerable items.</returns>
    private static IEnumerable<UIElement?> Generic(ToolItem item, RenderContext ctx, ToolRow row) => new[] { ArgsSection(item, row), OutputSection(item, ctx, row) };

    /// <summary>
    /// A link per file the tool names, plus Diff where the host can show a real one: the file is tracked, or the
    /// tool recorded its old text (passed to the host as the diff's left side).
    /// </summary>
    private static IEnumerable<UIElement?> FileBody(ToolItem item, RenderContext ctx, ToolRow row)
    {
        var inHeader = HeaderFile(item, ctx, ToolFormat.Headline(item, ctx.Cwd));
        var diff = ToolFormat.CollectDiff(item.Result?.Details);
        var changesFiles = diff is not null || item.Name == "write" || item.Name == "edit" || item.Name == "ast_edit";
        var files = ToolFormat.Files(item, ctx.Cwd);
        var links = new List<UIElement?>();
        foreach (var file in files)
        {
            if (inHeader?.Path != file.Path)
            {
                links.Add(FileLink(ctx, file.Path, file.Line));
            }

            if (!changesFiles || item.Status == ToolStatus.Running || (file.OldText is null && !ctx.IsTracked(file.Path)))
            {
                continue;
            }

            var name = files.Count == 1 ? "Diff" : $"Diff {ToolFormat.FileLinkLabel(file.Path, null, ctx.Cwd)}";
            var link = Ui.Link(name, () => ctx.OpenDiff(file.Path, file.OldText), "Open native diff");
            links.Add(link);
        }
        var body = new List<UIElement?> { ActionsLine([.. links]) };
        if (diff is not null)
        {
            body.Add(DiffSection(item, ctx, diff));
        }

        if (item.Name != "read" && diff is null)
        {
            body.Add(ArgsSection(item, row));
        }

        if (item.Name == "read" || item.Result?.IsError == true || diff is null)
        {
            body.Add(OutputSection(item, ctx, row));
        }

        return body;
    }

    /// <summary>
    /// Generates a collection of UI elements representing the shell command, parameters, and output section for a specified tool item within the given render context and row.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="row">The row.</param>
    /// <returns>A collection of ienumerable items.</returns>
    private static IEnumerable<UIElement?> ShellBody(ToolItem item, RenderContext ctx, ToolRow row)
    {
        var command = ToolFormat.ShellCommand(item);
        var body = new List<UIElement?>();
        var showCommand = ToolFormat.Description(item) is not null || !ToolFormat.FitsHeader(command);
        var parameters = ToolFormat.ShellParameters(item);
        if (showCommand || parameters.Length > 0)
        {
            var parametersBox = parameters.Length > 0 ? Quiet(row, parameters, "Parameters", language: "yaml") : null;
            var input = showCommand ? Quiet(row, command, "Command", language: ToolFormat.ShellLanguage(item), trailer: parametersBox) : parametersBox!;
            body.Add(Labeled("IN", input));
        }

        body.Add(OutputSection(item, ctx, row, shell: true));

        return body;
    }

    /// <summary>Width of the IN / OUT label column at the left of a tool's input and result.</summary>
    private const double LabelColumn = 34;

    /// <summary>
    /// A part of the body with a short uppercase label (IN, OUT) in a narrow column at its left, so what the agent
    /// sent and what it got back read apart at a glance. The label hides with its content, such as the output box of
    /// a running tool that has no output yet.
    /// </summary>
    private static UIElement Labeled(string label, UIElement content)
    {
        var caption = Ui.Small(Ui.Text(label, ThemeKeys.Subtle), 0.8);
        caption.VerticalAlignment = VerticalAlignment.Top;
        caption.Margin = new Thickness(0, 2, 0, 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(content, 1);
        grid.Children.Add(caption);
        grid.Children.Add(content);
        grid.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = content });

        return grid;
    }

    /// <summary>
    /// Constructs a collection of UI elements representing the task body, including agent links, a list of task names, and the output section for a specified tool item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="row">The row.</param>
    /// <returns>A collection of ienumerable items.</returns>
    private static IEnumerable<UIElement?> TaskBody(ToolItem item, RenderContext ctx, ToolRow row)
    {
        var names = ToolFormat.TaskNames(item.Args?.Type == JTokenType.Object ? item.Args["tasks"] : null);
        var list = names.Count == 0 ? null : Ui.Column(0, [.. names.Select(n => (UIElement)Ui.Link("• " + n, ctx.ShowAgents, "Show agents"))]);

        return new[] { ActionsLine(Ui.Link("Agents", ctx.ShowAgents, "Show the Agents section")), list, OutputSection(item, ctx, row) };
    }

    /// <summary>
    /// Constructs a collection of UI elements representing the Language Server Protocol (LSP) body, including file links, argument sections, and output details for a specified tool item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="row">The row.</param>
    /// <returns>A collection of ienumerable items.</returns>
    private static IEnumerable<UIElement?> LspBody(ToolItem item, RenderContext ctx, ToolRow row)
    {
        var args = item.Args as JObject;
        var file = ToolFormat.Str(args?["file"]) ?? ToolFormat.Str(args?["path"]);
        var line = args?["line"]?.Type == JTokenType.Integer ? (int?)args["line"]!.Value<int>() : null;

        return new[] { ActionsLine(file is not null ? FileLink(ctx, file, line) : null), ArgsSection(item, row), OutputSection(item, ctx, row) };
    }

    /// <summary>
    /// Creates a WrapPanel containing the provided non-null UI elements, applying a right margin to each framework element to ensure consistent spacing.
    /// </summary>
    /// <param name="children">The collection of children.</param>
    /// <returns>The uielement? result.</returns>
    private static UIElement? ActionsLine(params UIElement?[] children)
    {
        var present = children.Where(c => c != null).ToArray();
        if (present.Length == 0)
        {
            return null;
        }

        var row = new WrapPanel();
        foreach (var child in present)
        {
            if (child is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 0, 12, 0);
            }

            row.Children.Add(child);
        }

        return row;
    }

    /// <summary>A link that opens <paramref name="path"/> (as OMP wrote it; the host resolves it) at <paramref name="line"/>, labeled <paramref name="label"/> or else the path and line.</summary>
    private static UIElement FileLink(RenderContext ctx, string path, int? line, string? label = null) => Ui.Link(label ?? ToolFormat.FileLinkLabel(path, line, ctx.Cwd), () => ctx.OpenFile(path, line), $"Open {(line.HasValue ? $"{path}:{line}" : path)}", mono: true);

    /// <summary>The call's arguments as <c>name: value</c> lines (copying yields the indented JSON); null for a call without any.</summary>
    private static UIElement? ArgsSection(ToolItem item, ToolRow row)
    {
        if (item.Args is null || (item.Args is JContainer container && !container.HasValues))
        {
            return null;
        }

        return Labeled("IN", Quiet(row, ToolFormat.FlatArgs(item.Args), "Input", original: ToolFormat.IndentedJson(item.Args), language: "yaml"));
    }

    /// <summary>Input text the header does not show: muted, never on the code background, cut like the result.</summary>
    private static UIElement Quiet(ToolRow row, string text, string name, string? original = null, string? language = null, UIElement? trailer = null)
    {
        var input = new CodeBlock(row, text, ThemeKeys.Muted, original: original, language: language, trailer: trailer);
        Ui.AutomationName(input.Box, name);

        return input.Element;
    }

    /// <summary>
    /// The result under an OUT label: a diff gets its colored lines, Markdown prose renders as such, one line shows
    /// as plain text and several lines as code on the output surface; single-line JSON is indented for reading while
    /// copying still yields the original text.
    /// </summary>
    private static UIElement? OutputSection(ToolItem item, RenderContext ctx, ToolRow row, bool shell = false)
    {
        var text = item.Result?.Text ?? item.Partial ?? "";
        var running = item.Status == ToolStatus.Running;
        if (text.Length == 0 && !running)
        {
            return null;
        }

        var failed = item.Result?.IsError == true;
        if (!running && !failed && ToolFormat.LooksLikeDiff(text!))
        {
            return Labeled("OUT", DiffView(ctx, $"{item.Id}:out:more", text!));
        }

        if (!running && ToolFormat.RendersMarkdown(item))
        {
            var source = ToolFormat.ProseText(text!);
            var prose = MarkdownView.Render(source, ctx.Links, ctx.Copy);
            Ui.AutomationName(prose, "Result");

            var block = new CodeBlock(row, prose, source);

            return Labeled("OUT", block.Element);
        }

        var (unfenced, fenceLanguage) = running ? (text!, null) : ToolFormat.StripFences(text!);
        var shown = running ? text! : JsonText.Pretty(unfenced) ?? (shell ? ToolFormat.StripShellTrailer(unfenced) : unfenced);
        var language = running ? null : fenceLanguage ?? ToolFormat.FileLanguage(item) ?? CodeHighlighter.GuessLanguage(ToolFormat.ProseText(shown));
        var output = new CodeBlock(row, shown, ThemeKeys.Muted, original: shown == text ? null : text, language: language, mark: ToolFormat.SearchPattern(item));
        Ui.AutomationName(output.Box, "Result");
        if (running)
        {
            row.Track(output);
        }

        return Labeled("OUT", output.Element);
    }

    /// <summary>
    /// Creates a UI element that displays the difference content for a specific tool item within the provided render context.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="diff">The diff.</param>
    /// <returns>The uielement? result.</returns>
    private static UIElement? DiffSection(ToolItem item, RenderContext ctx, string diff) => DiffView(ctx, $"{item.Id}:diff:more", diff);

    /// <summary>
    /// A unified diff as selectable lines on full-width soft green (added) and red (removed) tints with a colored
    /// left edge, the text in the body color with only the +/- sign colored: the first <see cref="DiffPreviewLines"/>
    /// with "show more" (remembered under <paramref name="key"/>), which shows up to <see cref="DiffLines"/>, with "Copy all" past that.
    /// </summary>
    private static UIElement DiffView(RenderContext ctx, string key, string diff)
    {
        var lines = diff.TrimEnd('\n').Split('\n');
        var host = new ContentControl { Focusable = false };

        void Show()
        {
            var limit = ctx.Open.IsOpen(key) == true ? DiffLines : DiffPreviewLines;
            var shown = lines.Length > limit ? [.. lines.Take(limit)] : lines;
            var block = Ui.SizedProse(inlines => Ui.AddLines(inlines, string.Join("\n", shown.Select(line => line.Length == 0 ? " " : line))));
            block.SetResourceReference(TextElement.FontFamilyProperty, "Omp.MonoFont");
            var scroller = new ScrollViewer { Content = block, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Ui.BubbleWheel(scroller);
            var box = ((Panel)block).Children.OfType<RichTextBox>().Single();
            box.Document.Blocks.Clear();
            var spans = ToolFormat.ChangedSpans(shown);
            for (var i = 0; i < shown.Length; i++)
            {
                box.Document.Blocks.Add(DiffLine(shown[i], spans[i]));
            }

            box.SetBinding(FrameworkElement.MinWidthProperty, new Binding(nameof(ScrollViewer.ViewportWidth)) { Source = scroller });
            var sizer = ((Panel)block).Children.OfType<TextBlock>().Single();
            box.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = sizer, Converter = new Widen(2 * DiffInset + DiffRail + 4) });
            var framed = new Border { Child = scroller, Padding = new Thickness(0, 2, 0, 2) };
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
            if (more is FrameworkElement element)
            {
                element.HorizontalAlignment = HorizontalAlignment.Left;
            }

            host.Content = Ui.Column(2, framed, more);
        }

        Show();

        return host;
    }

    /// <summary>Horizontal room inside the tint of a diff line, around its text.</summary>
    private const double DiffInset = 8;

    /// <summary>Width of the colored edge of an added or removed diff line (kept transparent on other lines so text aligns).</summary>
    private const double DiffRail = 3;

    /// <summary>Adds a fixed width to a bound size.</summary>
    private sealed class Widen : IValueConverter
    {
        private readonly double _extra;

        /// <summary>
        /// Initializes a new instance of the Widen class with the specified extra width.
        /// </summary>
        /// <param name="extra">The extra.</param>
        public Widen(double extra) => _extra = extra;

        /// <summary>
        /// Converts the specified value to the target type by adding a predefined extra value to the input.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="targetType">The target type.</param>
        /// <param name="parameter">The parameter.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>The object result.</returns>
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (double)value + _extra;

        /// <summary>
        /// Converts a value back to the target type based on the specified parameter and culture information.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="targetType">The target type.</param>
        /// <param name="parameter">The parameter.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>The object result.</returns>
        /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <param name="changed">Within the text after the sign, the part that differs from the paired line; drawn on a stronger tint.</param>
    private static Paragraph DiffLine(string line, (int Start, int Length)? changed)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(DiffInset, 0, DiffInset, 0), BorderThickness = new Thickness(DiffRail, 0, 0, 0), BorderBrush = Brushes.Transparent };
        var kind = ToolFormat.DiffLineClass(line);
        if (kind == DiffLineKind.Add || kind == DiffLineKind.Delete)
        {
            var added = kind == DiffLineKind.Add;
            var color = added ? ThemeKeys.Success : ThemeKeys.Error;
            paragraph.SetResourceReference(TextElement.BackgroundProperty, added ? ThemeKeys.DiffAddedLine : ThemeKeys.DiffRemovedLine);
            paragraph.SetResourceReference(Block.BorderBrushProperty, color);
            var sign = new Run(line.Substring(0, 1));
            sign.SetResourceReference(TextElement.ForegroundProperty, color);
            paragraph.Inlines.Add(sign);
            var text = line.Substring(1);
            if (changed.HasValue)
            {
                var (start, length) = changed.Value;
                if (start > 0)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(0, start)));
                }

                var word = new Run(text.Substring(start, length));
                word.SetResourceReference(TextElement.BackgroundProperty, added ? ThemeKeys.DiffAddedWord : ThemeKeys.DiffRemovedWord);
                paragraph.Inlines.Add(word);
                if (start + length < text.Length)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(start + length)));
                }
            }
            else if (text.Length > 0)
            {
                paragraph.Inlines.Add(new Run(text));
            }

            return paragraph;
        }
        var run = new Run(line.Length == 0 ? " " : line);
        if (kind == DiffLineKind.Hunk || kind == DiffLineKind.Meta)
        {
            run.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Muted);
        }

        paragraph.Inlines.Add(run);

        return paragraph;
    }
}
