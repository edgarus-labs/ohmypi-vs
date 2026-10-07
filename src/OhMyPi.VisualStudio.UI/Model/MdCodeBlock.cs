namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class MdCodeBlock : MdBlock
{
    public MdCodeBlock(string? language, string code)
    {
        Language = language;
        Code = code;
    }

    public string? Language { get; }

    public string Code { get; }
}
