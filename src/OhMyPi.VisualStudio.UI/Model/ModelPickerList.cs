using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>One model of the catalog; <see cref="Variant"/> tells apart entries of one provider that share a display name.</summary>
internal sealed class ModelEntry
{
    /// <summary>
    /// Initializes a new instance of the ModelEntry class using the specified model view and optional variant, while automatically generating the associated model key.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="variant">The variant.</param>
    public ModelEntry(ModelView model, string? variant)
    {
        Model = model;
        Variant = variant;
        Key = new ModelKey(model.Provider, model.Id);
    }

    /// <summary>
    /// Gets the model.
    /// </summary>
    public ModelView Model { get; }

    /// <summary>
    /// Gets the key.
    /// </summary>
    public ModelKey Key { get; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => Model.Name;

    /// <summary>
    /// Gets the provider.
    /// </summary>
    public string Provider => Model.Provider;

    /// <summary>
    /// Gets the id.
    /// </summary>
    public string Id => Model.Id;

    /// <summary>
    /// Gets the variant.
    /// </summary>
    public string? Variant { get; }
}
