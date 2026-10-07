using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

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

    private bool IsEmpty => _order.Count == 0 && _connection is null && _reset is null && _session is null && _agents is null && !_changes;

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
        if (!_items.ContainsKey(item.Id))
        {
            _order.Add(item.Id);
        }

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
