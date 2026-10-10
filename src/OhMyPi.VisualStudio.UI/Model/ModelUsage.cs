using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>The user's stars and picks and the model in use, which decide what the rows show and where they sit.</summary>
internal sealed class ModelUsage
{
    /// <summary>
    /// Initializes a new instance of the ModelUsage class with the specified favorite, recent, and active model keys.
    /// </summary>
    /// <param name="favorites">The favorites.</param>
    /// <param name="recents">The collection of recents.</param>
    /// <param name="active">The active.</param>
    public ModelUsage(IReadOnlyCollection<ModelKey> favorites, IReadOnlyList<ModelKey> recents, ModelKey? active)
    {
        Favorites = favorites;
        Recents = recents;
        Active = active;
    }

    /// <summary>
    /// Gets the favorites.
    /// </summary>
    public IReadOnlyCollection<ModelKey> Favorites { get; }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<ModelKey> Recents { get; }

    /// <summary>
    /// Gets the active.
    /// </summary>
    public ModelKey? Active { get; }
}
