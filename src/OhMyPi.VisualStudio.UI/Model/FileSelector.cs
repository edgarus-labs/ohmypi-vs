namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A path with the first line of an optional OMP read selector.</summary>
internal readonly struct FileSelector
{
    public FileSelector(string path, int? line)
    {
        Path = path;
        Line = line;
    }

    public string Path { get; }

    public int? Line { get; }
}
