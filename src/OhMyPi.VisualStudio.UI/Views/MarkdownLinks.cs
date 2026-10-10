using OhMyPi.VisualStudio.UI.Model;
using System;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>What links in rendered Markdown do: web URLs open in the browser, file references in the editor.</summary>
internal sealed class MarkdownLinks
{
    /// <summary>
    /// Initializes a new instance of the MarkdownLinks class with the specified delegates for opening URLs, resolving files, and opening files.
    /// </summary>
    /// <param name="openUrl">The open url.</param>
    /// <param name="resolveFile">The resolve file.</param>
    /// <param name="openFile">The open file.</param>
    public MarkdownLinks(Action<string> openUrl, Func<string, FileTarget?> resolveFile, Action<string, int?> openFile)
    {
        OpenUrl = openUrl;
        ResolveFile = resolveFile;
        OpenFile = openFile;
    }

    /// <summary>
    /// Gets the open url.
    /// </summary>
    public Action<string> OpenUrl { get; }

    /// <summary>The existing file a code span or link target names, or null.</summary>
    public Func<string, FileTarget?> ResolveFile { get; }

    /// <summary>
    /// Gets the open file.
    /// </summary>
    public Action<string, int?> OpenFile { get; }
}
