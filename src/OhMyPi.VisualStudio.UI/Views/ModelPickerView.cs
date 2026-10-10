using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The model picker: one search bar (search box, favorites and recent toggles, close) over the list of providers and
/// their models; the header of the provider at the top of the view stays pinned while its models scroll under it.
/// Hover and the arrow keys only highlight; a click or Enter on a model picks it; a click on a provider header
/// expands or collapses it. Ctrl+D stars the highlighted model and Ctrl+C copies its id.
/// </summary>
internal sealed class ModelPickerView : Grid
{
    private const string SearchGlyph = "\uE721";
    private const string StarGlyph = "\uE734";
    private const string StarFilledGlyph = "\uE735";
    private const string ClockGlyph = "\uE823";
    private const string CloseGlyph = "\uE711";
    private const int PageRows = 8;

    private readonly IModelPreferences _preferences;
    private readonly Action<string> _copyId;
    private readonly Action<string, Exception> _fail;
    private readonly TextBox _search;
    private readonly ToggleButton _favorites;
    private readonly TextBlock _favoritesGlyph;
    private readonly ToggleButton _recent;
    private readonly ListBox _list;
    private readonly ContentControl _pinned;
    private readonly TextBlock _status;
    private readonly ModelPickerFilter _filter = new ModelPickerFilter();
    private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
    private List<ModelRowItem> _items = new List<ModelRowItem>();
    private ModelCatalog? _catalog;
    private ModelKey? _active;
    private Point _pointer = new Point(double.NaN, double.NaN);
    /// <summary>The model row the left button went down on; a click picks it only when the button comes up on the same row.</summary>
    private ModelRowItem? _pressed;

