using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

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
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        };
    }

    /// <summary>
    /// Gets the view.
    /// </summary>
    public ModelPickerView View { get; }

    /// <summary>
    /// Gets the popup.
    /// </summary>
    public Popup Popup { get; }

    /// <summary>
    /// Occurs when picked.
    /// </summary>
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

    /// <summary>
    /// Updates the status of the associated view with the specified status string.
    /// </summary>
    /// <param name="status">The status.</param>
    public void SetStatus(string? status) => View.SetStatus(status);

    /// <summary>
    /// Updates the view with a collection of model views and specifies which model should be active.
    /// </summary>
    /// <param name="models">The collection of models.</param>
    /// <param name="active">The active.</param>
    public void SetModels(IReadOnlyList<ModelView> models, ModelKey? active) => View.SetModels(models, active);

    /// <summary>
    /// Closes the popup window and returns focus to the associated anchor element.
    /// </summary>
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
        if (PresentationSource.FromVisual(_alignTo) is not null && PresentationSource.FromVisual(_area) is not null)
        {
            left = Math.Max(0, _alignTo.TranslatePoint(new Point(0, 0), _area).X);
            width = Math.Min(_alignTo.ActualWidth, _area.ActualWidth - left);
        }
        Popup.HorizontalOffset = left * zoom;
        Popup.VerticalOffset = TopGap * zoom;
        View.SetSize(ModelPickerLayout.Fit(width - 2, _area.ActualHeight - TopGap - 2));
    }

    /// <summary>
    /// Subscribes to the zoom transform change events of the anchor&apos;s owner to automatically update the popup size when it is open.
    /// </summary>
    private void WatchZoom()
    {
        if (_watchingZoom)
        {
            return;
        }

        var owner = FindOwner(_anchor);
        if (owner is null)
        {
            return;
        }

        _watchingZoom = true;
        owner.ZoomTransform.Changed += (_, __) =>
        {
            if (Popup.IsOpen)
            {
                ApplySize();
            }
        };
    }

    /// <summary>
    /// Traverses the visual or logical tree upwards from the specified element to find and return the nearest ancestor of type OmpChatControl.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The omp chat control? result.</returns>
    private static OmpChatControl? FindOwner(DependencyObject? element)
    {
        while (element is not null && !(element is OmpChatControl))
        {
            element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return element as OmpChatControl;
    }
}
