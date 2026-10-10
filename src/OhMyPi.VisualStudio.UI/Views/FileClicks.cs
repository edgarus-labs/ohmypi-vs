using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Opens a file named in read-only tool text when the user clicks its path; over a path the cursor becomes a hand
/// and the tooltip names the file. A click that ends a selection only selects. In a rich text box only the line
/// under the pointer is read, so moving the mouse never walks the whole document.
/// </summary>
internal static class FileClicks
{
    private static readonly DependencyProperty ContextProperty =
        DependencyProperty.RegisterAttached("Context", typeof(RenderContext), typeof(FileClicks), new PropertyMetadata(null));

    /// <summary>
    /// Attaches the specified text box to the render context, enabling the attachment when the text box has a selection.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="ctx">The ctx.</param>
    public static void Attach(TextBox box, RenderContext ctx) => Attach(box, ctx, () => box.SelectionLength > 0);

    /// <summary>Attaches to a box whose lines are runs separated by line breaks in one paragraph, as <see cref="CodeBlock.Fill"/> writes them.</summary>
    public static void Attach(RichTextBox box, RenderContext ctx) => Attach(box, ctx, () => !box.Selection.IsEmpty);

    /// <summary>
    /// Attaches a render context to the specified control and configures mouse event handlers to manage cursor visibility, tooltips, and the opening of target elements.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="ctx">The ctx.</param>
    /// <param name="selecting">The selecting.</param>
    private static void Attach(Control box, RenderContext ctx, Func<bool> selecting)
    {
        box.SetValue(ContextProperty, ctx);
        box.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!selecting() && OpenAt(box, e.GetPosition(box)))
            {
                e.Handled = true;
            }
        };
        box.MouseMove += (_, e) =>
        {
            var target = At(box, e.GetPosition(box));
            box.Cursor = target is not null ? Cursors.Hand : null;
            var tip = target is not null ? $"Open {target.Path}{(target.Line.HasValue ? $":{target.Line}" : "")}" : null;
            if (!Equals(box.ToolTip, tip))
            {
                box.ToolTip = tip;
            }
        };
    }

    /// <summary>Opens the file whose path is under <paramref name="point"/> (relative to <paramref name="box"/>); false when there is none.</summary>
    public static bool OpenAt(Control box, Point point)
    {
        var target = At(box, point);
        if (target is null)
        {
            return false;
        }

        ((RenderContext)box.GetValue(ContextProperty)).OpenFile(target.Path, target.Line);

        return true;
    }

    /// <summary>
    /// Resolves the file target associated with a specific point within the provided control by identifying the token at that location via the render context.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="point">The point.</param>
    /// <returns>The file target? result.</returns>
    private static FileTarget? At(Control box, Point point)
    {
        if (!(box.GetValue(ContextProperty) is RenderContext ctx))
        {
            return null;
        }

        var token = box is TextBox text ? TokenAt(text, point) : box is RichTextBox rich ? TokenAt(rich, point) : null;

        return token is null ? null : ctx.ResolveFile(token);
    }

    /// <summary>
    /// Retrieves the token located at the specified point within the provided text box, returning null if no character exists at that position.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="point">The point.</param>
    /// <returns>The string? result.</returns>
    private static string? TokenAt(TextBox box, Point point)
    {
        var index = box.GetCharacterIndexFromPoint(point, snapToText: false);

        return index < 0 ? null : FileLinks.TokenAt(box.Text, index);
    }

    /// <summary>The path-like token under <paramref name="point"/>, read from the runs of its line only; a hit on a character's right half belongs to that character, as in a text box.</summary>
    private static string? TokenAt(RichTextBox box, Point point)
    {
        var pointer = box.GetPositionFromPoint(point, snapToText: false);
        var run = pointer?.Parent as Run ?? pointer?.GetAdjacentElement(LogicalDirection.Forward) as Run;
        if (pointer is null || run is null)
        {
            return null;
        }

        Inline first = run;
        while (first.PreviousInline is Run previous)
        {
            first = previous;
        }

        var line = new StringBuilder();
        var index = 0;
        for (var inline = first; inline is Run part; inline = inline.NextInline)
        {
            if (part == run)
            {
                var trailing = pointer.LogicalDirection == LogicalDirection.Backward ? 1 : 0;
                index = Math.Max(0, line.Length + Math.Max(0, run.ContentStart.GetOffsetToPosition(pointer)) - trailing);
            }

            line.Append(part.Text);
        }

        return FileLinks.TokenAt(line.ToString(), index);
    }
}
