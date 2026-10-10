using System;

namespace OhMyPi.VisualStudio;

/// <summary>GUIDs shared with <c>OmpPackage.vsct</c> and the registration attributes.</summary>
internal static class PackageGuids
{
    /// <summary>
    /// The package string.
    /// </summary>
    public const string PackageString = "f4d10c0a-2f40-4d24-90e5-399d239064e3";
    /// <summary>
    /// The command set string.
    /// </summary>
    public const string CommandSetString = "33df85cf-aade-453e-b0c3-d133b70ab0dc";
    /// <summary>
    /// The tool window string.
    /// </summary>
    public const string ToolWindowString = "4d2206e5-7d27-42cb-b880-775f46f3ef1c";
    /// <summary>
    /// The options page string.
    /// </summary>
    public const string OptionsPageString = "c21a91da-b546-4e84-9f68-37d1e37502be";
    /// <summary>
    /// The output pane string.
    /// </summary>
    public const string OutputPaneString = "041098dc-0f3a-4e10-a029-7a4c3957461b";

    /// <summary>
    /// The command set.
    /// </summary>
    public static readonly Guid CommandSet = new Guid(CommandSetString);
    /// <summary>
    /// The tool window.
    /// </summary>
    public static readonly Guid ToolWindow = new Guid(ToolWindowString);
    /// <summary>
    /// The images.
    /// </summary>
    public static readonly Guid Images = new Guid("9b1f5c2e-6d0a-4f3e-8c47-2a51d7e0b6f4");
    /// <summary>
    /// The output pane.
    /// </summary>
    public static readonly Guid OutputPane = new Guid(OutputPaneString);
}
