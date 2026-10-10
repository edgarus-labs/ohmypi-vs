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
    /// <summary>
    /// Gets the font family.
    /// </summary>
    public static object FontFamily => VsFonts.EnvironmentFontFamilyKey;

    /// <summary>Base text size of the window: the environment font, as the rest of the shell, following its setting.</summary>
    public static object FontSize => VsFonts.EnvironmentFontSizeKey;

    /// <summary>Base text size of the conversation: two steps (122%) above the environment font, still following its setting.</summary>
    public static object ChatFontSize => VsFonts.Environment122PercentFontSizeKey;

    /// <summary>The "Shell" color category of Visual Studio 2026, whose text tokens Solution Explorer draws with.</summary>
    private static readonly Guid ShellCategory = new Guid("73708ded-2d56-4aad-b8eb-73b20d3f4bff");

    private static readonly TextKeySet Text = TextKeys(key => Application.Current?.TryFindResource(key) != null);

    /// <summary>
    /// Gets the background.
    /// </summary>
    public static object Background => EnvironmentColors.ToolWindowBackgroundBrushKey;

    /// <summary>
    /// Gets the foreground.
    /// </summary>
    public static object Foreground => Text.Foreground;

    /// <summary>
    /// Gets the muted.
    /// </summary>
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
    /// <summary>
    /// The code surface border.
    /// </summary>
    public const string CodeSurfaceBorder = PaletteKeys.CodeSurfaceBorder;
    /// <summary>
    /// The output surface.
    /// </summary>
    public const string OutputSurface = PaletteKeys.OutputSurface;
    /// <summary>
    /// The diff added line.
    /// </summary>
    public const string DiffAddedLine = PaletteKeys.DiffAddedLine;
    /// <summary>
    /// The diff removed line.
    /// </summary>
    public const string DiffRemovedLine = PaletteKeys.DiffRemovedLine;
    /// <summary>
    /// The diff added word.
    /// </summary>
    public const string DiffAddedWord = PaletteKeys.DiffAddedWord;
    /// <summary>
    /// The diff removed word.
    /// </summary>
    public const string DiffRemovedWord = PaletteKeys.DiffRemovedWord;
    /// <summary>
    /// Gets the divider.
    /// </summary>
    public static object Divider => EnvironmentColors.ToolWindowBorderBrushKey;

    /// <summary>
    /// Gets the scroll thumb.
    /// </summary>
    public static object ScrollThumb => EnvironmentColors.ScrollBarThumbBackgroundBrushKey;

    /// <summary>
    /// Gets the scroll thumb hover.
    /// </summary>
    public static object ScrollThumbHover => EnvironmentColors.ScrollBarThumbMouseOverBackgroundBrushKey;

    /// <summary>
    /// Gets the scroll thumb pressed.
    /// </summary>
    public static object ScrollThumbPressed => EnvironmentColors.ScrollBarThumbPressedBackgroundBrushKey;

    /// <summary>
    /// Gets the card.
    /// </summary>
    public static object Card => EnvironmentColors.CommandBarGradientBeginBrushKey;

    /// <summary>
    /// Gets the card border.
    /// </summary>
    public static object CardBorder => EnvironmentColors.ToolWindowBorderBrushKey;

    /// <summary>
    /// Gets the hover.
    /// </summary>
    public static object Hover => EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey;

    /// <summary>
    /// Gets the link.
    /// </summary>
    public static object Link => EnvironmentColors.ControlLinkTextBrushKey;

    /// <summary>
    /// Gets the link hover.
    /// </summary>
    public static object LinkHover => EnvironmentColors.ControlLinkTextHoverBrushKey;

    /// <summary>
    /// Gets the error.
    /// </summary>
    public static object Error => EnvironmentColors.ToolWindowValidationErrorTextBrushKey;

    /// <summary>
    /// Gets the error border.
    /// </summary>
    public static object ErrorBorder => EnvironmentColors.ToolWindowValidationErrorBorderBrushKey;

    /// <summary>
    /// Gets the success.
    /// </summary>
    public static object Success => EnvironmentColors.VizSurfaceGreenMediumBrushKey;

    /// <summary>
    /// Gets the warning.
    /// </summary>
    public static object Warning => EnvironmentColors.VizSurfaceGoldMediumBrushKey;

    /// <summary>
    /// Gets the progress.
    /// </summary>
    public static object Progress => EnvironmentColors.VizSurfaceSoftBlueMediumBrushKey;

    /// <summary>
    /// Gets the input background.
    /// </summary>
    public static object InputBackground => CommonControlsColors.TextBoxBackgroundBrushKey;

    /// <summary>
    /// Gets the input border.
    /// </summary>
    public static object InputBorder => CommonControlsColors.TextBoxBorderBrushKey;

    /// <summary>
    /// Gets the input border focused.
    /// </summary>
    public static object InputBorderFocused => CommonControlsColors.TextBoxBorderFocusedBrushKey;

    /// <summary>
    /// Gets the input text.
    /// </summary>
    public static object InputText => CommonControlsColors.TextBoxTextBrushKey;

    /// <summary>
    /// Gets the button background.
    /// </summary>
    public static object ButtonBackground => CommonControlsColors.ButtonBrushKey;

    /// <summary>
    /// Gets the button text.
    /// </summary>
    public static object ButtonText => CommonControlsColors.ButtonTextBrushKey;

    /// <summary>
    /// Gets the button border.
    /// </summary>
    public static object ButtonBorder => CommonControlsColors.ButtonBorderBrushKey;

    /// <summary>
    /// Gets the button hover.
    /// </summary>
    public static object ButtonHover => CommonControlsColors.ButtonHoverBrushKey;

    /// <summary>
    /// Gets the button hover text.
    /// </summary>
    public static object ButtonHoverText => CommonControlsColors.ButtonHoverTextBrushKey;

    /// <summary>
    /// Gets the button hover border.
    /// </summary>
    public static object ButtonHoverBorder => CommonControlsColors.ButtonBorderHoverBrushKey;

    /// <summary>
    /// Gets the button pressed.
    /// </summary>
    public static object ButtonPressed => CommonControlsColors.ButtonPressedBrushKey;

    /// <summary>
    /// Gets the button pressed text.
    /// </summary>
    public static object ButtonPressedText => CommonControlsColors.ButtonPressedTextBrushKey;

    /// <summary>
    /// Gets the button default.
    /// </summary>
    public static object ButtonDefault => CommonControlsColors.ButtonDefaultBrushKey;

    /// <summary>
    /// Gets the button default text.
    /// </summary>
    public static object ButtonDefaultText => CommonControlsColors.ButtonDefaultTextBrushKey;

    /// <summary>
    /// Gets the button default border.
    /// </summary>
    public static object ButtonDefaultBorder => CommonControlsColors.ButtonBorderDefaultBrushKey;

    /// <summary>
    /// Gets the button disabled.
    /// </summary>
    public static object ButtonDisabled => CommonControlsColors.ButtonDisabledBrushKey;

    /// <summary>
    /// Gets the button disabled text.
    /// </summary>
    public static object ButtonDisabledText => CommonControlsColors.ButtonDisabledTextBrushKey;

    /// <summary>
    /// Gets the focus visual.
    /// </summary>
    public static object FocusVisual => CommonControlsColors.FocusVisualBrushKey;

    /// <summary>
    /// Gets the check box background.
    /// </summary>
    public static object CheckBoxBackground => CommonControlsColors.CheckBoxBackgroundBrushKey;

    /// <summary>
    /// Gets the check box border.
    /// </summary>
    public static object CheckBoxBorder => CommonControlsColors.CheckBoxBorderBrushKey;

    /// <summary>
    /// Gets the check box glyph.
    /// </summary>
    public static object CheckBoxGlyph => CommonControlsColors.CheckBoxGlyphBrushKey;

    /// <summary>
    /// Gets the popup background.
    /// </summary>
    public static object PopupBackground => EnvironmentColors.CommandBarMenuBackgroundGradientBeginBrushKey;

    /// <summary>
    /// Gets the popup border.
    /// </summary>
    public static object PopupBorder => EnvironmentColors.CommandBarMenuBorderBrushKey;

    /// <summary>
    /// Gets the popup text.
    /// </summary>
    public static object PopupText => EnvironmentColors.CommandBarTextActiveBrushKey;

    /// <summary>
    /// Gets the tool tip background.
    /// </summary>
    public static object ToolTipBackground => EnvironmentColors.ToolTipBrushKey;

    /// <summary>
    /// Gets the tool tip text.
    /// </summary>
    public static object ToolTipText => EnvironmentColors.ToolTipTextBrushKey;

    /// <summary>
    /// Gets the tool tip border.
    /// </summary>
    public static object ToolTipBorder => EnvironmentColors.ToolTipBorderBrushKey;
}
