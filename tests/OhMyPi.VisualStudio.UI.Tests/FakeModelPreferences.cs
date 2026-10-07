using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Tests;

internal sealed class FakeModelPreferences : IModelPreferences
{
    private readonly List<ModelKey> _favorites = new List<ModelKey>();
    private readonly List<ModelKey> _recents = new List<ModelKey>();

    public IReadOnlyList<ModelKey> Favorites => _favorites.ToArray();

    public IReadOnlyList<ModelKey> Recents => _recents.ToArray();

    /// <summary>When set, saving a favorite or a pick throws it.</summary>
    public Exception? SaveError { get; set; }

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