    public ModelPickerView(IModelPreferences preferences, Action<string> copyId, Action<string, Exception> fail)
    {
        _preferences = preferences;
        _copyId = copyId;
        _fail = fail;
        System.Windows.Documents.TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _search = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
        };
        _search.SetResourceReference(Control.ForegroundProperty, ThemeKeys.InputText);
        _search.SetResourceReference(TextBox.CaretBrushProperty, ThemeKeys.InputText);
        _search.SetResourceReference(TextBox.SelectionBrushProperty, ThemeKeys.ThemeAccent);
        _search.Template = PlainTextBox();
        Ui.AutomationName(_search, "Search models");
        var hint = new TextBlock { Text = "Search models…", FontSize = 13, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Muted);
        _search.TextChanged += (_, __) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _filter.Query = _search.Text;
            Refresh(false);
        };
        var lens = new TextBlock { Text = SearchGlyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsHitTestVisible = false };
        lens.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Muted);
        lens.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.IconFont");
        var textArea = new Grid { Children = { hint, _search } };
        var inputContent = new DockPanel { Children = { lens, textArea } };
        var input = new Border
        {
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = inputContent,
        };
        input.SetResourceReference(Border.BackgroundProperty, ThemeKeys.InputBackground);
        input.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.InputBorder);
        _search.IsKeyboardFocusWithinChanged += (_, __) =>
            input.SetResourceReference(Border.BorderBrushProperty, _search.IsKeyboardFocusWithin ? ThemeKeys.InputBorderFocused : ThemeKeys.InputBorder);
        input.MouseLeftButtonDown += (_, __) => _search.Focus();

        _favoritesGlyph = Glyph(StarGlyph, 15);
        _favorites = new ToggleButton { Content = _favoritesGlyph, ToolTip = "Favorites" }.Styled("Omp.PickerIconButton");
        Ui.AutomationName(_favorites, "Show favorites only");
        _favorites.Click += (_, __) =>
        {
            _filter.FavoritesOnly = _favorites.IsChecked == true;
            SaveFilters();
            SyncToggles();
            Refresh(false);
        };
        _recent = new ToggleButton { Content = Glyph(ClockGlyph, 15), ToolTip = "Recent" }.Styled("Omp.PickerIconButton");
        Ui.AutomationName(_recent, "Show recently used only");
        _recent.Click += (_, __) =>
        {
            _filter.RecentOnly = _recent.IsChecked == true;
            SaveFilters();
            SyncToggles();
            Refresh(false);
        };
        var close = new Button { Content = Glyph(CloseGlyph, 14), ToolTip = "Close" }.Styled("Omp.PickerIconButton");
        Ui.AutomationName(close, "Close");
        close.Click += (_, __) => CloseRequested?.Invoke();

        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        bar.Children.Add(input);
        AddAt(bar, _favorites, 1, new Thickness(6, 0, 0, 0));
        AddAt(bar, _recent, 2, new Thickness(6, 0, 0, 0));
        AddAt(bar, close, 3, new Thickness(6, 0, 0, 0));
        var barFrame = new Border { Padding = new Thickness(10, 10, 10, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        barFrame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        SetRow(barFrame, 0);
        Children.Add(barFrame);

        _list = new ListBox().Styled("Omp.ModelList");
        Ui.AutomationName(_list, "Models");
        _list.PreviewMouseMove += (_, e) => HoverHighlight(e);
        _list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var row = RowAt(e.OriginalSource);
            _pressed = row is not null && !row.IsGroup && !InButton(e.OriginalSource) ? row : null;
            if (row is not null && row.IsGroup)
            {
                Toggle(row);
                e.Handled = true;
            }
        };
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var pressed = _pressed;
            _pressed = null;
            if (pressed is not null && ReferenceEquals(RowAt(e.OriginalSource), pressed) && !InButton(e.OriginalSource))
            {
                Pick(pressed);
            }
        };
        _list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, __) =>
        {
            UpdatePinned();
            _ = Dispatcher.InvokeAsync(UpdatePinned, DispatcherPriority.Loaded);
        }));

        _pinned = new ContentControl { VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Focusable = false };
        _pinned.SetResourceReference(ContentControl.ContentTemplateProperty, "Omp.ProviderHeaderTemplate");
        _pinned.MouseLeftButtonDown += (_, e) =>
        {
            if (_pinned.Content is ModelRowItem group)
            {
                CollapseFromPinned(group);
            }

            e.Handled = true;
        };

        _status = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 24, 16, 0), TextWrapping = TextWrapping.Wrap };
        _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Muted);
        var listArea = new Grid { Children = { _list, _pinned, _status } };
        SetRow(listArea, 1);
        Children.Add(listArea);

        PreviewKeyDown += OnPreviewKeyDown;
        Reset(null);
    }

    public event Action<ModelView>? Picked;

    /// <summary>Raised when the close button is pressed.</summary>
    public event Action? CloseRequested;

    public void SetSize(ModelPickerSize size)
    {
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>Clears the picker and shows <paramref name="status"/> until <see cref="SetModels"/> provides the catalog.</summary>
    public void Reset(string? status)
    {
        _catalog = null;
        _active = null;
        _expanded.Clear();
        _filter.Query = "";
        var saved = _preferences as IModelPickerFilters;
        _filter.FavoritesOnly = ReadFilter(() => saved?.FavoritesOnly ?? false);
        _filter.RecentOnly = ReadFilter(() => saved?.RecentOnly ?? false);
        _favorites.IsChecked = _filter.FavoritesOnly;
        _recent.IsChecked = _filter.RecentOnly;
        SyncToggles();
        _search.Text = "";
        _items = new List<ModelRowItem>();
        _list.ItemsSource = null;
        _pinned.Visibility = Visibility.Collapsed;
        SetStatus(status);
    }

    public void SetStatus(string? status)
    {
        _status.Text = status ?? "";
        _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows <paramref name="models"/>; the provider of the model in use is expanded and that model highlighted.</summary>
    public void SetModels(IReadOnlyList<ModelView> models, ModelKey? active)
    {
        Reset(null);
        _catalog = new ModelCatalog(models);
        _active = active;
        if (active is not null)
        {
            _expanded.Add(active.Provider);
        }

        Refresh(true);
    }

    public void FocusSearch() => _ = Dispatcher.InvokeAsync(() => _search.Focus(), DispatcherPriority.Input);

    /// <summary>Stars or unstars the highlighted model.</summary>
    internal void ToggleHighlightedFavorite()
    {
        if (_list.SelectedItem is ModelRowItem row && !row.IsGroup)
        {
            row.IsFavorite = !row.IsFavorite;
        }
    }

    private ModelUsage Usage() => new ModelUsage(_preferences.Favorites, _preferences.Recents, _active);

    private bool Narrowed => PickerSearch.Terms(_filter.Query).Length > 0 || _filter.FavoritesOnly || _filter.RecentOnly;

    private void SaveFilters()
    {
        if (!(_preferences is IModelPickerFilters saved))
        {
            return;
        }

        try
        {
            saved.FavoritesOnly = _filter.FavoritesOnly;
            saved.RecentOnly = _filter.RecentOnly;
        }
        catch (Exception error)
        {
            _fail("Saving the model filters failed", error);
        }
    }

    private bool ReadFilter(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception error)
        {
            _fail("Reading the model filters failed", error);

            return false;
        }
    }

    /// <summary>
    /// Rebuilds the list. With <paramref name="keepOffset"/> the list stays where it was scrolled; otherwise the
    /// model in use (when <paramref name="highlightActive"/>) or the first model is highlighted and brought into view.
    /// </summary>
    private void Refresh(bool highlightActive, double? keepOffset = null)
    {
        if (_catalog is null)
        {
            return;
        }

        var highlighted = (_list.SelectedItem as ModelRowItem)?.Entry?.Key;
        _items = [.. _catalog.Rows(_filter, Usage(), _expanded).Select(row =>
        {
            var item = new ModelRowItem(row);
            if (!item.IsGroup)
            {
                item.FavoriteChanged += OnFavoriteChanged;
            }

            return item;
        })];
        _list.ItemsSource = _items;
        SetStatus(_items.Count > 0 ? null : Narrowed ? "No models match." : "OMP reports no available models.");
        var target = (keepOffset is not null && highlighted is not null ? _items.FirstOrDefault(item => highlighted.Equals(item.Entry?.Key)) : null)
            ?? (highlightActive ? _items.FirstOrDefault(item => item.IsActive) : null)
            ?? (keepOffset is null ? _items.FirstOrDefault(item => !item.IsGroup) : null);
        _list.SelectedItem = target;
        if (keepOffset is not null)
        {
            var offset = keepOffset.Value;
            _ = Dispatcher.InvokeAsync(() => Scroller()?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        else if (target is not null)
        {
            BringIntoView(target, highlightActive);
        }
        else
        {
            Scroller()?.ScrollToTop();
        }
    }

    /// <summary>Shows <paramref name="target"/>; on opening, with its provider header above it when both fit.</summary>
    private void BringIntoView(ModelRowItem target, bool withHeader)
    {
        _list.ScrollIntoView(target);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_list.SelectedItem, target))
            {
                return;
            }

            _list.UpdateLayout();
            if (withHeader)
            {
                var index = _items.IndexOf(target);
                var group = index;
                while (group > 0 && !_items[group].IsGroup)
                {
                    group--;
                }

                _list.ScrollIntoView(_items[group]);
                _list.UpdateLayout();
            }
            _list.ScrollIntoView(target);
            if (_list.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement row)
            {
                row.BringIntoView(new Rect(row.RenderSize));
            }

            UpdatePinned();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Expands a provider, collapsing every other one and bringing its header to the top; or collapses it, leaving the list where it is.</summary>
    private void Toggle(ModelRowItem group)
    {
        if (Narrowed)
        {
            return;
        }

        if (group.IsExpanded)
        {
            _expanded.Remove(group.Provider);
            Refresh(false, Scroller()?.VerticalOffset ?? 0);

            return;
        }
        _expanded.Clear();
        _expanded.Add(group.Provider);
        ScrollHeaderToTop(group.Provider);
    }

    /// <summary>Rebuilds the list and scrolls the header of <paramref name="provider"/> to its top.</summary>
    private void ScrollHeaderToTop(string provider)
    {
        Refresh(false, 0);
        var header = _items.FirstOrDefault(item => item.IsGroup && item.Provider == provider);
        if (header is null)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _list.ScrollIntoView(header);
            _list.UpdateLayout();
            Scroller()?.ScrollToVerticalOffset(OffsetOf(header));
        }, DispatcherPriority.Background);
    }

    /// <summary>Collapses the provider whose header is pinned and scrolls its header back to the top of the list.</summary>
    private void CollapseFromPinned(ModelRowItem group)
    {
        if (Narrowed || !group.IsExpanded)
        {
            return;
        }

        _expanded.Remove(group.Provider);
        ScrollHeaderToTop(group.Provider);
    }

    /// <summary>Pins the header of the provider whose models are at the top of the list, hiding it while its own header is there.</summary>
    private void UpdatePinned()
    {
        var top = _list.InputHitTest(new Point(30, 1)) as DependencyObject;
        var row = top is null ? null : RowAt(top);
        if (row is null || row.IsGroup)
        {
            _pinned.Visibility = Visibility.Collapsed;

            return;
        }
        var index = _items.IndexOf(row);
        while (index > 0 && !_items[index].IsGroup)
        {
            index--;
        }

        var group = index >= 0 && index < _items.Count && _items[index].IsGroup ? _items[index] : null;
        _pinned.Content = group;
        _pinned.Visibility = group is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Calculates the vertical offset of a specified model row item relative to the scroller&apos;s current position.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The double result.</returns>
    private double OffsetOf(ModelRowItem item)
    {
        var scroller = Scroller();
        if (scroller is null || !(_list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row))
        {
            return 0;
        }

        return scroller.VerticalOffset + row.TranslatePoint(new Point(0, 0), scroller).Y;
    }

    /// <summary>
    /// Retrieves the ScrollViewer instance associated with the specified list.
    /// </summary>
    /// <returns>The scroll viewer? result.</returns>
    private ScrollViewer? Scroller() => Find<ScrollViewer>(_list);

    /// <summary>
    /// Retrieves the associated ModelRowItem from the data context of the ListBoxItem container corresponding to the specified source element.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The model row item? result.</returns>
    private ModelRowItem? RowAt(object source) =>
        source is DependencyObject node && ItemsControl.ContainerFromElement(_list, node) is ListBoxItem item ? item.DataContext as ModelRowItem : null;

    /// <summary>Whether <paramref name="source"/> is inside a button of a row, such as its favorite star.</summary>
    private bool InButton(object source)
    {
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, _list);
            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Updates the favorite status of a specific model row item within the user preferences.
    /// </summary>
    /// <param name="item">The item.</param>
    private void OnFavoriteChanged(ModelRowItem item)
    {
        try
        {
            _preferences.SetFavorite(item.Entry!.Key, item.IsFavorite);
        }
        catch (Exception error)
        {
            _fail("Saving the favorite models failed", error);
        }
    }

    /// <summary>
    /// Records the selected model row item in the user preferences and invokes the Picked event.
    /// </summary>
    /// <param name="row">The row.</param>
    private void Pick(ModelRowItem row)
    {
        if (row.Entry is null)
        {
            return;
        }

        try
        {
            _preferences.RecordPicked(row.Entry.Key);
        }
        catch (Exception error)
        {
            _fail("Saving the recent models failed", error);
        }
        Picked?.Invoke(row.Entry.Model);
    }

    /// <summary>
    /// Updates the visual state and styling of the favorites and recent toggle elements based on their current selection status.
    /// </summary>
    private void SyncToggles()
    {
        _favoritesGlyph.Text = _favorites.IsChecked == true ? StarFilledGlyph : StarGlyph;
        if (_favorites.IsChecked == true)
        {
            _favoritesGlyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Warning);
        }
        else
        {
            _favoritesGlyph.ClearValue(TextBlock.ForegroundProperty);
        }

        if (_recent.IsChecked == true)
        {
            _recent.SetResourceReference(Control.ForegroundProperty, ThemeKeys.ThemeAccent);
        }
        else
        {
            _recent.ClearValue(Control.ForegroundProperty);
        }
    }

    /// <summary>
    /// Handles the preview key down event by evaluating the pressed key and modifiers to determine if the input should be marked as handled.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>Runs the picker's action for <paramref name="key"/> pressed with <paramref name="modifiers"/>; false when the key is not the picker's.</summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        var row = _list.SelectedItem as ModelRowItem;
        var control = (modifiers & ModifierKeys.Control) != 0;
        switch (key)
        {
            case Key.Up:
                Move(-1);
                return true;

            case Key.Down:
                Move(1);
                return true;

            case Key.PageUp:
                Move(-PageRows);
                return true;

            case Key.PageDown:
                Move(PageRows);
                return true;

            case Key.Enter:
                if (row is not null && !row.IsGroup)
                {
                    Pick(row);
                }

                return true;

            case Key.D when control:
                ToggleHighlightedFavorite();
                return true;

            case Key.C when control && row?.Entry is not null && _search.SelectionLength == 0:
                _copyId(row.Entry.Id);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Highlights the model <paramref name="delta"/> models away, skipping provider headers, and scrolls it into view.</summary>
    private void Move(int delta)
    {
        var models = _items.Where(item => !item.IsGroup).ToList();
        if (models.Count == 0)
        {
            return;
        }

        var current = _list.SelectedItem is ModelRowItem row ? models.IndexOf(row) : -1;
        var next = current < 0 ? (delta > 0 ? 0 : models.Count - 1) : Math.Max(0, Math.Min(models.Count - 1, current + delta));
        var target = models[next];
        _list.SelectedItem = target;
        _list.ScrollIntoView(target);
    }

    /// <summary>Highlights the model under the pointer, but only when the pointer itself moved, so a list scrolling under a still pointer keeps its highlight.</summary>
    private void HoverHighlight(MouseEventArgs e)
    {
        var position = e.GetPosition(_list);
        if (position == _pointer)
        {
            return;
        }

        _pointer = position;
        if (RowAt(e.OriginalSource) is ModelRowItem row && !row.IsGroup && !ReferenceEquals(_list.SelectedItem, row))
        {
            _list.SelectedItem = row;
        }
    }

    /// <summary>
    /// Creates a TextBlock element configured as an icon glyph using the specified character and font size.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="size">The size.</param>
    /// <returns>The text block result.</returns>
    private static TextBlock Glyph(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.IconFont");

        return text;
    }

    /// <summary>
    /// Adds a UI element to a specified grid column and applies the provided margin if the element is a framework element.
    /// </summary>
    /// <param name="grid">The unique identifier of the gr.</param>
    /// <param name="element">The element.</param>
    /// <param name="column">The column.</param>
    /// <param name="margin">The margin.</param>
    private static void AddAt(Grid grid, UIElement element, int column, Thickness margin)
    {
        if (element is FrameworkElement framework)
        {
            framework.Margin = margin;
        }

        SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>A text box drawn as bare text, so the search bar's border is the only frame.</summary>
    private static ControlTemplate PlainTextBox()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);

        return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
    }

    /// <summary>
    /// Recursively searches the visual tree starting from the specified root element to find the first descendant of type T.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <returns>The t? result.</returns>
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (Find<T>(child) is T found)
            {
                return found;
            }
        }

        return null;
    }
}
