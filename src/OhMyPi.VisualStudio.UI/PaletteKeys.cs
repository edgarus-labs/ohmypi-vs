namespace OhMyPi.VisualStudio.UI;

/// <summary>
/// Resource keys of the colors the chat derives from the Visual Studio theme rather than taking from it: the code
/// surface set apart from the window background, and the diff backgrounds of changed lines and words. The host
/// computes them from the theme's background and puts them in a resource dictionary above the chat control.
/// </summary>
public static class PaletteKeys
{
    /// <summary>Background of tool output, diffs and code blocks: the window background lifted a little.</summary>
    public const string CodeSurface = "Omp.Surface.Code";
    /// <summary>Border of the code surface and of table frames: a little further from the window background than the surface.</summary>
    public const string CodeSurfaceBorder = "Omp.Surface.CodeBorder";
    public const string DiffAddedLine = "Omp.Diff.AddedLine";
    public const string DiffRemovedLine = "Omp.Diff.RemovedLine";
    public const string DiffAddedWord = "Omp.Diff.AddedWord";
    public const string DiffRemovedWord = "Omp.Diff.RemovedWord";
}
