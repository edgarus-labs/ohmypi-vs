namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file or folder sent to OMP as an <c>@path</c> mention.</summary>
internal sealed class FileAttachment : Attachment
{
    /// <summary>
    /// Initializes a new instance of the FileAttachment class using the specified file path.
    /// </summary>
    /// <param name="path">The path.</param>
    public FileAttachment(string path) : base(System.IO.Path.GetFileName(path.TrimEnd('\\', '/')))
    {
        Path = path;
    }

    /// <summary>Absolute path.</summary>
    public string Path { get; }
}
