using System;
using System.Windows;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Provider marker colors: a provider's brand color, or one derived from the Visual Studio theme accent and kept in
/// step with it, since the accent is set as a dynamic resource on an attached property.
/// </summary>
internal static class ThemeTint
{
    /// <summary>The theme accent whose saturation and lightness a marker of an unknown provider takes.</summary>
    public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
        "Accent", typeof(object), typeof(ThemeTint), new PropertyMetadata(null, OnMarkerChanged));

    /// <summary>
    /// The provider property.
    /// </summary>
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.RegisterAttached(
        "Provider", typeof(string), typeof(ThemeTint), new PropertyMetadata(null, OnMarkerChanged));

    /// <summary>Brand colors of AI providers, matched against the provider id; null marks a brand that is black and white.</summary>
    private static readonly (string Key, Color? Color)[] Brands =
    [
        ("anthropic", Color.FromRgb(0xD9, 0x77, 0x57)),
        ("claude", Color.FromRgb(0xD9, 0x77, 0x57)),
        ("openai", Color.FromRgb(0x10, 0xA3, 0x7F)),
        ("codex", Color.FromRgb(0x10, 0xA3, 0x7F)),
        ("azure", Color.FromRgb(0x00, 0x78, 0xD4)),
        ("copilot", Color.FromRgb(0x89, 0x57, 0xE5)),
        ("github", Color.FromRgb(0x89, 0x57, 0xE5)),
        ("gemini", Color.FromRgb(0x42, 0x85, 0xF4)),
        ("google", Color.FromRgb(0x42, 0x85, 0xF4)),
        ("vertex", Color.FromRgb(0x42, 0x85, 0xF4)),
        ("deepseek", Color.FromRgb(0x4D, 0x6B, 0xFE)),
        ("mistral", Color.FromRgb(0xFA, 0x52, 0x0F)),
        ("meta", Color.FromRgb(0x08, 0x66, 0xFF)),
        ("llama", Color.FromRgb(0x08, 0x66, 0xFF)),
        ("groq", Color.FromRgb(0xF5, 0x50, 0x36)),
        ("openrouter", Color.FromRgb(0x65, 0x66, 0xF1)),
        ("perplexity", Color.FromRgb(0x20, 0x80, 0x8D)),
        ("cohere", Color.FromRgb(0x39, 0x59, 0x4D)),
        ("qwen", Color.FromRgb(0x61, 0x5C, 0xED)),
        ("alibaba", Color.FromRgb(0xFF, 0x6A, 0x00)),
        ("bedrock", Color.FromRgb(0xFF, 0x99, 0x00)),
        ("aws", Color.FromRgb(0xFF, 0x99, 0x00)),
        ("ollama", null),
        ("cursor", null),
        ("xai", null),
        ("grok", null),
    ];

    /// <summary>
    /// Retrieves the value of the AccentProperty from the specified DependencyObject.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The object result.</returns>
    public static object GetAccent(DependencyObject element) => element.GetValue(AccentProperty);

    /// <summary>
    /// Sets the accent value for the specified dependency object.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value.</param>
    public static void SetAccent(DependencyObject element, object value) => element.SetValue(AccentProperty, value);

    /// <summary>
    /// Retrieves the provider value associated with the specified dependency object.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The string? result.</returns>
    public static string? GetProvider(DependencyObject element) => (string?)element.GetValue(ProviderProperty);

    /// <summary>
    /// Sets the provider value for the specified dependency object.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value.</param>
    public static void SetProvider(DependencyObject element, string? value) => element.SetValue(ProviderProperty, value);

    /// <summary>
    /// Updates the background of a Border element based on the provided accent color and provider when the marker property changes.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="e">The e.</param>
    private static void OnMarkerChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (!(element is System.Windows.Controls.Border border))
        {
            return;
        }

        var provider = GetProvider(element);
        if (!(GetAccent(element) is SolidColorBrush accent) || string.IsNullOrEmpty(provider))
        {
            border.ClearValue(System.Windows.Controls.Border.BackgroundProperty);

            return;
        }
        border.Background = Freeze(Marker(accent.Color, provider!));
    }

    /// <summary>
    /// The provider's brand color when its id names a known brand; for a black-and-white brand a gray as light as the
    /// theme accent; for any other provider a hue spread by its name with the accent's saturation and lightness, so
    /// any number of providers stay apart and match the theme.
    /// </summary>
    internal static Color Marker(Color accent, string provider)
    {
        var id = provider.ToLowerInvariant();
        SaturationAndLightness(accent, out var saturation, out var lightness);
        lightness = Math.Min(Math.Max(lightness, 0.45), 0.7);
        foreach (var (key, color) in Brands)
        {
            if (id.IndexOf(key, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            return color ?? FromHsl(0, 0, lightness);
        }
        var hash = 2166136261u;
        foreach (var c in id)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return FromHsl(hash % 360u, Math.Max(saturation, 0.35), lightness);
    }

    /// <summary>
    /// Calculates the saturation and lightness components of a given color using the HSL color model.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="saturation">The saturation.</param>
    /// <param name="lightness">The lightness.</param>
    private static void SaturationAndLightness(Color color, out double saturation, out double lightness)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        lightness = (max + min) / 2;
        var delta = max - min;
        saturation = delta == 0 ? 0 : lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
    }

    /// <summary>
    /// Converts HSL (Hue, Saturation, Lightness) color values to a corresponding Color object.
    /// </summary>
    /// <param name="hue">The hue.</param>
    /// <param name="saturation">The saturation.</param>
    /// <param name="lightness">The lightness.</param>
    /// <returns>The color result.</returns>
    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>
    /// Creates a frozen SolidColorBrush using the specified color to improve performance and ensure immutability.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The solid color brush result.</returns>
    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return brush;
    }
}
