using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.UI;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio;

/// <summary>The <b>oh-my-pi</b> tool window: hosts the chat control of the current OMP service generation.</summary>
[Guid(PackageGuids.ToolWindowString)]
public sealed class OmpToolWindow : ToolWindowPane
{
    private readonly OmpRuntime _runtime;
    private readonly ContentControl _host = new ContentControl { Focusable = false };
    private Generation? _shown;

    /// <param name="state">The package's runtime, from <see cref="OmpPackage"/>'s tool window initialization.</param>
    public OmpToolWindow(object state)
        : base(null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _runtime = (OmpRuntime)state;
        Caption = "oh-my-pi";
        BitmapImageMoniker = new Microsoft.VisualStudio.Imaging.Interop.ImageMoniker { Guid = PackageGuids.Images, Id = 1 };
        ToolBar = new CommandID(PackageGuids.CommandSet, PackageIds.ToolWindowToolbar);
        Content = _host;
        ChatColors.Attach(_host.Resources);
        _runtime.GenerationChanged += OnGenerationChanged;
        ShowCurrent();
    }

    /// <summary>The chat control of the current generation, once one exists.</summary>
    internal OmpChatControl? Control { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtime.GenerationChanged -= OnGenerationChanged;
            Control?.Dispose();
            Control = null;
            _host.Content = null;
        }
        base.Dispose(disposing);
    }

    private void OnGenerationChanged(object sender, EventArgs e) => Background.Run(ThreadHelper.JoinableTaskFactory, _runtime.Logger, "Showing the new OMP chat", async () =>
                                                                         {
                                                                             await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                                                             ShowCurrent();
                                                                         });

    /// <summary>Shows the chat of the current generation, replacing and disposing the previous one; null before the first generation exists.</summary>
    internal OmpChatControl? ShowCurrent()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var generation = _runtime.Current;
        if (generation is null || generation == _shown)
        {
            return Control;
        }

        var previous = Control;
        var control = new OmpChatControl();
        control.Initialize(generation.Service, generation.Host);
        _host.Content = control;
        Control = control;
        _shown = generation;
        previous?.Dispose();

        return control;
    }
}
