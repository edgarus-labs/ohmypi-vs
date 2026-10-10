using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;
using System.Windows;
using System.Windows.Media;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Puts the theme-dependent chat colors into a resource dictionary: the code surface and its border, derived from
/// the tool window background so they are visible on any theme (in high contrast the background and the text
/// color themselves), and the soft diff tints of changed lines and words. They are recomputed when the theme
/// changes.
/// </summary>
internal static class ChatColors
{
    /// <summary>Fills <paramref name="resources"/> with the chat colors of the current theme and keeps them current.</summary>
    public static void Attach(ResourceDictionary resources)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Apply(resources);
        VSColorTheme.ThemeChanged += _ => Apply(resources);
    }

    /// <summary>
    /// Calculates and applies theme-aware chat palette colors to the specified resource dictionary based on the current Visual Studio environment colors.
    /// </summary>
    /// <param name="resources">The resources.</param>
    private static void Apply(ResourceDictionary resources)
    {
        var c = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
        var t = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);
        var background = new Argb(c.A, c.R, c.G, c.B);
        var dark = ChatPalette.IsDark(background);
        var (surface, border) = ChatPalette.CodeSurface(background, new Argb(t.A, t.R, t.G, t.B), SystemParameters.HighContrast);
        resources[PaletteKeys.CodeSurface] = Freeze(surface);
        resources[PaletteKeys.CodeSurfaceBorder] = Freeze(border);
        resources[PaletteKeys.OutputSurface] = Freeze(ChatPalette.OutputSurface(background, SystemParameters.HighContrast));
        resources[PaletteKeys.DiffAddedLine] = Freeze(ChatPalette.AddedLine(dark));
        resources[PaletteKeys.DiffRemovedLine] = Freeze(ChatPalette.RemovedLine(dark));
        resources[PaletteKeys.DiffAddedWord] = Freeze(ChatPalette.AddedWord(dark));
        resources[PaletteKeys.DiffRemovedWord] = Freeze(ChatPalette.RemovedWord(dark));
    }

    /// <summary>
    /// Creates a frozen SolidColorBrush using the specified ARGB color values to improve performance and ensure immutability.
    /// </summary>
    /// <param name="c">The c.</param>
    /// <returns>The solid color brush result.</returns>
    private static SolidColorBrush Freeze(Argb c)
    {
        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();

        return brush;
    }
}
