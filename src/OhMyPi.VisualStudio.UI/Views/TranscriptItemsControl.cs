using Omp.Core;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Virtualizing list of transcript items. A changed item arrives as a collection Replace: the view of a live item
/// (<see cref="ILiveView"/>) is updated in place, any other item's visual is rebuilt.
/// </summary>
internal sealed class TranscriptItemsControl : ItemsControl
{
    private readonly Dictionary<string, FrameworkElement> _live = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the render.
    /// </summary>
    public Func<TranscriptItem, FrameworkElement>? Render { get; set; }

    /// <summary>
    /// Gets or sets the previous.
    /// </summary>
    public Func<TranscriptItem, TranscriptItem?>? Previous { get; set; }

    /// <summary>
    /// Gets or sets the scroller.
    /// </summary>
    public ScrollViewer? Scroller { get; private set; }

    /// <summary>
    /// Occurs when scroller ready.
    /// </summary>
    public event EventHandler? ScrollerReady;

    /// <summary>
    /// Overrides the default template application logic to initialize the internal scroller reference and notify subscribers that the scroller is ready.
    /// </summary>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Scroller = GetTemplateChild("PART_Scroller") as ScrollViewer;
        ScrollerReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Determines whether the specified item acts as its own container.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    protected override bool IsItemItsOwnContainerOverride(object item) => false;

    /// <summary>
    /// Returns a configured ContentControl instance to serve as the item container, with focus properties disabled and a custom handler attached to the RequestBringIntoView event.
    /// </summary>
    /// <returns>The dependency object result.</returns>
    protected override DependencyObject GetContainerForItemOverride()
    {
        var container = new ContentControl { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        container.RequestBringIntoView += IgnoreTextBringIntoView;

        return container;
    }

    /// <summary>
    /// Read-only text keeps asking to be brought into view while it has focus (its caret and selection), which would
    /// pull the conversation away from the latest output; those requests stop at the item.
    /// </summary>
    private static void IgnoreTextBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        for (var node = e.TargetObject; node is not null && !ReferenceEquals(node, sender); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase)
            {
                e.Handled = true;

                return;
            }
        }
    }

    /// <summary>
    /// Prepares the item container by resolving the appropriate view for the transcript item, managing live view tracking, and configuring the container&apos;s content and layout margins.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="item">The item.</param>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        var container = (ContentControl)element;
        var transcriptItem = (TranscriptItem)item;
        var view = Reuse(transcriptItem) ?? Render?.Invoke(transcriptItem);
        if (view is ILiveView live && live.IsLive)
        {
            _live[transcriptItem.Id] = view;
        }

        container.Content = view;
        var previous = Previous?.Invoke(transcriptItem);
        container.Margin = new Thickness(0, ItemRenderer.GapAbove(transcriptItem, previous), 0, 0);
        (view as ISeparatedView)?.SetSeparated(ItemRenderer.IsAnswerToTools(transcriptItem, previous));
    }

    /// <summary>
    /// The live view of the item's previous version, updated to <paramref name="item"/>, or null. An update that
    /// throws falls back to a full render, which reports the failure in the item's place.
    /// </summary>
    private FrameworkElement? Reuse(TranscriptItem item)
    {
        if (!_live.TryGetValue(item.Id, out var view))
        {
            return null;
        }

        _live.Remove(item.Id);
        bool updated;
        try
        {
            updated = ((ILiveView)view).TryUpdate(item);
        }
        catch (Exception)
        {
            updated = false;
        }
        if (!updated)
        {
            return null;
        }

        if (view.Parent is ContentControl previous && ReferenceEquals(previous.Content, view))
        {
            previous.Content = null;
        }

        return view;
    }

    /// <summary>
    /// Clears the content of the specified content control associated with the given item.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="item">The item.</param>
    protected override void ClearContainerForItemOverride(DependencyObject element, object item) => ((ContentControl)element).Content = null;

    /// <summary>
    /// Updates the internal live collection by synchronizing it with the changes reported in the collection change arguments.
    /// </summary>
    /// <param name="e">The e.</param>
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _live.Clear();
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (TranscriptItem removed in e.OldItems)
            {
                _live.Remove(removed.Id);
            }
        }
        base.OnItemsChanged(e);
    }
}
