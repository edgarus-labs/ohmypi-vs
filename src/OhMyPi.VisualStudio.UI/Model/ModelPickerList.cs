using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>One model of the catalog; <see cref="Variant"/> tells apart entries of one provider that share a display name.</summary>
internal sealed class ModelEntry
{
    public ModelEntry(ModelView model, string? variant)
    {
        Model = model;
        Variant = variant;
        Key = new ModelKey(model.Provider, model.Id);
    }

    public ModelView Model { get; }

    public ModelKey Key { get; }

    public string Name => Model.Name;

    public string Provider => Model.Provider;

    public string Id => Model.Id;

    public string? Variant { get; }
}
