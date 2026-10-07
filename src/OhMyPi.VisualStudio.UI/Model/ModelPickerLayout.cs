using System;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Sizes of the model picker popup in unzoomed units.</summary>
internal static class ModelPickerLayout
{
    /// <summary>The whole available room, never negative.</summary>
    public static ModelPickerSize Fit(double availableWidth, double availableHeight) =>
        new ModelPickerSize(Math.Max(0, availableWidth), Math.Max(0, availableHeight));
}
