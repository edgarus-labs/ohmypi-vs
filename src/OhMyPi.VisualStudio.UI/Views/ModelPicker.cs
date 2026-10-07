using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>The model picker: a <see cref="ModelPickerView"/> in a popup laid over the chat's transcript area.</summary>
    internal sealed class ModelPicker
    {
        /// <summary>Space between the line above the area and the popup's top edge.</summary>
        private const double TopGap = 6;

        private readonly FrameworkElement _anchor;
        private readonly FrameworkElement _area;
        private readonly FrameworkElement _alignTo;
        private bool _watchingZoom;

        /// <param name="anchor">The control that opens the picker and takes focus back when it closes.</param>
        /// <param name="area">The part of the chat the popup covers.</param>
        /// <param name="alignTo">The element below the area whose left and right edges the popup lines up with.</param>
        public ModelPicker(FrameworkElement anchor, FrameworkElement area, FrameworkElement alignTo, IModelPreferences preferences, Action<string> copyId, Action<string, Exception> fail)
        {
            _anchor = anchor;
            _area = area;
            _alignTo = alignTo;
            View = new ModelPickerView(preferences, copyId, fail);
            View.Picked += model =>
            {
                Close();
                Picked?.Invoke(model);
            };
            View.CloseRequested += Close;
            Popup = Ui.Popup(area, View);
            Popup.Placement = PlacementMode.Relative;
            var frame = (Border)Popup.Child;
            frame.Padding = new Thickness(0);
            frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PopupBackground);
            frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.PopupBorder);
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(6);
            View.SizeChanged += (_, __) => View.Clip = new RectangleGeometry(new Rect(View.RenderSize), 5, 5);
            Popup.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Close();
                e.Handled = true;
            };
        }

        public ModelPickerView View { get; }

        public Popup Popup { get; }

        public event Action<ModelView>? Picked;

        /// <summary>Opens the popup showing <paramref name="status"/> until <see cref="SetModels"/> provides the catalog.</summary>
        public void Open(string? status = null)
        {
            View.Reset(status);
            ApplySize();
            WatchZoom();
            Popup.IsOpen = true;
            View.FocusSearch();
        }

        public void SetStatus(string? status) => View.SetStatus(status);

        public void SetModels(IReadOnlyList<ModelView> models, ModelKey? active) => View.SetModels(models, active);

        public void Close()
        {
            Popup.IsOpen = false;
            _anchor.Focus();
        }

        /// <summary>
        /// Covers the area's height less a small gap at the top, so the frame's rounded corners stand clear of the line
        /// above the area; its sides are in line with <see cref="_alignTo"/>. Sizes are in the chat's unzoomed units, as the popup content is.
        /// </summary>
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
            View.SetSize(ModelPickerLayout.Fit(width - 2, _area.ActualHeight - TopGap - 2));
        }

        private void WatchZoom()
        {
            if (_watchingZoom) return;
            var owner = FindOwner(_anchor);
            if (owner == null) return;
            _watchingZoom = true;
            owner.ZoomTransform.Changed += (_, __) =>
            {
                if (Popup.IsOpen) ApplySize();
            };
        }

        private static OmpChatControl? FindOwner(DependencyObject? element)
        {
            while (element != null && !(element is OmpChatControl))
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            return element as OmpChatControl;
        }
    }
}
