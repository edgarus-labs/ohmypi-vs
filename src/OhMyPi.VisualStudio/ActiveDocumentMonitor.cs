using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OhMyPi.VisualStudio.Logic;
using System;
using IServiceProvider = System.IServiceProvider;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Tracks the document in the active (or last active) editor frame through shell selection events, so the chat's
/// "+" button can add it and is disabled when no document is open.
/// </summary>
internal sealed class ActiveDocumentMonitor : IVsSelectionEvents, IDisposable
{
    private readonly IVsMonitorSelection? _selection;
    private uint _cookie;

    /// <summary>
    /// Initializes a new instance of the ActiveDocumentMonitor class and subscribes to selection events using the provided service provider.
    /// </summary>
    /// <param name="services">The services.</param>
    public ActiveDocumentMonitor(IServiceProvider services)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _selection = services.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        if (_selection is not null)
        {
            ErrorHandler.ThrowOnFailure(_selection.AdviseSelectionEvents(this, out _cookie));
        }
    }

    /// <summary>Raised on the UI thread when the active document frame changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Full path of the document in the active editor frame; null when no file document is open.</summary>
    public string? ActivePath
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_selection is null)
            {
                return null;
            }

            if (ErrorHandler.Failed(_selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var value)))
            {
                return null;
            }

            if (!(value is IVsWindowFrame frame))
            {
                return null;
            }

            if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out var moniker)))
            {
                return null;
            }

            return moniker is string path && LocalPaths.Exists(path, allowDirectories: false) ? path : null;
        }
    }

    /// <summary>
    /// Handles the element value change event and triggers the Changed event if the modified element is the document frame.
    /// </summary>
    /// <param name="elementid">The unique identifier of the element.</param>
    /// <param name="varValueOld">The var value old.</param>
    /// <param name="varValueNew">The var value new.</param>
    /// <returns>The int result.</returns>
    public int OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
    {
        if (elementid == (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return VSConstants.S_OK;
    }

    /// <summary>
    /// Handles the event when the current selection changes within the Visual Studio environment, providing the previous and new selection states.
    /// </summary>
    /// <param name="pHierOld">The p hier old.</param>
    /// <param name="itemidOld">The itemid old.</param>
    /// <param name="pMISOld">The p misold.</param>
    /// <param name="pSCOld">The p scold.</param>
    /// <param name="pHierNew">The p hier new.</param>
    /// <param name="itemidNew">The itemid new.</param>
    /// <param name="pMISNew">The p misnew.</param>
    /// <param name="pSCNew">The p scnew.</param>
    /// <returns>The int result.</returns>
    public int OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld,
        IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew) => VSConstants.S_OK;

    /// <summary>
    /// Handles the notification that the command UI context has changed for the specified cookie and updates the active state.
    /// </summary>
    /// <param name="dwCmdUICookie">The dw cmd uicookie.</param>
    /// <param name="fActive">The f active.</param>
    /// <returns>The int result.</returns>
    public int OnCmdUIContextChanged(uint dwCmdUICookie, int fActive) => VSConstants.S_OK;

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_selection is not null && _cookie != 0)
        {
            _selection.UnadviseSelectionEvents(_cookie);
        }

        _cookie = 0;
    }
}
