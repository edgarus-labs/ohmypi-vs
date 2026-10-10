using Omp.Core;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>One coalesced set of service updates applied in a single UI pass.</summary>
internal sealed class UpdateBatch
{
    /// <summary>
    /// Gets or sets the connection.
    /// </summary>
    public ConnectionStatus? Connection { get; set; }

    /// <summary>
    /// Gets or sets the collection of reset.
    /// </summary>
    public IReadOnlyList<TranscriptItem>? Reset { get; set; }

    /// <summary>
    /// Gets or sets the session.
    /// </summary>
    public SessionView? Session { get; set; }

    /// <summary>
    /// Gets or sets the collection of items.
    /// </summary>
    public IReadOnlyList<TranscriptItem> Items { get; set; } = Array.Empty<TranscriptItem>();

    /// <summary>
    /// Gets or sets the collection of agents.
    /// </summary>
    public IReadOnlyList<AgentView>? Agents { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether changes changed.
    /// </summary>
    public bool ChangesChanged { get; set; }
}
