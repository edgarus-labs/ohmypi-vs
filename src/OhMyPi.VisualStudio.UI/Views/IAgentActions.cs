using Omp.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>What the Agents section asks of the chat control.</summary>
internal interface IAgentActions
{
    RenderContext RenderContext { get; }

    Task<IReadOnlyList<TranscriptItem>> TranscriptAsync(string agentId);

    Task SteerAsync(string agentId, string message);

    Task<bool> CancelAsync(string agentId);

    /// <summary>Runs a user action, reporting a failure as an error notice.</summary>
    Task RunAsync(string action, Func<Task> work);

    void Notice(NoticeLevel level, string text);
}
