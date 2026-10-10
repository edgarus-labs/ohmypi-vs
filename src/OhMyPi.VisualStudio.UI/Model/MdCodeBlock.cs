namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a Markdown block element containing a segment of source code and its associated programming language.
/// </summary>
internal sealed class MdCodeBlock : MdBlock
{
    /// <summary>
    /// Initializes a new instance of the MdCodeBlock class with the specified programming language and source code.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="code">The code.</param>
    public MdCodeBlock(string? language, string code)
    {
        Language = language;
        Code = code;
    }

    /// <summary>
    /// Gets the language.
    /// </summary>
    public string? Language { get; }

    /// <summary>
    /// Gets the code.
    /// </summary>
    public string Code { get; }
}
