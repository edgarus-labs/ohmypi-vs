using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>
    /// The usage popup, laid over the top of the chat's transcript area in line with the composer: the rate limits of
    /// every configured provider, from OMP's <c>/usage</c>. Each provider is a heading with its color marker; each limit
    /// a row with its name, a bar of the share used in the provider's color, the percentage and when it resets. The
    /// account is in the tooltip and the limits this session draws on are brighter. Loaded each time it opens and on Refresh.
    /// </summary>
    internal sealed class UsagePopup
    {
        private const double TopGap = 6;

        private readonly FrameworkElement _area;
        private readonly FrameworkElement _alignTo;
        private readonly Func<Task<IReadOnlyList<ProviderUsage>>> _load;
        private readonly Action<string, Exception> _logError;
        private readonly Action _closed;
        private readonly StackPanel _rows = new StackPanel();
        private readonly ScrollViewer _body;
        private readonly Grid _view = new Grid();
        private bool _loading;

        /// <param name="area">The part of the chat the popup lies over.</param>
        /// <param name="alignTo">The element whose left and right edges the popup lines up with.</param>
        /// <param name="closed">Runs when the popup closes, to put focus back in the chat.</param>
        public UsagePopup(FrameworkElement area, FrameworkElement alignTo, Func<Task<IReadOnlyList<ProviderUsage>>> load, Action<string, Exception> logError, Action closed)
        {
            _area = area;
            _alignTo = alignTo;
            _load = load;
            _logError = logError;
            _closed = closed;

            var title = Ui.Text("Usage", weight: FontWeights.SemiBold);
            title.VerticalAlignment = VerticalAlignment.Center;
            var refresh = Ui.IconButton(Glyphs.Sync, "Refresh usage", () => _ = RefreshAsync());
            var close = Ui.IconButton(Glyphs.Cancel, "Close usage", Close);
            var bar = new DockPanel { Margin = new Thickness(14, 8, 8, 6) };
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(refresh, Dock.Right);
            bar.Children.Add(close);
            bar.Children.Add(refresh);
            bar.Children.Add(title);
            var barFrame = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bar }.Theme(Border.BorderBrushProperty, ThemeKeys.Divider);

            _body = new ScrollViewer
            {
                Content = _rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Padding = new Thickness(14, 6, 14, 10),
            };
            _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_body, 1);
            _view.Children.Add(barFrame);
            _view.Children.Add(_body);
            Ui.AutomationName(_view, "Usage");

            Popup = Ui.Popup(area, _view);
            Popup.Placement = PlacementMode.Relative;
            var frame = (Border)Popup.Child;
            frame.Padding = new Thickness(0);
            frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PopupBackground);
            frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.PopupBorder);
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(6);
            _view.SizeChanged += (_, __) => _view.Clip = new RectangleGeometry(new Rect(_view.RenderSize), 5, 5);
            Popup.Closed += (_, __) => _closed();
        }

        public Popup Popup { get; }

        public void Open()
        {
            ApplySize();
            Popup.IsOpen = true;
            _ = RefreshAsync();
        }

        public void Close() => Popup.IsOpen = false;

        /// <summary>As wide as <see cref="_alignTo"/>, at most as tall as the area less the top gap; sizes are in the chat's unzoomed units.</summary>
        private void ApplySize()
        {
            var zoom = Ui.ZoomOf(_area);
            var left = 0.0;
            var width = _area.ActualWidth;
            if (PresentationSource.FromVisual(_alignTo) != null && PresentationSource.FromVisual(_area) != null)
            {
                left = Math.Max(0, _alignTo.TranslatePoint(new Point(0, 0), _area).X);
                width = Math.Min(_alignTo.ActualWidth, _area.ActualWidth - left);
            }
            Popup.HorizontalOffset = left * zoom;
            Popup.VerticalOffset = TopGap * zoom;
            _view.Width = Math.Max(200, width - 2);
            _view.MaxHeight = Math.Max(120, _area.ActualHeight - TopGap - 2);
        }

        private async Task RefreshAsync()
        {
            if (_loading) return;
            _loading = true;
            _rows.Children.Clear();
            _rows.Children.Add(Ui.Muted("Loading usage…"));
            try
            {
                Render(await _load());
            }
            catch (Exception error)
            {
                _logError("Loading usage from OMP failed", error);
                _rows.Children.Clear();
                _rows.Children.Add(Ui.Muted("Usage is unavailable: " + error.Message, wrap: true));
            }
            finally
            {
                _loading = false;
            }
        }

        private void Render(IReadOnlyList<ProviderUsage> providers)
        {
            _rows.Children.Clear();
            var shown = providers.Where(provider => provider.Limits.Count > 0).ToList();
            if (shown.Count == 0) _rows.Children.Add(Ui.Muted("No provider reports usage limits."));
            foreach (var provider in shown)
            {
                var id = provider.Provider.ToLowerInvariant().Replace(' ', '-');
                var marker = Marker(id, 8);
                marker.Margin = new Thickness(0, 0, 8, 0);
                var name = Ui.Text(provider.Provider.ToUpperInvariant(), ThemeKeys.Muted, small: true, weight: FontWeights.SemiBold);
                var heading = Ui.Row(marker, name);
                heading.Margin = new Thickness(0, _rows.Children.Count == 0 ? 2 : 12, 0, 4);
                _rows.Children.Add(heading);
                foreach (var limit in provider.Limits) _rows.Children.Add(LimitRow(limit, id));
            }
        }

        /// <summary>A square in the provider's color, as in the model picker.</summary>
        private static Border Marker(string provider, double size)
        {
            var marker = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            marker.SetResourceReference(ThemeTint.AccentProperty, ThemeKeys.ThemeAccent);
            ThemeTint.SetProvider(marker, provider);
            return marker;
        }

        private static UIElement LimitRow(UsageLimit limit, string provider)
        {
            var used = Math.Max(0, Math.Min(100, limit.UsedPercent));
            var name = Ui.Text(limit.Name, limit.InUse ? null : ThemeKeys.Muted, small: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(16, 0, 0, 0);
            var fill = new Border { CornerRadius = new CornerRadius(2) };
            fill.SetResourceReference(ThemeTint.AccentProperty, ThemeKeys.ThemeAccent);
            ThemeTint.SetProvider(fill, provider);
            var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(used, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - used, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(2) }.Theme(Border.BackgroundProperty, ThemeKeys.Divider);
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(fill);
            var percent = Ui.Text($"{used:0}%", used >= 100 ? ThemeKeys.Error : used >= 80 ? ThemeKeys.Warning : null, small: true);
            percent.Width = 38;
            percent.TextAlignment = TextAlignment.Right;
            var resets = Ui.Muted(limit.Resets ?? "");
            resets.Width = 64;
            resets.Margin = new Thickness(8, 0, 0, 0);
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 40 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(percent, 2);
            Grid.SetColumn(resets, 3);
            row.Children.Add(name);
            row.Children.Add(bar);
            row.Children.Add(percent);
            row.Children.Add(resets);
            var resetText = limit.Resets != null ? $"\nResets {limit.Resets}" : "";
            row.ToolTip = $"{limit.Name}\n{limit.Account}\n{limit.UsedPercent:0.##}% used{resetText}{(limit.InUse ? "\nUsed by this session" : "")}";
            Ui.AutomationName(row, $"{limit.Name} {used:0}% used");
            return row;
        }
    }
}
