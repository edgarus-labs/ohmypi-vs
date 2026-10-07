using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
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
            var at = _activity == null ? Items.Count : Items.Count - 1;
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
            foreach (var item in items) Upsert(item);
            SetActivity(activity);
        }

        public void AddNotice(NoticeLevel level, string text) =>
            Upsert(new NoticeItem { Id = $"local-{++_localNotices}", Level = level, Text = text });

        /// <summary>Shows, updates or (with null) removes the activity row, which always stays last.</summary>
        public void SetActivity(ActivityItem? activity)
        {
            if (activity == null)
            {
                if (_activity != null) Items.RemoveAt(Items.Count - 1);
            }
            else if (_activity == null) Items.Add(activity);
            else Items[Items.Count - 1] = activity;
            _activity = activity;
        }

        /// <summary>The item shown just before <paramref name="item"/>, if any.</summary>
        public TranscriptItem? Previous(TranscriptItem item)
        {
            if (item is ActivityItem) return Items.Count > 1 ? Items[Items.Count - 2] : null;
            return _index.TryGetValue(item.Id, out var index) && index > 0 ? Items[index - 1] : null;
        }
    }

    /// <summary>
    /// Follows new output only while the reader is at the bottom: scrolling up stops following and offers
    /// "jump to latest"; returning to the bottom resumes. Only the reader's own scrolling (<see cref="ReaderScrolling"/>)
    /// turns following off or on; the view moved by layout (content measured, zoom, focus) keeps whatever was chosen.
    /// </summary>
    internal sealed class FollowBottom
    {
        private const double Threshold = 24;

        public bool Stick { get; private set; } = true;

        public bool JumpVisible { get; private set; }

        /// <summary>True while the reader drives the scroll (wheel, keys, scroll bar); other offset changes come from layout.</summary>
        public bool ReaderScrolling { get; set; }

        public static bool IsAtBottom(double extentHeight, double offset, double viewportHeight) =>
            extentHeight - offset - viewportHeight <= Threshold;

        /// <summary>
        /// Feeds one scroll-changed notification. Returns true when the view should scroll to the bottom.
        /// An offset change the reader made decides whether to follow; any other offset change, and a pure extent or
        /// viewport change, is layout, which brings a following view back to the bottom.
        /// </summary>
        public bool OnScrollChanged(double extentHeight, double offset, double viewportHeight, double extentChange, double viewportChange, double offsetChange)
        {
            if (offsetChange != 0)
            {
                if (ReaderScrolling)
                {
                    Stick = IsAtBottom(extentHeight, offset, viewportHeight);
                    JumpVisible = !Stick;
                    return false;
                }
                if (!Stick) return false;
                JumpVisible = false;
                return !IsAtBottom(extentHeight, offset, viewportHeight);
            }
            if (extentChange == 0 && viewportChange == 0) return false;
            if (Stick) return true;
            JumpVisible = true;
            return false;
        }

        /// <summary>Sticks to the bottom again (jump button, sent prompt).</summary>
        public void ToBottom()
        {
            Stick = true;
            JumpVisible = false;
        }

        /// <summary>Stops following before a keyboard scroll up, so content measured while it scrolls cannot pull the view back down.</summary>
        public void Release()
        {
            Stick = false;
            JumpVisible = true;
        }
    }

    /// <summary>One coalesced set of service updates applied in a single UI pass.</summary>
    internal sealed class UpdateBatch
    {
        public ConnectionStatus? Connection { get; set; }
        public IReadOnlyList<TranscriptItem>? Reset { get; set; }
        public SessionView? Session { get; set; }
        public IReadOnlyList<TranscriptItem> Items { get; set; } = Array.Empty<TranscriptItem>();
        public IReadOnlyList<AgentView>? Agents { get; set; }
        public bool ChangesChanged { get; set; }
    }

    /// <summary>
    /// Thread-safe coalescing of service events raised on background threads: only the latest instance of each
    /// transcript item (in first-arrival order), session, agents and connection survives until the UI drains it.
    /// Each Enqueue returns true when the queue was empty, i.e. a flush must be scheduled.
    /// </summary>
    internal sealed class UpdateQueue
    {
        private readonly object _gate = new object();
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, TranscriptItem> _items = new Dictionary<string, TranscriptItem>(StringComparer.Ordinal);
        private ConnectionStatus? _connection;
        private IReadOnlyList<TranscriptItem>? _reset;
        private SessionView? _session;
        private IReadOnlyList<AgentView>? _agents;
        private bool _changes;

        private bool IsEmpty => _order.Count == 0 && _connection == null && _reset == null && _session == null && _agents == null && !_changes;

        private bool Enqueue(Action add)
        {
            lock (_gate)
            {
                var wasEmpty = IsEmpty;
                add();
                return wasEmpty;
            }
        }

        public bool EnqueueItem(TranscriptItem item) => Enqueue(() =>
        {
            if (!_items.ContainsKey(item.Id)) _order.Add(item.Id);
            _items[item.Id] = item;
        });

        /// <summary>A wholesale transcript replacement supersedes every item queued before it.</summary>
        public bool EnqueueReset(IReadOnlyList<TranscriptItem> items) => Enqueue(() =>
        {
            _order.Clear();
            _items.Clear();
            _reset = items;
        });

        public bool EnqueueSession(SessionView session) => Enqueue(() => _session = session);

        public bool EnqueueAgents(IReadOnlyList<AgentView> agents) => Enqueue(() => _agents = agents);

        public bool EnqueueConnection(ConnectionStatus connection) => Enqueue(() => _connection = connection);

        public bool EnqueueChanges() => Enqueue(() => _changes = true);

        public UpdateBatch Drain()
        {
            lock (_gate)
            {
                var batch = new UpdateBatch
                {
                    Connection = _connection,
                    Reset = _reset,
                    Session = _session,
                    Items = _order.Select(id => _items[id]).ToList(),
                    Agents = _agents,
                    ChangesChanged = _changes,
                };
                _order.Clear();
                _items.Clear();
                _connection = null;
                _reset = null;
                _session = null;
                _agents = null;
                _changes = false;
                return batch;
            }
        }
    }
}
