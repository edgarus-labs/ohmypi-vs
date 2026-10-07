using System;

namespace OhMyPi.VisualStudio.UI;

/// <summary>A model's identity: the provider OMP lists it under and its technical id there.</summary>
public sealed class ModelKey : IEquatable<ModelKey>
{
    public ModelKey(string provider, string id)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    public string Provider { get; }

    public string Id { get; }

    public bool Equals(ModelKey? other) => other is not null && Provider == other.Provider && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as ModelKey);

    public override int GetHashCode() => unchecked(Provider.GetHashCode() * 397 ^ Id.GetHashCode());

    public override string ToString() => Provider + "/" + Id;
}
