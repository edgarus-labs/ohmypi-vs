using System;

namespace Omp.Core.Changes;

/// <summary>File content at one moment: <see cref="Missing"/> when the file did not exist. A null snapshot means untrackable.</summary>
public sealed class Snapshot
{
    /// <summary>
    /// The missing.
    /// </summary>
    public static readonly Snapshot Missing = new Snapshot(false, null);

    /// <summary>
    /// Initializes a new instance of the Snapshot class with the specified existence status and content.
    /// </summary>
    /// <param name="exists">The exists.</param>
    /// <param name="content">The content.</param>
    private Snapshot(bool exists, string? content)
    {
        Exists = exists;
        Content = content;
    }

    /// <summary>
    /// Creates a new snapshot instance from the specified content string.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The snapshot result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an error occurs during execution.</exception>
    public static Snapshot Of(string content) => new Snapshot(true, content ?? throw new ArgumentNullException(nameof(content)));

    /// <summary>
    /// Gets a value indicating whether exists.
    /// </summary>
    public bool Exists { get; }

    /// <summary>
    /// Gets the content.
    /// </summary>
    public string? Content { get; }
}
