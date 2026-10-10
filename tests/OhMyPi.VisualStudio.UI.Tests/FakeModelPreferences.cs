using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>
/// Provides a mock implementation of the model preferences contract for testing purposes, allowing for the simulation of favorite and recently picked model keys.
/// </summary>
internal sealed class FakeModelPreferences : IModelPreferences
{
    private readonly List<ModelKey> _favorites = new List<ModelKey>();
    private readonly List<ModelKey> _recents = new List<ModelKey>();

    /// <summary>
    /// Gets the collection of favorites.
    /// </summary>
    public IReadOnlyList<ModelKey> Favorites => _favorites.ToArray();

    /// <summary>
    /// Gets the collection of recents.
    /// </summary>
    public IReadOnlyList<ModelKey> Recents => _recents.ToArray();

    /// <summary>When set, saving a favorite or a pick throws it.</summary>
    public Exception? SaveError { get; set; }

    /// <summary>
    /// Updates the favorite status of the specified model by adding or removing it from the favorites collection.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="favorite">The favorite.</param>
    public void SetFavorite(ModelKey model, bool favorite)
    {
        if (SaveError is not null)
        {
            throw SaveError;
        }

        _favorites.Remove(model);
        if (favorite)
        {
            _favorites.Add(model);
        }
    }

    /// <summary>
    /// Records the specified model as the most recently picked item by moving it to the top of the recents collection.
    /// </summary>
    /// <param name="model">The model.</param>
    public void RecordPicked(ModelKey model)
    {
        if (SaveError is not null)
        {
            throw SaveError;
        }

        _recents.Remove(model);
        _recents.Insert(0, model);
    }
}
