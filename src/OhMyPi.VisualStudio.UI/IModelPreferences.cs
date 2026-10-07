using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI;

/// <summary>The models the user starred and picked most recently, kept across Visual Studio sessions.</summary>
public interface IModelPreferences
{
    IReadOnlyList<ModelKey> Favorites { get; }

    /// <summary>Models picked before, most recent first.</summary>
    IReadOnlyList<ModelKey> Recents { get; }

    void SetFavorite(ModelKey model, bool favorite);

    /// <summary>Moves <paramref name="model"/> to the front of <see cref="Recents"/>.</summary>
    void RecordPicked(ModelKey model);
}
