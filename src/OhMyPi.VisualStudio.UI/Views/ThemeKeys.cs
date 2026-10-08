using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The Visual Studio theme resources the chat uses; every brush is referenced dynamically so
/// dark, light, blue and high-contrast themes switch live.
/// </summary>
internal static class ThemeKeys
{
    public static object FontFamily => VsFonts.EnvironmentFontFamilyKey;

    /// <summary>Base text size of the window: the environment font, as the rest of the shell, following its setting.</summary>
    public static object FontSize => VsFonts.EnvironmentFontSizeKey;

    /// <summary>Base text size of the conversation: two steps (122%) above the environment font, still following its setting.</summary>
    public static object ChatFontSize => VsFonts.Environment122PercentFontSizeKey;

    /// <summary>The "Shell" color category of Visual Studio 2026, whose text tokens Solution Explorer draws with.</summary>
    private static readonly Guid ShellCategory = new Guid("73708ded-2d56-4aad-b8eb-73b20d3f4bff");

    private static readonly TextKeySet Text = TextKeys(key => Application.Current?.TryFindResource(key) != null);

    public static object Background => EnvironmentColors.ToolWindowBackgroundBrushKey;

    public static object Foreground => Text.Foreground;

    public static object Muted => Text.Muted;

    /// <summary>Dimmer than <see cref="Muted"/>: detail lines under picker rows.</summary>
    public static object Subtle => Text.Subtle;

    /// <summary>
    /// The text colors: the shell's secondary text token for body text and its tertiary one for dimmed text where
    /// Visual Studio defines them (2026 and later, also under themes that only restyle the older color categories),
    /// otherwise the environment colors.
    /// </summary>
    internal static TextKeySet TextKeys(Func<object, bool> exists)
    {
        object Pick(string token, object fallback)
        {
            var key = new ThemeResourceKey(ShellCategory, token, ThemeResourceKeyType.BackgroundBrush);

            return exists(key) ? key : fallback;
        }

        return new TextKeySet(
            Pick("TextFillSecondary", EnvironmentColors.ToolWindowTextBrushKey),
            Pick("TextFillTertiary", EnvironmentColors.SystemGrayTextBrushKey),
            Pick("TextFillTertiary", EnvironmentColors.CommandBarTextInactiveBrushKey));
    }

    /// <summary>Accent for selection and activity: the link color, as on "View all".</summary>
    public static object Accent => EnvironmentColors.ControlLinkTextBrushKey;

    /// <summary>The theme's own accent color (the active window frame), for fills that follow the chosen theme.</summary>
    public static object ThemeAccent => EnvironmentColors.AccentMediumBrushKey;

    /// <summary>Colors the host derives from the theme (see <see cref="PaletteKeys"/>): the code surface and the diff backgrounds.</summary>
    public const string CodeSurface = PaletteKeys.CodeSurface;
    public const string CodeSurfaceBorder = PaletteKeys.CodeSurfaceBorder;
    public const string OutputSurface = PaletteKeys.OutputSurface;
    public const string DiffAddedLine = PaletteKeys.DiffAddedLine;
    public const string DiffRemovedLine = PaletteKeys.DiffRemovedLine;
    public const string DiffAddedWord = PaletteKeys.DiffAddedWord;
    public const string DiffRemovedWord = PaletteKeys.DiffRemovedWord;
    public static object Divider => EnvironmentColors.ToolWindowBorderBrushKey;

    public static object ScrollThumb => EnvironmentColors.ScrollBarThumbBackgroundBrushKey;

    public static object ScrollThumbHover => EnvironmentColors.ScrollBarThumbMouseOverBackgroundBrushKey;

    public static object ScrollThumbPressed => EnvironmentColors.ScrollBarThumbPressedBackgroundBrushKey;

    public static object Card => EnvironmentColors.CommandBarGradientBeginBrushKey;

    public static object CardBorder => EnvironmentColors.ToolWindowBorderBrushKey;

    public static object Hover => EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey;

    public static object Link => EnvironmentColors.ControlLinkTextBrushKey;

    public static object LinkHover => EnvironmentColors.ControlLinkTextHoverBrushKey;

    public static object Error => EnvironmentColors.ToolWindowValidationErrorTextBrushKey;

    public static object ErrorBorder => EnvironmentColors.ToolWindowValidationErrorBorderBrushKey;

    public static object Success => EnvironmentColors.VizSurfaceGreenMediumBrushKey;

    public static object Warning => EnvironmentColors.VizSurfaceGoldMediumBrushKey;

    public static object Progress => EnvironmentColors.VizSurfaceSoftBlueMediumBrushKey;

    public static object InputBackground => CommonControlsColors.TextBoxBackgroundBrushKey;

    public static object InputBorder => CommonControlsColors.TextBoxBorderBrushKey;

    public static object InputBorderFocused => CommonControlsColors.TextBoxBorderFocusedBrushKey;

    public static object InputText => CommonControlsColors.TextBoxTextBrushKey;

    public static object ButtonBackground => CommonControlsColors.ButtonBrushKey;

    public static object ButtonText => CommonControlsColors.ButtonTextBrushKey;

    public static object ButtonBorder => CommonControlsColors.ButtonBorderBrushKey;

    public static object ButtonHover => CommonControlsColors.ButtonHoverBrushKey;

    public static object ButtonHoverText => CommonControlsColors.ButtonHoverTextBrushKey;

    public static object ButtonHoverBorder => CommonControlsColors.ButtonBorderHoverBrushKey;

    public static object ButtonPressed => CommonControlsColors.ButtonPressedBrushKey;

    public static object ButtonPressedText => CommonControlsColors.ButtonPressedTextBrushKey;

    public static object ButtonDefault => CommonControlsColors.ButtonDefaultBrushKey;

    public static object ButtonDefaultText => CommonControlsColors.ButtonDefaultTextBrushKey;

    public static object ButtonDefaultBorder => CommonControlsColors.ButtonBorderDefaultBrushKey;

    public static object ButtonDisabled => CommonControlsColors.ButtonDisabledBrushKey;

    public static object ButtonDisabledText => CommonControlsColors.ButtonDisabledTextBrushKey;

    public static object FocusVisual => CommonControlsColors.FocusVisualBrushKey;

    public static object CheckBoxBackground => CommonControlsColors.CheckBoxBackgroundBrushKey;

    public static object CheckBoxBorder => CommonControlsColors.CheckBoxBorderBrushKey;

    public static object CheckBoxGlyph => CommonControlsColors.CheckBoxGlyphBrushKey;

    public static object PopupBackground => EnvironmentColors.CommandBarMenuBackgroundGradientBeginBrushKey;

    public static object PopupBorder => EnvironmentColors.CommandBarMenuBorderBrushKey;

    public static object PopupText => EnvironmentColors.CommandBarTextActiveBrushKey;

    public static object ToolTipBackground => EnvironmentColors.ToolTipBrushKey;

    public static object ToolTipText => EnvironmentColors.ToolTipTextBrushKey;

    public static object ToolTipBorder => EnvironmentColors.ToolTipBorderBrushKey;
}
