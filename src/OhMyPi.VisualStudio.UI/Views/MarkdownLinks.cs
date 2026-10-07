using OhMyPi.VisualStudio.UI.Model;
using System;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>What links in rendered Markdown do: web URLs open in the browser, file references in the editor.</summary>
internal sealed class MarkdownLinks
{
    public MarkdownLinks(Action<string> openUrl, Func<string, FileTarget?> resolveFile, Action<string, int?> openFile)
    {
        OpenUrl = openUrl;
        ResolveFile = resolveFile;
        OpenFile = openFile;
    }

    public Action<string> OpenUrl { get; }

    /// <summary>The existing file a code span or link target names, or null.</summary>
    public Func<string, FileTarget?> ResolveFile { get; }

    public Action<string, int?> OpenFile { get; }
}
