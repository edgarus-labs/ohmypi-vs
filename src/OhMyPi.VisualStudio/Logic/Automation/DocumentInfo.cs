namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class DocumentInfo
{
    public string Path { get; set; } = "";

    public bool IsDirty { get; set; }

    public bool IsActive { get; set; }

    public bool IsReadOnly { get; set; }
}
