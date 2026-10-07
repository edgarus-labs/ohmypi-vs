using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Omp.Core.Changes;

namespace OhMyPi.VisualStudio.UI
{
    /// <summary>What the chat control needs from the Visual Studio shell that hosts it.</summary>
    public interface IOmpHost
    {
        /// <summary>Full path of the document in the active text editor, or null when no file is open; read on the UI thread.</summary>
        string? ActiveDocumentPath { get; }

        /// <summary>Raised when <see cref="ActiveDocumentPath"/> may have changed; may fire on any thread.</summary>
        event EventHandler ActiveDocumentChanged;

        /// <summary>Files changed by OMP tools; read on the UI thread after <see cref="ChangesChanged"/>.</summary>
        IReadOnlyList<TrackedChange> Changes { get; }

        /// <summary>Raised when <see cref="Changes"/> changed; may fire on any thread.</summary>
        event EventHandler ChangesChanged;

        /// <summary>
        /// Opens the native VS diff of what OMP changed in <paramref name="path"/>: the tracked baseline when one exists,
        /// otherwise <paramref name="recordedBefore"/> (the tool's own record of the old text). Throws when neither exists.
        /// </summary>
        Task OpenDiffAsync(string path, string? recordedBefore = null);

        Task OpenFileAsync(string path, int? line = null);

        /// <summary>Shows the "oh-my-pi" Output window pane.</summary>
        void ShowLog();

        /// <summary>Opens the Tools &gt; Options page.</summary>
        void OpenSettings();

        /// <summary>Starts OMP when it is not running, otherwise restarts it.</summary>
        Task RestartAsync();

        /// <summary>Completes when OMP is ready, joining a start already in progress or starting it; throws when it cannot start.</summary>
        Task EnsureServiceAsync();

        /// <summary>Writes a failure with its exception to the oh-my-pi Output pane.</summary>
        void LogError(string message, Exception error);

        /// <summary>Non-null when no OMP service could be built; the control then shows why instead of the chat.</summary>
        OmpUnavailable? Unavailable { get; }

        /// <summary>The user's favorite and recently picked models, kept across Visual Studio sessions.</summary>
        IModelPreferences ModelPreferences { get; }
    }

    /// <summary>Why no OMP service could be built.</summary>
    public sealed class OmpUnavailable
    {
        public OmpUnavailable(string message, bool executableNotFound)
        {
            Message = message;
            ExecutableNotFound = executableNotFound;
        }

        public string Message { get; }

        /// <summary>True when the OMP executable could not be located; false when building the service failed otherwise.</summary>
        public bool ExecutableNotFound { get; }
    }
}
