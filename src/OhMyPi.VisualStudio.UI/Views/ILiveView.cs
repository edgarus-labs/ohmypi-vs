using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A rendered transcript item that can take a newer version of the same item without being rebuilt.</summary>
internal interface ILiveView
{
    /// <summary>Whether more versions of the item are expected (a streaming answer, a running tool).</summary>
    bool IsLive { get; }

    /// <summary>Updates the view in place to <paramref name="item"/>; false when it must be rendered anew instead.</summary>
    bool TryUpdate(TranscriptItem item);
}
