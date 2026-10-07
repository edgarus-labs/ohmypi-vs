using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio
{
    /// <summary>Tools &gt; Options &gt; oh-my-pi &gt; General. Frontend settings only; providers, models and approval policy live in OMP.</summary>
    [ComVisible(true)]
    [Guid(PackageGuids.OptionsPageString)]
    public sealed class OmpOptionsPage : DialogPage
    {
        private const string ProcessCategory = "OMP process";
        private const string LogCategory = "Log";

        [Category(ProcessCategory)]
        [DisplayName("Executable path")]
        [Description("Path to the OMP executable: absolute after ~ and %VARIABLE% expansion, naming a .exe, .com, .cmd or .bat file. Empty: search the absolute PATH entries for omp.exe and omp.cmd, then ~/.local/bin, ~/.bun/bin and %LOCALAPPDATA%\\omp.")]
        public string ExecutablePath { get; set; } = "";

        [Category(ProcessCategory)]
        [DisplayName("Extra arguments")]
        [Description("Extra command-line arguments passed to `omp --mode rpc-ui`, split like a Windows command line (for example: --profile work). Model, provider and approval configuration stay in OMP.")]
        public string ExtraArgs { get; set; } = "";

        [Category(ProcessCategory)]
        [DisplayName("Startup")]
        [Description("When the extension loads: resume the last session of this solution or folder (ResumeLast), start a fresh one (NewSession), or wait until a command is run (Manual).")]
        [DefaultValue(StartupMode.ResumeLast)]
        public StartupMode Startup { get; set; } = StartupMode.ResumeLast;

        [Category(ProcessCategory)]
        [DisplayName("Auto restart")]
        [Description("Restart the OMP process after an unexpected exit (bounded retries).")]
        [DefaultValue(true)]
        public bool AutoRestart { get; set; } = true;

        [Category(LogCategory)]
        [DisplayName("Log level")]
        [Description("Verbosity of the oh-my-pi Output window pane.")]
        [DefaultValue(OmpLogLevel.Info)]
        public OmpLogLevel LogLevel { get; set; } = OmpLogLevel.Info;

        [Category(LogCategory)]
        [DisplayName("Trace RPC")]
        [Description("Log every RPC frame to the oh-my-pi Output window pane. Strings are cut to 200 characters and answers to OMP's text input requests are redacted, but prompts, other answers, file contents and tool output still appear: do not share the log publicly.")]
        [DefaultValue(false)]
        public bool TraceRpc { get; set; }

        /// <summary>Raised after the user applies the page.</summary>
        public event EventHandler? Applied;

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply) Applied?.Invoke(this, EventArgs.Empty);
        }
    }
}
