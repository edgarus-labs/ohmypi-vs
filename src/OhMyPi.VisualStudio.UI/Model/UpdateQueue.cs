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

    /// <summary>
    /// Gets a value indicating whether is empty.
    /// </summary>
    private bool IsEmpty => _order.Count == 0 && _connection is null && _reset is null && _session is null && _agents is null && !_changes;

    /// <summary>
    /// Executes the specified action within a thread-safe lock and returns a value indicating whether the queue was empty prior to the operation.
    /// </summary>
    /// <param name="add">The add.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    private bool Enqueue(Action add)
    {
        lock (_gate)
        {
            var wasEmpty = IsEmpty;
            add();

            return wasEmpty;
        }
    }

    /// <summary>
    /// Adds a transcript item to the processing queue, ensuring the item is stored and its unique identifier is tracked in the processing order.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
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

    /// <summary>
    /// Enqueues an operation to update the current session with the specified session view.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool EnqueueSession(SessionView session) => Enqueue(() => _session = session);

    /// <summary>
    /// Enqueues an operation to update the current collection of agents with the specified list of agent views.
    /// </summary>
    /// <param name="agents">The collection of agents.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool EnqueueAgents(IReadOnlyList<AgentView> agents) => Enqueue(() => _agents = agents);

    /// <summary>
    /// Enqueues an operation to update the current connection status.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool EnqueueConnection(ConnectionStatus connection) => Enqueue(() => _connection = connection);

    /// <summary>
    /// Enqueues an operation to mark the current state as having pending changes.
    /// </summary>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool EnqueueChanges() => Enqueue(() => _changes = true);

    /// <summary>
    /// Extracts all currently queued items and associated state into a new UpdateBatch and resets the internal buffer.
    /// </summary>
    /// <returns>The update batch result.</returns>
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
