using Omp.Core;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

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
