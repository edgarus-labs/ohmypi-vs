namespace OhMyPi.VisualStudio.UI.Model;

internal readonly struct ModelPickerSize
{
    /// <summary>
    /// Initializes a new instance of the ModelPickerSize struct with the specified width and height.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public ModelPickerSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public double Height { get; }
}
