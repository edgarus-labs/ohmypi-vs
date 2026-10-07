using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using System.Windows.Media;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;

namespace OhMyPi.VisualStudio
{
    /// <summary>
    /// Puts the diff backgrounds into a resource dictionary: pure green and red for changed lines, translucent so the
    /// code stays readable and less opaque on light themes, where the same colors glare; changed words take the fills
    /// of Visual Studio's own diff (Tools &gt; Options &gt; Fonts and Colors). They are recomputed when the theme or
    /// those settings change.
    /// </summary>
    internal static class DiffColors
    {
        private static readonly (string Format, string Key)[] Formats =
        {
            ("deltadiff.add.word", DiffColorKeys.AddedWord),
            ("deltadiff.remove.word", DiffColorKeys.RemovedWord),
        };

        /// <summary>Fills <paramref name="resources"/> with the diff colors; does nothing where the editor's format map is not available.</summary>
        public static void Attach(ResourceDictionary resources)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SComponentModel)) is IComponentModel components)) return;
            var map = components.GetService<IEditorFormatMapService>()?.GetEditorFormatMap("text");
            if (map == null) return;
            Apply(map, resources);
            map.FormatMappingChanged += (_, __) => Apply(map, resources);
            VSColorTheme.ThemeChanged += _ => Apply(map, resources);
        }

        /// <summary>
        /// Sets the line colors and copies the word fills of Visual Studio's diff. Visual Studio may outline changed
        /// words instead of filling them; a word without a fill then takes its line's color, less transparent.
        /// </summary>
        private static void Apply(IEditorFormatMap map, ResourceDictionary resources)
        {
            var dark = IsDarkTheme();
            var addedLine = DiffPalette.AddedLine(dark);
            var removedLine = DiffPalette.RemovedLine(dark);
            resources[DiffColorKeys.AddedLine] = Freeze(ToColor(addedLine));
            resources[DiffColorKeys.RemovedLine] = Freeze(ToColor(removedLine));
            foreach (var (format, key) in Formats)
            {
                var properties = map.GetProperties(format);
                if (properties.Contains(EditorFormatDefinition.BackgroundBrushId)) resources[key] = properties[EditorFormatDefinition.BackgroundBrushId];
                else resources.Remove(key);
            }
            if (!resources.Contains(DiffColorKeys.AddedWord)) resources[DiffColorKeys.AddedWord] = Freeze(ToColor(DiffPalette.Stronger(addedLine)));
            if (!resources.Contains(DiffColorKeys.RemovedWord)) resources[DiffColorKeys.RemovedWord] = Freeze(ToColor(DiffPalette.Stronger(removedLine)));
        }

        /// <summary>Whether the tool window background of the current theme is dark.</summary>
        private static bool IsDarkTheme()
        {
            var c = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            return DiffPalette.IsDark(c.R, c.G, c.B);
        }

        private static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
