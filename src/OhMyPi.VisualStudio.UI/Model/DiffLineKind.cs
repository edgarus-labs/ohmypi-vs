namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Specifies the type of line encountered within a difference output, such as additions, deletions, hunk headers, metadata, or context.
/// </summary>
internal enum DiffLineKind { Add, Delete, Hunk, Meta, Context }
