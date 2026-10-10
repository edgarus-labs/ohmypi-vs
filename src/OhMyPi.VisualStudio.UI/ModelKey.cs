using System;

namespace OhMyPi.VisualStudio.UI;

/// <summary>A model's identity: the provider OMP lists it under and its technical id there.</summary>
public sealed class ModelKey : IEquatable<ModelKey>
{
    /// <summary>
    /// Initializes a new instance of the ModelKey class using the specified provider and identifier.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="id">The unique identifier.</param>
    /// <exception cref="ArgumentNullException">Thrown when an error occurs during execution.</exception>
    public ModelKey(string provider, string id)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>
    /// Gets the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Determines whether the current instance is equal to the specified model key by comparing their provider and identifier values.
    /// </summary>
    /// <param name="other">The other.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool Equals(ModelKey? other) => other is not null && Provider == other.Provider && Id == other.Id;

    /// <summary>
    /// Determines whether the specified object is equal to the current ModelKey instance.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public override bool Equals(object? obj) => Equals(obj as ModelKey);

    /// <summary>
    /// Returns a hash code based on the combined values of the Provider and Id properties.
    /// </summary>
    /// <returns>The int result.</returns>
    public override int GetHashCode() => unchecked(Provider.GetHashCode() * 397 ^ Id.GetHashCode());

    /// <summary>
    /// Returns a string representation of the object by concatenating the provider and identifier.
    /// </summary>
    /// <returns>The string result.</returns>
    public override string ToString() => Provider + "/" + Id;
}
