using Omp.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// The transcript as an observable list reconciled by item id: a known id replaces its entry in place,
/// a new id appends. Local notices (raised by the UI, not OMP) get <c>local-N</c> ids.
/// </summary>
internal sealed class TranscriptList
{
    private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
    private int _localNotices;
    private int _conversationItems;
    private ActivityItem? _activity;

    /// <summary>
    /// Gets the items.
    /// </summary>
    public ObservableCollection<TranscriptItem> Items { get; } = new ObservableCollection<TranscriptItem>();

    /// <summary>Whether anything other than notices is shown.</summary>
    public bool HasConversation => _conversationItems > 0;

    /// <summary>
    /// Adds a new transcript item or updates an existing one, while maintaining the total conversation weight and the item&apos;s position within the collection.
    /// </summary>
    /// <param name="item">The item.</param>
    public void Upsert(TranscriptItem item)
    {
        if (_index.TryGetValue(item.Id, out var index))
        {
            _conversationItems += Weight(item) - Weight(Items[index]);
            Items[index] = item;

            return;
        }
        var at = _activity is null ? Items.Count : Items.Count - 1;
        _index[item.Id] = at;
        _conversationItems += Weight(item);
        Items.Insert(at, item);
    }

    /// <summary>
    /// Calculates the weight of a transcript item, returning zero for notice items and one for all other item types.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The int result.</returns>
    private static int Weight(TranscriptItem item) => item is NoticeItem ? 0 : 1;

    /// <summary>
    /// Resets the internal state and index of the transcript and repopulates it with the specified collection of transcript items.
    /// </summary>
    /// <param name="items">The collection of items.</param>
    public void Reset(IReadOnlyList<TranscriptItem> items)
    {
        _index.Clear();
        _conversationItems = 0;
        var activity = _activity;
        _activity = null;
        Items.Clear();
        foreach (var item in items)
        {
            Upsert(item);
        }

        SetActivity(activity);
    }

    /// <summary>
    /// Adds a new notice with the specified severity level and text to the local notice collection.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="text">The text.</param>
    public void AddNotice(NoticeLevel level, string text) =>
        Upsert(new NoticeItem { Id = $"local-{++_localNotices}", Level = level, Text = text });

    /// <summary>Shows, updates or (with null) removes the activity row, which always stays last.</summary>
    public void SetActivity(ActivityItem? activity)
    {
        if (activity is null)
        {
            if (_activity is not null)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        }
        else if (_activity is null)
        {
            Items.Add(activity);
        }
        else
        {
            Items[Items.Count - 1] = activity;
        }

        _activity = activity;
    }

    /// <summary>The item shown just before <paramref name="item"/>, if any.</summary>
    public TranscriptItem? Previous(TranscriptItem item)
    {
        if (item is ActivityItem)
        {
            return Items.Count > 1 ? Items[Items.Count - 2] : null;
        }

        return _index.TryGetValue(item.Id, out var index) && index > 0 ? Items[index - 1] : null;
    }
}
