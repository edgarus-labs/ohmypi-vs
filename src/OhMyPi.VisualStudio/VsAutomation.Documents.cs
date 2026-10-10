using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic.Automation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTE2 = EnvDTE80.DTE2;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Provides a set of automation utilities for managing Visual Studio documents, including operations for reading, modifying, and saving text buffers.
/// </summary>
internal sealed partial class VsAutomation
{
    /// <summary>
    /// Asynchronously retrieves a list of information for all currently open documents, including their file paths, modification status, and activity state.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public async Task<IReadOnlyList<DocumentInfo>> ListDocumentsAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var active = ActiveDocumentPath(dte);
        var documents = new List<DocumentInfo>();
        foreach (Document document in dte.Documents)
        {
            try
            {
                var path = document.FullName;
                if (!IsRootedPath(path))
                {
                    continue;
                }

                documents.Add(new DocumentInfo
                {
                    Path = path,
                    IsDirty = !document.Saved,
                    IsActive = SamePath(path, active),
                    IsReadOnly = document.ReadOnly,
                });
            }
            catch (COMException)
            {
            }
        }

        return documents;
    }

    /// <summary>
    /// Asynchronously opens a document at the specified path and optionally positions the editor caret at the provided line and column.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="column">The column.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task OpenDocumentAsync(string path, int? line, int? column, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var file = ExistingFile(path);
        try
        {
            VsShellUtilities.OpenDocument(_package, file, Guid.Empty, out _, out _, out var frame, out var view);
            if (frame is not null)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
            }

            if (line is null)
            {
                return;
            }

            if (view is null)
            {
                throw new InvalidOperationException($"{file} was opened, but its editor has no caret to move.");
            }

            ErrorHandler.ThrowOnFailure(view.GetBuffer(out var buffer));
            ErrorHandler.ThrowOnFailure(buffer.GetLineCount(out var lineCount));
            if (line < 1 || line > lineCount)
            {
                throw new InvalidOperationException($"{file} has {lineCount} lines; line {line} does not exist.");
            }

            var lineIndex = line.Value - 1;
            ErrorHandler.ThrowOnFailure(buffer.GetLengthOfLine(lineIndex, out var length));
            var columnIndex = Math.Min(Math.Max((column ?? 1) - 1, 0), length);
            ErrorHandler.ThrowOnFailure(view.SetCaretPos(lineIndex, columnIndex));
            ErrorHandler.ThrowOnFailure(view.CenterLines(lineIndex, 1));
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not open {file}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Asynchronously reads a specified range of text from a document file, utilizing an existing text buffer if available or loading the file directly from disk.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="startLine">The start line.</param>
    /// <param name="endLine">The end line.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the document text.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<DocumentText> ReadDocumentAsync(string path, int? startLine, int? endLine, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var file = FullPath(path);
        var buffer = TryGetTextBuffer(file, open: false);
        if (buffer is not null)
        {
            return ReadFromBuffer(file, buffer, startLine, endLine);
        }

        await TaskScheduler.Default;
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"File not found: {file}");
        }

        var lines = LineEdits.SplitLines(File.ReadAllText(file));
        LineEdits.ResolveRange(lines.Count, startLine, endLine, out var first, out var last);

        return new DocumentText
        {
            Path = file,
            StartLine = first,
            EndLine = last,
            TotalLines = lines.Count,
            Text = string.Join("\n", lines.Skip(first - 1).Take(last - first + 1)),
            FromEditor = false,
        };
    }

    /// <summary>
    /// Extracts a specified range of text from a Visual Studio text buffer and returns it as a DocumentText object.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="startLine">The start line.</param>
    /// <param name="endLine">The end line.</param>
    /// <returns>The document text result.</returns>
    private static DocumentText ReadFromBuffer(string file, IVsTextLines buffer, int? startLine, int? endLine)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(buffer.GetLineCount(out var total));
        LineEdits.ResolveRange(total, startLine, endLine, out var first, out var last);
        ErrorHandler.ThrowOnFailure(buffer.GetLengthOfLine(last - 1, out var lastLength));
        ErrorHandler.ThrowOnFailure(buffer.GetLineText(first - 1, 0, last - 1, lastLength, out var text));

        return new DocumentText
        {
            Path = file,
            StartLine = first,
            EndLine = last,
            TotalLines = total,
            Text = LineEdits.NormalizeLineBreaks(text ?? ""),
            FromEditor = true,
        };
    }

    /// <summary>
    /// Asynchronously replaces a specified range of lines in a text document at the given path with the provided text.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="startLine">The start line.</param>
    /// <param name="endLine">The end line.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task ReplaceLinesAsync(string path, int startLine, int endLine, string text, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var file = ExistingFile(path);
        try
        {
            var buffer = TryGetTextBuffer(file, open: true)
                ?? throw new InvalidOperationException($"{file} is not a text document; it cannot be edited line by line.");
            ErrorHandler.ThrowOnFailure(buffer.GetStateFlags(out var flags));
            if ((flags & ((uint)BUFFERSTATEFLAGS.BSF_USER_READONLY | (uint)BUFFERSTATEFLAGS.BSF_FILESYS_READONLY)) != 0)
            {
                throw new InvalidOperationException($"{file} is read-only in the editor.");
            }

            ErrorHandler.ThrowOnFailure(buffer.GetLineCount(out var total));
            var lineBreak = "\r\n";
            if (total > 1)
            {
                ErrorHandler.ThrowOnFailure(buffer.GetLineText(0, 0, 1, 0, out var firstLine));
                lineBreak = LineEdits.DetectLineBreak(firstLine);
            }

            var edit = LineEdits.Plan(total, line =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ErrorHandler.ThrowOnFailure(buffer.GetLengthOfLine(line, out var length));

                return length;
            }, startLine, endLine, text, lineBreak);

            var characters = Marshal.StringToCoTaskMemUni(edit.Text);
            try
            {
                var hr = buffer.ReplaceLines(edit.StartLine, edit.StartIndex, edit.EndLine, edit.EndIndex, characters, edit.Text.Length, new TextSpan[1]);
                if (ErrorHandler.Failed(hr))
                {
                    throw new InvalidOperationException($"The editor rejected the edit of {file} (HRESULT 0x{hr:X8}); the buffer may be read-only here.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(characters);
            }
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not edit {file}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Asynchronously saves a specific document at the provided path or all modified rooted documents in the editor, returning a list of the paths that were successfully saved.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<IReadOnlyList<string>> SaveDocumentsAsync(string? path, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var saved = new List<string>();
        if (path is not null)
        {
            var document = FindDocument(dte, FullPath(path)) ?? throw new InvalidOperationException($"{path} is not open in the editor.");
            if (SaveIfDirty(document, out var documentPath))
            {
                saved.Add(documentPath);
            }

            return saved;
        }
        var failures = new List<string>();
        foreach (Document document in dte.Documents)
        {
            if (!IsRootedPath(document.FullName))
            {
                continue;
            }

            try
            {
                if (SaveIfDirty(document, out var documentPath))
                {
                    saved.Add(documentPath);
                }
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(ex.Message);
            }
        }
        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"{string.Join(" ", failures)} Saved: {(saved.Count == 0 ? "none" : string.Join(", ", saved))}.");
        }

        return saved;
    }

    /// <summary>
    /// Saves the specified document if it has unsaved changes and outputs the file path.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="path">The path.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static bool SaveIfDirty(Document document, out string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        path = document.FullName;
        if (document.Saved)
        {
            return false;
        }

        try
        {
            document.Save();
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not save {path}: {ex.Message}", ex);
        }

        return true;
    }

    /// <summary>
    /// Asynchronously closes the specified document in the Visual Studio editor, optionally saving changes before closure.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="save">The save.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task CloseDocumentAsync(string path, bool save, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var document = FindDocument(dte, FullPath(path)) ?? throw new InvalidOperationException($"{path} is not open in the editor.");
        try
        {
            document.Close(save ? vsSaveChanges.vsSaveChangesYes : vsSaveChanges.vsSaveChangesNo);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not close {path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Asynchronously retrieves the current text selection information, including coordinates and content, from the active document in the IDE.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the selection info?.</returns>
    public async Task<SelectionInfo?> GetSelectionAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        Document? document;
        try { document = dte.ActiveDocument; }
        catch (COMException) { return null; }
        if (document is null || !(document.Selection is TextSelection selection))
        {
            return null;
        }

        return new SelectionInfo
        {
            Path = document.FullName,
            StartLine = selection.TopPoint.Line,
            StartColumn = selection.TopPoint.LineCharOffset,
            EndLine = selection.BottomPoint.Line,
            EndColumn = selection.BottomPoint.LineCharOffset,
            Text = selection.Text ?? "",
        };
    }

    /// <summary>
    /// Searches the collection of open documents in the DTE2 instance to find and return the document that matches the specified file path.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <param name="path">The path.</param>
    /// <returns>The document? result.</returns>
    private static Document? FindDocument(DTE2 dte, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (Document document in dte.Documents)
        {
            try
            {
                if (SamePath(document.FullName, path))
                {
                    return document;
                }
            }
            catch (COMException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Validates the existence of a file at the specified path and returns its full path, throwing an &lt;see cref=&quot;InvalidOperationException&quot;/&gt; if the file is not found.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static string ExistingFile(string path)
    {
        var file = FullPath(path);
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"File not found: {file}");
        }

        return file;
    }

    /// <summary>The editor buffer of <paramref name="path"/>; null when the document is not open (and not <paramref name="open"/>ed) or has no text buffer.</summary>
    private IVsTextLines? TryGetTextBuffer(string path, bool open)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!VsShellUtilities.IsDocumentOpen(_package, path, Guid.Empty, out _, out _, out IVsWindowFrame? frame))
        {
            if (!open)
            {
                return null;
            }

            VsShellUtilities.OpenDocument(_package, path, Guid.Empty, out _, out _, out frame, out _);
        }
        if (frame is null || ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out var data)))
        {
            return null;
        }

        if (data is IVsTextLines lines)
        {
            return lines;
        }

        if (data is IVsTextBufferProvider provider && ErrorHandler.Succeeded(provider.GetTextBuffer(out var providedLines)))
        {
            return providedLines;
        }

        return null;
    }
}
