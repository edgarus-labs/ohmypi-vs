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

    public TextBlock Heading { get; }

    public ListBox List => _list;

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

    public void SetStatus(string? status)
    {
        _status.Text = status ?? "";
        _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

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

    private void Add(UIElement element)
    {
        SetDock(element, Dock.Top);
        Children.Add(element);
    }

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

    private void Pick(ListBoxItem item)
    {
        if (item.Tag is null)
        {
            return;
        }

        Picked?.Invoke(item.Tag);
    }
}
