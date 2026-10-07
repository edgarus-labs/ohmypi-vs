namespace OhMyPi.VisualStudio.UI.Model;

internal readonly struct ModelPickerSize
{
    public ModelPickerSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }
}
