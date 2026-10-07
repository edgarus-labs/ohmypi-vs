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

    public ObservableCollection<TranscriptItem> Items { get; } = new ObservableCollection<TranscriptItem>();

    /// <summary>Whether anything other than notices is shown.</summary>
    public bool HasConversation => _conversationItems > 0;

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

    private static int Weight(TranscriptItem item) => item is NoticeItem ? 0 : 1;

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
