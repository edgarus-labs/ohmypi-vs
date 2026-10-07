using System;

namespace OhMyPi.VisualStudio
{
    /// <summary>GUIDs shared with <c>OmpPackage.vsct</c> and the registration attributes.</summary>
    internal static class PackageGuids
    {
        public const string PackageString = "f4d10c0a-2f40-4d24-90e5-399d239064e3";
        public const string CommandSetString = "33df85cf-aade-453e-b0c3-d133b70ab0dc";
        public const string ToolWindowString = "4d2206e5-7d27-42cb-b880-775f46f3ef1c";
        public const string OptionsPageString = "c21a91da-b546-4e84-9f68-37d1e37502be";
        public const string OutputPaneString = "041098dc-0f3a-4e10-a029-7a4c3957461b";

        public static readonly Guid CommandSet = new Guid(CommandSetString);
        public static readonly Guid ToolWindow = new Guid(ToolWindowString);
        public static readonly Guid Images = new Guid("9b1f5c2e-6d0a-4f3e-8c47-2a51d7e0b6f4");
        public static readonly Guid OutputPane = new Guid(OutputPaneString);
    }

    /// <summary>Command and menu ids from <c>OmpPackage.vsct</c>.</summary>
    internal static class PackageIds
    {
        public const int ToolWindowToolbar = 0x1001;

        public const int Open = 0x0100;
        public const int ViewToolWindow = 0x0101;
        public const int NewSession = 0x0102;
        public const int ResumeSession = 0x0103;
        public const int SelectModel = 0x0104;
        public const int SelectThinkingLevel = 0x0105;
        public const int ToggleFastMode = 0x0106;
        public const int SendPrompt = 0x0107;
        public const int Abort = 0x0108;
        public const int Restart = 0x0109;
        public const int RenameSession = 0x010A;
        public const int ShowAgents = 0x010B;
        public const int ShowLog = 0x010C;
        public const int OpenSettings = 0x010D;
        public const int AddActiveFile = 0x010E;
        public const int AddSelectedFiles = 0x010F;
        public const int ShowUsage = 0x0110;
    }
}
