namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Specifies the available renderer types used to determine the visual representation for different context domains such as files, searches, shells, tasks, LSP, and MCP.
/// </summary>
internal enum RendererKind { File, Search, Shell, Task, Lsp, Mcp, Generic }
