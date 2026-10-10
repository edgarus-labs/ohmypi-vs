namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Long pasted text, sent as a <c>&lt;pasted-text&gt;</c> block.</summary>
internal sealed class PastedTextAttachment : Attachment
{
    public PastedTextAttachment(string label, string text) : base(label)
    {
        Text = text;
    }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }
}
