using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>The user's stars and picks and the model in use, which decide what the rows show and where they sit.</summary>
internal sealed class ModelUsage
{
    public ModelUsage(IReadOnlyCollection<ModelKey> favorites, IReadOnlyList<ModelKey> recents, ModelKey? active)
    {
        Favorites = favorites;
        Recents = recents;
        Active = active;
    }

    public IReadOnlyCollection<ModelKey> Favorites { get; }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<ModelKey> Recents { get; }

    public ModelKey? Active { get; }
}
