namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Pasted or dropped image, sent in the prompt's <c>images</c>.</summary>
internal sealed class ImageAttachment : Attachment
{
    public ImageAttachment(string label, string data, string mimeType, long bytes) : base(label)
    {
        Data = data;
        MimeType = mimeType;
        Bytes = bytes;
    }

    /// <summary>Base64 bytes.</summary>
    public string Data { get; }

    public string MimeType { get; }

    public long Bytes { get; }
}
