namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class ErrorItem
{
    /// <summary>error, warning or message.</summary>
    public string Severity { get; set; } = "error";

    public string? Code { get; set; }

    public string Message { get; set; } = "";

    public string? Path { get; set; }

    public int Line { get; set; }

    public int Column { get; set; }

    public string? Project { get; set; }
}
