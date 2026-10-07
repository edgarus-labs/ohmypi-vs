namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file or folder sent to OMP as an <c>@path</c> mention.</summary>
internal sealed class FileAttachment : Attachment
{
    public FileAttachment(string path) : base(System.IO.Path.GetFileName(path.TrimEnd('\\', '/')))
    {
        Path = path;
    }

    /// <summary>Absolute path.</summary>
    public string Path { get; }
}
