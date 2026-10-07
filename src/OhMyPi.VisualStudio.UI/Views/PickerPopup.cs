using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A <see cref="PickerList"/> in a popup anchored to a control (model and effort pickers).</summary>
internal sealed class PickerPopup
{
    private readonly UIElement _anchor;

    public PickerPopup(UIElement anchor, string title, bool searchable, double width = 380)
    {
        _anchor = anchor;
        Content = new PickerList(title, searchable) { Width = width };
        Content.List.MaxHeight = 360;
        Content.Picked += value =>
        {
            Close();
            Picked?.Invoke(value);
        };
        Popup = Ui.Popup(anchor, Content);
    }

    public PickerList Content { get; }

    public Popup Popup { get; }

    public event Action<object>? Picked;

    /// <summary>Opens the popup showing <paramref name="status"/> until <see cref="SetSource"/> provides rows.</summary>
    public void Open(string? status = null)
    {
        Content.Reset(status);
        Popup.IsOpen = true;
        Content.FocusFirst();
    }

    public void SetStatus(string? status) => Content.SetStatus(status);

    public void SetSource(Func<string, IReadOnlyList<PickerItem>> source) => Content.SetSource(source);

    public void Select(Func<object, bool> predicate) => Content.Select(predicate);

    public void Close()
    {
        Popup.IsOpen = false;
        _anchor.Focus();
    }
}
