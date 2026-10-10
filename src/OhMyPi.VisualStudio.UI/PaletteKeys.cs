namespace OhMyPi.VisualStudio.UI;

/// <summary>
/// Resource keys of the colors the chat derives from the Visual Studio theme rather than taking from it: the code
/// and output surfaces set apart from the window background, and the diff backgrounds of changed lines and words. The
/// host computes them from the theme's background and puts them in a resource dictionary above the chat control.
/// </summary>
public static class PaletteKeys
{
    /// <summary>Background of cards, inline code, table headers, the tool-name pill and the send button: the window background nudged toward the text.</summary>
    public const string CodeSurface = "Omp.Surface.Code";
    /// <summary>Border of the code surface, of tool output and of table frames: a little further from the window background than the surface.</summary>
    public const string CodeSurfaceBorder = "Omp.Surface.CodeBorder";
    /// <summary>Background of a tool's result: the window background moved at least a fixed visible step, away from the text where there is room, so it differs from both the window and the code surface.</summary>
    public const string OutputSurface = "Omp.Surface.Output";
    /// <summary>
    /// The diff added line.
    /// </summary>
    public const string DiffAddedLine = "Omp.Diff.AddedLine";
    /// <summary>
    /// The diff removed line.
    /// </summary>
    public const string DiffRemovedLine = "Omp.Diff.RemovedLine";
    /// <summary>
    /// The diff added word.
    /// </summary>
    public const string DiffAddedWord = "Omp.Diff.AddedWord";
    /// <summary>
    /// The diff removed word.
    /// </summary>
    public const string DiffRemovedWord = "Omp.Diff.RemovedWord";
}
