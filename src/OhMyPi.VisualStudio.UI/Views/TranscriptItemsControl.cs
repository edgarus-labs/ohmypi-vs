using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>A rendered transcript item that can take a newer version of the same item without being rebuilt.</summary>
    internal interface ILiveView
    {
        /// <summary>Whether more versions of the item are expected (a streaming answer, a running tool).</summary>
        bool IsLive { get; }

        /// <summary>Updates the view in place to <paramref name="item"/>; false when it must be rendered anew instead.</summary>
        bool TryUpdate(TranscriptItem item);
    }

    /// <summary>A rendered transcript item that is set off by a rule above it when it answers the tool calls before it.</summary>
    internal interface ISeparatedView
    {
        void SetSeparated(bool separated);
    }

    /// <summary>
    /// Virtualizing list of transcript items. A changed item arrives as a collection Replace: the view of a live item
    /// (<see cref="ILiveView"/>) is updated in place, any other item's visual is rebuilt.
    /// </summary>
    internal sealed class TranscriptItemsControl : ItemsControl
    {
        private readonly Dictionary<string, FrameworkElement> _live = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

        public Func<TranscriptItem, FrameworkElement>? Render { get; set; }

        public Func<TranscriptItem, TranscriptItem?>? Previous { get; set; }

        public ScrollViewer? Scroller { get; private set; }

        public event EventHandler? ScrollerReady;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Scroller = GetTemplateChild("PART_Scroller") as ScrollViewer;
            ScrollerReady?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsItemItsOwnContainerOverride(object item) => false;

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
            for (var node = e.TargetObject; node != null && !ReferenceEquals(node, sender); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            {
                if (node is TextBoxBase)
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            var container = (ContentControl)element;
            var transcriptItem = (TranscriptItem)item;
            var view = Reuse(transcriptItem) ?? Render?.Invoke(transcriptItem);
            if (view is ILiveView live && live.IsLive) _live[transcriptItem.Id] = view;
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
            if (!_live.TryGetValue(item.Id, out var view)) return null;
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
            if (!updated) return null;
            if (view.Parent is ContentControl previous && ReferenceEquals(previous.Content, view)) previous.Content = null;
            return view;
        }

        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            ((ContentControl)element).Content = null;
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) _live.Clear();
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TranscriptItem removed in e.OldItems) _live.Remove(removed.Id);
            }
            base.OnItemsChanged(e);
        }
    }
}
