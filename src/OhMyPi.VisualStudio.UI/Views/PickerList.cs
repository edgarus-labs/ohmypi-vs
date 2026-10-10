using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Searchable list shared by the popup pickers and the in-panel session history. Up/Down move the selection from
/// the search box, Enter or click picks, Escape raises <see cref="Dismissed"/>.
/// </summary>
internal sealed class PickerList : DockPanel
{
    private readonly TextBox? _search;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private Func<string, IReadOnlyList<PickerItem>>? _source;

    /// <summary>
    /// Initializes a new instance of the PickerList class with a specified title and optional search functionality.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="searchable">The searchable.</param>
    /// <param name="searchName">The search name.</param>
    public PickerList(string title, bool searchable, string? searchName = null)
    {
        LastChildFill = true;
        _list = new ListBox().Styled("Omp.ListBox");
        Ui.AutomationName(_list, title);
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem item && item.IsEnabled)
            {
                Pick(item);
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            if (_list.SelectedItem is ListBoxItem item)
            {
                Pick(item);
            }
        };
        _status = Ui.Muted("");
        _status.Margin = new Thickness(8, 4, 8, 4);
        Heading = Ui.Muted(title);
        Heading.Margin = new Thickness(8, 2, 8, 4);
        Add(Heading);
        if (searchable)
        {
            _search = new TextBox { Margin = new Thickness(4, 0, 4, 4) }.Styled("Omp.TextBox");
            Ui.AutomationName(_search, searchName ?? $"Search {title}");
            _search.TextChanged += (_, __) => Refresh();
            _search.PreviewKeyDown += OnSearchKey;
            Add(_search);
        }
        Add(_status);
        Children.Add(_list);
        Ui.OnEscape(this, () => Dismissed?.Invoke());
    }

    /// <summary>
    /// Gets the heading.
    /// </summary>
    public TextBlock Heading { get; }

    /// <summary>
    /// Gets the list.
    /// </summary>
    public ListBox List => _list;

    /// <summary>
    /// Occurs when picked.
    /// </summary>
    public event Action<object>? Picked;

    /// <summary>Escape was pressed inside the list.</summary>
    public event Action? Dismissed;

    /// <summary>Clears the rows and shows <paramref name="status"/> until <see cref="SetSource"/> provides rows.</summary>
    public void Reset(string? status)
    {
        _source = null;
        _list.Items.Clear();
        if (_search is not null)
        {
            _search.Text = "";
        }

        SetStatus(status);
    }

    /// <summary>
    /// Asynchronously sets the input focus to the search field if available, otherwise defaults the focus to the list component.
    /// </summary>
    public void FocusFirst() => _ = Dispatcher.InvokeAsync(() =>
                                     {
                                         if (_search is not null)
                                         {
                                             _search.Focus();
                                         }
                                         else
                                         {
                                             _list.Focus();
                                         }
                                     }, DispatcherPriority.Input);

    /// <summary>
    /// Updates the status text and toggles its visibility based on whether the provided status value is null or empty.
    /// </summary>
    /// <param name="status">The status.</param>
    public void SetStatus(string? status)
    {
        _status.Text = status ?? "";
        _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Sets the data source for retrieving picker items and refreshes the current view.
    /// </summary>
    /// <param name="source">The source.</param>
    public void SetSource(Func<string, IReadOnlyList<PickerItem>> source)
    {
        _source = source;
        Refresh();
        if (_search is null)
        {
            _ = _list.Dispatcher.InvokeAsync(() => (_list.SelectedItem as ListBoxItem)?.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>Selects the row whose value satisfies <paramref name="predicate"/>.</summary>
    public void Select(Func<object, bool> predicate)
    {
        foreach (var entry in _list.Items)
        {
            if (entry is ListBoxItem row && row.Tag is not null && predicate(row.Tag))
            {
                _list.SelectedItem = row;
                _list.ScrollIntoView(row);

                return;
            }
        }
    }

    /// <summary>
    /// Adds the specified UI element to the children collection and sets its dock position to the top.
    /// </summary>
    /// <param name="element">The element.</param>
    private void Add(UIElement element)
    {
        SetDock(element, Dock.Top);
        Children.Add(element);
    }

    /// <summary>
    /// Updates the list box items based on the current search text and source provider, applying accessibility properties and selecting the first valid entry.
    /// </summary>
    private void Refresh()
    {
        if (_source is null)
        {
            return;
        }

        var items = _source(_search?.Text ?? "");
        _list.Items.Clear();
        ListBoxItem? first = null;
        foreach (var item in items)
        {
            var row = new ListBoxItem { Content = item.Content, Tag = item.Value };
            Ui.AutomationName(row, item.AutomationName);
            if (item.Value is null)
            {
                row.IsEnabled = false;
                row.Focusable = false;
                row.Padding = new Thickness(8, 6, 8, 1);
            }
            else if (first is null)
            {
                first = row;
            }
            _list.Items.Add(row);
        }
        _list.SelectedItem = first;
        first?.BringIntoView();
        SetStatus(items.Count == 0 ? "No matches." : null);
    }

    /// <summary>
    /// Handles key press events to navigate the list via arrow keys or select the currently highlighted item using the Enter key.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_list.SelectedItem is ListBoxItem item)
                {
                    Pick(item);
                }

                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Updates the selected index of the list by shifting it by the specified delta to the next enabled item and scrolls that item into view.
    /// </summary>
    /// <param name="delta">The delta.</param>
    private void Move(int delta)
    {
        var index = _list.SelectedIndex;
        for (var next = index + delta; next >= 0 && next < _list.Items.Count; next += delta)
        {
            if (_list.Items[next] is ListBoxItem candidate && candidate.IsEnabled)
            {
                _list.SelectedIndex = next;
                _list.ScrollIntoView(candidate);

                return;
            }
        }
    }

    /// <summary>
    /// Processes the selection of a list box item by invoking the Picked event with the item&apos;s associated tag.
    /// </summary>
    /// <param name="item">The item.</param>
    private void Pick(ListBoxItem item)
    {
        if (item.Tag is null)
        {
            return;
        }

        Picked?.Invoke(item.Tag);
    }
}
