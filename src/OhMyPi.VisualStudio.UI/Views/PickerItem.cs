using System.Windows;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>One row of a picker: a selectable value, or a non-selectable group header when <see cref="Value"/> is null.</summary>
internal sealed class PickerItem
{
    /// <summary>
    /// Initializes a new instance of the PickerItem class with the specified value, visual content, and automation name.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="content">The content.</param>
    /// <param name="automationName">The automation name.</param>
    public PickerItem(object? value, UIElement content, string automationName)
    {
        Value = value;
        Content = content;
        AutomationName = automationName;
    }

    /// <summary>
    /// Gets the value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the content.
    /// </summary>
    public UIElement Content { get; }

    /// <summary>
    /// Gets the automation name.
    /// </summary>
    public string AutomationName { get; }
}
