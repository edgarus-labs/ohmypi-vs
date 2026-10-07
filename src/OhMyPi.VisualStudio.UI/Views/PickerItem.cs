using System.Windows;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>One row of a picker: a selectable value, or a non-selectable group header when <see cref="Value"/> is null.</summary>
internal sealed class PickerItem
{
    public PickerItem(object? value, UIElement content, string automationName)
    {
        Value = value;
        Content = content;
        AutomationName = automationName;
    }

    public object? Value { get; }

    public UIElement Content { get; }

    public string AutomationName { get; }
}
