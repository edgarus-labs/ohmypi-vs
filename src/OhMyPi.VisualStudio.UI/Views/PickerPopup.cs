using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A <see cref="PickerList"/> in a popup anchored to a control (model and effort pickers).</summary>
internal sealed class PickerPopup
{
    private readonly UIElement _anchor;

    /// <summary>
    /// Initializes a new instance of the PickerPopup class, configuring it with a specified anchor element, title, searchability, and width.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="title">The title.</param>
    /// <param name="searchable">The searchable.</param>
    /// <param name="width">The width.</param>
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

    /// <summary>
    /// Gets the content.
    /// </summary>
    public PickerList Content { get; }

    /// <summary>
    /// Gets the popup.
    /// </summary>
    public Popup Popup { get; }

    /// <summary>
    /// Occurs when picked.
    /// </summary>
    public event Action<object>? Picked;

    /// <summary>Opens the popup showing <paramref name="status"/> until <see cref="SetSource"/> provides rows.</summary>
    public void Open(string? status = null)
    {
        Content.Reset(status);
        Popup.IsOpen = true;
        Content.FocusFirst();
    }

    /// <summary>
    /// Updates the status of the associated content to the specified value.
    /// </summary>
    /// <param name="status">The status.</param>
    public void SetStatus(string? status) => Content.SetStatus(status);

    /// <summary>
    /// Sets the data source for the content by providing a function that resolves a search string into a read-only list of picker items.
    /// </summary>
    /// <param name="source">The source.</param>
    public void SetSource(Func<string, IReadOnlyList<PickerItem>> source) => Content.SetSource(source);

    /// <summary>
    /// Filters the content based on the specified predicate to select matching elements.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    public void Select(Func<object, bool> predicate) => Content.Select(predicate);

    /// <summary>
    /// Closes the popup window and returns focus to the associated anchor element.
    /// </summary>
    public void Close()
    {
        Popup.IsOpen = false;
        _anchor.Focus();
    }
}
