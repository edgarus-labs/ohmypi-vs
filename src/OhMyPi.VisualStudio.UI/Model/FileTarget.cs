namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file a chat message points at, with an optional 1-based line.</summary>
internal sealed class FileTarget
{
    public FileTarget(string path, int? line)
    {
        Path = path;
        Line = line;
    }

    public string Path { get; }

    public int? Line { get; }
}
