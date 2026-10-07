using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>
    /// Opens a file named in read-only tool text when the user clicks its path; over a path the cursor becomes a hand
    /// and the tooltip names the file. A click that ends a selection only selects.
    /// </summary>
    internal static class FileClicks
    {
        private static readonly DependencyProperty ContextProperty =
            DependencyProperty.RegisterAttached("Context", typeof(RenderContext), typeof(FileClicks), new PropertyMetadata(null));

        public static void Attach(TextBox box, RenderContext ctx)
        {
            box.SetValue(ContextProperty, ctx);
            box.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (box.SelectionLength == 0 && OpenAt(box, e.GetPosition(box))) e.Handled = true;
            };
            box.MouseMove += (_, e) =>
            {
                var target = At(box, e.GetPosition(box));
                box.Cursor = target != null ? Cursors.Hand : null;
                var tip = target != null ? $"Open {target.Path}{(target.Line.HasValue ? $":{target.Line}" : "")}" : null;
                if (!Equals(box.ToolTip, tip)) box.ToolTip = tip;
            };
        }

        /// <summary>Opens the file whose path is under <paramref name="point"/> (relative to <paramref name="box"/>); false when there is none.</summary>
        public static bool OpenAt(TextBox box, Point point)
        {
            var target = At(box, point);
            if (target == null) return false;
            ((RenderContext)box.GetValue(ContextProperty)).OpenFile(target.Path, target.Line);
            return true;
        }

        private static FileTarget? At(TextBox box, Point point)
        {
            if (!(box.GetValue(ContextProperty) is RenderContext ctx)) return null;
            var index = box.GetCharacterIndexFromPoint(point, snapToText: false);
            if (index < 0) return null;
            var token = FileLinks.TokenAt(box.Text, index);
            return token == null ? null : ctx.ResolveFile(token);
        }
    }
}
