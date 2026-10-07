using System;

namespace Omp.Core.Changes;

/// <summary>File content at one moment: <see cref="Missing"/> when the file did not exist. A null snapshot means untrackable.</summary>
public sealed class Snapshot
{
    public static readonly Snapshot Missing = new Snapshot(false, null);

    private Snapshot(bool exists, string? content)
    {
        Exists = exists;
        Content = content;
    }

    public static Snapshot Of(string content) => new Snapshot(true, content ?? throw new ArgumentNullException(nameof(content)));

    public bool Exists { get; }

    public string? Content { get; }
}
