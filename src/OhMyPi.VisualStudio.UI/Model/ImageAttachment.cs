namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Pasted or dropped image, sent in the prompt's <c>images</c>.</summary>
internal sealed class ImageAttachment : Attachment
{
    /// <summary>
    /// Initializes a new instance of the ImageAttachment class with the specified label, image data, MIME type, and byte size.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="data">The data.</param>
    /// <param name="mimeType">The mime type.</param>
    /// <param name="bytes">The bytes.</param>
    public ImageAttachment(string label, string data, string mimeType, long bytes) : base(label)
    {
        Data = data;
        MimeType = mimeType;
        Bytes = bytes;
    }

    /// <summary>Base64 bytes.</summary>
    public string Data { get; }

    /// <summary>
    /// Gets the mime type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the bytes.
    /// </summary>
    public long Bytes { get; }
}
