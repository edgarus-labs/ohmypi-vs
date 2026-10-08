# oh-my-pi for Visual Studio

A native Visual Studio client for the [oh-my-pi (OMP)](https://github.com/can1357/oh-my-pi) coding-agent runtime, at parity with the oh-my-pi VS Code extension.

The extension is a frontend only. OMP remains the runtime and the source of truth for providers, credentials, models, sessions, agents and subagents, tools (including LSP, DAP and MCP), approval policy and session persistence. The extension starts `omp --mode rpc-ui` and talks to it over OMP's documented JSONL RPC protocol (`docs/rpc.md` in the OMP repository). It never parses terminal output.

## Requirements

- Visual Studio 2022 version 17.14 or newer (including Visual Studio 2026), Community, Professional or Enterprise, on x64 or Arm64. Developed and tested on Visual Studio 2026 (18.x).
- An installed and configured OMP (`omp`, tested with 18.6.0). Configure providers, logins and models with OMP itself, for example by running `omp` once in a terminal. The extension has no provider settings and stores no credentials.

## Installation

Double-click `OhMyPi.VisualStudio.vsix`, or install it from a command prompt:

```bat
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" OhMyPi.VisualStudio.vsix
```

Uninstall it from `Extensions > Manage Extensions > Installed`.

## Finding the OMP executable

When **Executable path** is empty, the extension looks for `omp.exe` and `omp.cmd` in the absolute `PATH` entries (relative entries are skipped), then in `~/.local/bin`, `~/.bun/bin` and `%LOCALAPPDATA%\omp`; an extensionless `omp` shim is not used. A configured path must be absolute (or start with `~`; `%VAR%` references are expanded) and name an existing `.exe`, `.com`, `.cmd` or `.bat` file. If no executable is found, the tool window shows an "OMP not found" banner and an info bar links to the options page; run `Tools > oh-my-pi > Restart` after fixing the path.

## Layout

OMP lives in the **oh-my-pi** tool window, docked as a tab next to Solution Explorer by default. Open it with `View > Other Windows > oh-my-pi` or `Tools > oh-my-pi > Open`; Visual Studio remembers where you dock it. The tool window toolbar has New Session, Session History, Abort (while busy) and Restart. The window contains:

- a thin header: state, session name (click to rename), elapsed time while working, context usage and cost;
- **Agents** and **Changes** sections, collapsed by default, with counts in their titles. Changes lists every file OMP's tools changed; clicking one opens Visual Studio's native diff of the content before OMP touched it (left, read-only) against the current file (right). Starting or switching sessions clears the list and closes its diff windows;
- the transcript;
- pending approvals and questions, queued messages and the todos strip;
- the composer.

## Features

- **Chat**: user and assistant messages stream in as they arrive; thinking is collapsed. Each tool call is a header (status icon, tool name in a small pill, what the call is for or its path, duration at the right) over one card: `IN` holds the command and the input as `name: value` lines (copying it yields the JSON), `OUT` the result, each label in a narrow column at the left. A result shows its first five lines with `show more`, which opens the call in a box of at most 500 px that scrolls inside, `collapse` closes it again and `Copy all` copies output past the display limit; a `read` shows only its header and `show more (N lines)` until opened. A failed call tints its whole card a soft red, a call started in the background shows its own icon, and shell output loses OMP's timing and exit-code trailer (the exit code is in the header). Code is colored by a built-in lexer (comments, strings, numbers, keywords, JSON and YAML keys, XML tags, shell commands and variables) chosen by the fence label, the file extension or the shape of the text; the line numbers OMP puts on file listings sit in a separate column that a selection never includes, a skipped range shows as an empty line, and search matches are marked. Markdown files and Markdown results render as Markdown. Diffs tint changed lines softly with a colored edge, and copying a JSON result copies the original text. The answer after tool calls is set off by a rule and lines up with the tool names, and a turn ends with a `Whole turn · duration · tokens · cost` line.
- **Composer**: multiline input (Enter sends, Shift+Enter starts a new line). The toolbar inside the box has `+`, the model, the reasoning effort, fast mode and Send. While the agent works, Send becomes Stop (or press Esc), which aborts the turn and cancels every running subagent; Enter steers the running turn (OMP hands the message over between tool calls) and Alt+Enter queues it as a follow-up that runs after the turn. To steer a running subagent, expand it in the **Agents** section and use its Steer button.
- **Attachments**: `+` adds the file in the active editor as a chip and is disabled while no document is open; switch documents and press it again to add several. `Add File to OMP Chat` is in the code editor and Solution Explorer context menus (multi-select works). Files are sent as OMP `@path` mentions, so OMP reads them itself. Ctrl+V attaches an image on the clipboard (a screenshot, an image copied in a browser) or copied files (image files as images, others as mentions); a pasted text longer than 10 lines or 1000 characters becomes a `Pasted text` chip instead of filling the input. Images can also be dropped onto the input: at most 8 per prompt, 10 MB each and 20 MB in total.
- **Models and effort**: the model list comes from OMP (`get_available_models`); nothing is hard-coded. The effort selector lists `off`, OMP's `auto` and the efforts OMP reports for the current model.
- **Sessions**: new session, session history (resume any session stored next to the current one) and rename. The last session of each solution or folder is resumed when the extension starts.
- **Prompt lifecycle**: a prompt is accepted, the agent works, yields (`prompt_result`) and the session settles (`session_settled`); an acknowledgement alone is never treated as completion.
- **Approvals and questions**: OMP `confirm`, `select`, `input`, `editor` and `ask` requests appear inline. If the tool window is hidden when one arrives, the extension shows it; nothing is ever answered automatically. Inputs marked secret are masked, and answers to `input` requests are redacted from the RPC trace.
- **Process management**: one OMP process per Visual Studio instance. Its working directory is the open solution's directory, else the open folder, else your user profile; opening or closing a solution or folder restarts OMP there. If OMP exits unexpectedly it is restarted with bounded retries and the current session is re-bound; if it still fails, an info bar offers Restart and Show Log. OMP normally runs in a kill-on-close Windows Job Object, so it ends with Visual Studio even after a crash; if the job object cannot be set up (the Output pane logs a warning), its process tree is ended process by process on shutdown only, and a Visual Studio crash can leave it running.
- **Agent control of Visual Studio**: the extension registers `vs_*` host tools with OMP, so agents can operate the IDE, not just chat in it. They run in-process on the UI thread through the Visual Studio APIs, and a failure comes back to the agent as a tool error. Every path argument must lie inside the workspace (the open solution or folder, resolved against OMP's working directory); other paths are refused. Approval of tool calls is OMP's policy, as for any tool.

| Area | Tools |
| --- | --- |
| Solution | `vs_solution` (solution, projects, configuration/platform, startup project, active document) |
| Documents | `vs_documents`, `vs_open_document`, `vs_read_document` (editor buffer, so unsaved edits are seen), `vs_replace_lines` (edits the buffer, stays unsaved), `vs_save`, `vs_close_document`, `vs_selection` |
| Build | `vs_build` (build, rebuild or clean the solution or one project; waits and returns the result and errors), `vs_errors` (Error List), `vs_output` (Output window panes) |
| Projects | `vs_add_file_to_project`, `vs_remove_file_from_project` |
| Debugger | `vs_debug` (start, start without debugging, stop, break, continue, step into/over/out, state), `vs_breakpoints` (list, add with optional condition, remove), `vs_debug_inspect` (evaluate, call stack, locals) |
| Anything else | `vs_find_commands` and `vs_execute_command` run any Visual Studio command (`Edit.FormatDocument`, `TestExplorer.RunAllTests`, …) |

## Commands

The commands are under `Tools > oh-my-pi`, except the two `Add File to OMP Chat` commands, which are only in the code editor and Solution Explorer context menus, and `View.OhMyPi`, which is `View > Other Windows > oh-my-pi`. All of them can be bound in `Tools > Options > Environment > Keyboard` (search for `OMP.` or `View.OhMyPi`). The extension adds no keybindings.

| Command | Canonical name |
| --- | --- |
| Open | `OMP.Open` |
| Open (`View > Other Windows > oh-my-pi`) | `View.OhMyPi` |
| New Session | `OMP.NewSession` |
| Resume Session (Session History) | `OMP.ResumeSession` |
| Rename Session | `OMP.RenameSession` |
| Show Agents | `OMP.ShowAgents` |
| Send Prompt (reveal and focus the input) | `OMP.SendPrompt` |
| Select Model | `OMP.SelectModel` |
| Select Thinking Level | `OMP.SelectThinkingLevel` |
| Toggle Fast Mode | `OMP.ToggleFastMode` |
| Abort | `OMP.Abort` |
| Restart | `OMP.Restart` |
| Show Log (Output window, `oh-my-pi` pane) | `OMP.ShowLog` |
| Open Settings | `OMP.OpenSettings` |
| Add File to OMP Chat (code editor context menu) | `OMP.AddFileToChat` |
| Add File to OMP Chat (Solution Explorer context menu) | `OMP.AddSelectedFilesToChat` |

With **Startup** set to `Manual`, OMP starts on the first command that needs it or on the first prompt.

## Settings

`Tools > Options > oh-my-pi > General` covers the frontend only:

| Setting | Default | Purpose |
| --- | --- | --- |
| Executable path | empty | Absolute path (or `~`-relative; `%VAR%` is expanded) to an `omp` `.exe`, `.com`, `.cmd` or `.bat` file; empty means search as described above. |
| Extra arguments | empty | Extra arguments for `omp --mode rpc-ui`, split like a Windows command line, for example `--profile work` or `--approval-mode always-ask`. |
| Startup | `ResumeLast` | `ResumeLast`, `NewSession` or `Manual`. |
| Auto restart | `true` | Restart OMP after an unexpected exit. |
| Log level | `Info` | Verbosity of the `oh-my-pi` Output window pane (`Error`, `Warn`, `Info`, `Debug`). |
| Trace RPC | `false` | Log every RPC frame to the Output pane, with each string truncated to 200 characters and each frame to 2000. Answers to OMP `input` requests are redacted; prompts, other answers, tool arguments and tool results (including file contents) are not. |

Changing the executable path, extra arguments or auto restart offers to restart OMP. Providers, models, MCP servers, agents, approval policy and session storage are configured in OMP (`~/.omp/agent/config.yml`, `omp` commands), not here.

## Development

Layout:

| Path | Contents |
| --- | --- |
| `src/Omp.Core` | netstandard2.0: OMP process, JSONL RPC client, session/agent state, change model, prompt formatting. No Visual Studio dependency. |
| `src/OhMyPi.VisualStudio.UI` | net48 WPF: the chat tool window content (`OmpChatControl`) and the `IOmpHost` interface it needs from the shell. |
| `src/OhMyPi.VisualStudio` | net48 VSSDK package and VSIX: `AsyncPackage`, tool window, commands (`OmpPackage.vsct`), options page, Output window logging, active document tracking, change tracking and diffs. `Logic/` holds its Visual Studio-independent parts. |
| `tests/*` | xunit tests for each project. |

Prerequisites:

- Visual Studio 2026 with the **Visual Studio extension development** workload (VSSDK).
- The .NET 10 SDK. `global.json` pins 10.0.401 and accepts later feature bands; `Directory.Build.props` pins C# 13.
- Node.js on `PATH`: the `Omp.Core` tests run a fake OMP (`tests/Omp.Core.Tests/Fixtures/fake-omp.mjs`) with `node`.
- Optionally an installed, logged-in OMP: the real-OMP test is skipped unless `OMP_REAL=1` is set, because it sends a real (small, billed) model request.

Build the solution and the VSIX with Visual Studio's MSBuild (the VSSDK build tasks need it):

```bat
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OhMyPi.sln /restore /p:Configuration=Release
```

The VSIX is written to `src\OhMyPi.VisualStudio\bin\Release\net48\OhMyPi.VisualStudio.vsix`. Its version comes from `<Version>` in `src\OhMyPi.VisualStudio\OhMyPi.VisualStudio.csproj`, the only place it is set. Run the tests with `dotnet test` per test project (the host tests compile `src\OhMyPi.VisualStudio\Logic` directly, so they don't need the VSSDK):

```bat
dotnet test tests\Omp.Core.Tests
dotnet test tests\OhMyPi.VisualStudio.UI.Tests
dotnet test tests\OhMyPi.VisualStudio.Tests
```

Branching: work happens on feature branches, which reach `develop` through pull requests; a release is a `vMAJOR.MINOR.PATCH` tag on `develop`. GitHub Actions pipelines (all build on a `windows-2025-vs2026` runner through the shared `.github/workflows/build.yml`, which builds the solution and runs the three test projects):

- **CI** (`ci.yml`): a pull request into `develop` when it is opened and on every push to it; build and tests only. Pushing a branch without a pull request runs nothing.
- **CD** (`cd.yml`): every push to `develop` (a merged pull request); build, tests, and the VSIX uploaded as a build artifact named after the commit.
- **Release** (`release.yml`): pushing a `vMAJOR.MINOR.PATCH` tag, or running it by hand (Actions > Release > Run workflow) with such a tag. It builds and tests with that version (`-p:Version=`), then creates a **draft** GitHub release for the tag with `OhMyPi.VisualStudio-<version>.vsix` attached and generated notes. Re-running it for a tag whose release is still a draft replaces the VSIX; a published release is never changed. Publish the draft on GitHub to make the release (and, for a manually run release, its tag) public.

To try a Release build, install it into the experimental instance and start it. A rebuilt VSIX keeps the same version until `<Version>` changes, so uninstall the previous one first:

```bat
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" /rootSuffix:Exp /quiet /uninstall:c0c67373-6c43-44fb-92d0-9919ffff0791
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" /rootSuffix:Exp /quiet src\OhMyPi.VisualStudio\bin\Release\net48\OhMyPi.VisualStudio.vsix
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe" /rootSuffix Exp
```

Or open `OhMyPi.sln` in Visual Studio, select the Debug configuration and debug `OhMyPi.VisualStudio` (F5): the Debug build deploys the extension to the experimental instance and starts `devenv /rootsuffix Exp`. Release builds never deploy.

## Architecture decision: VSSDK rather than VisualStudio.Extensibility

**Decision.** The extension is a classic in-process VSSDK `AsyncPackage` with a WPF `ToolWindowPane`, targeting .NET Framework 4.8, built with `Microsoft.VisualStudio.SDK` 17.14 and `Microsoft.VSSDK.BuildTools`. It does not use the out-of-process VisualStudio.Extensibility model or the Community Toolkit.

**Why.**

- The chat surface needs real WPF: a streaming transcript that updates items in place, a composer with Enter/Shift+Enter handling and an in-box toolbar, popups for the model and effort pickers, inline approval cards. VisualStudio.Extensibility's Remote UI allows only XAML data templates bound to a remote data context, without code-behind or custom controls.
- Change tracking needs Visual Studio's native diff window (`IVsDifferenceService`), and file attachments need the shell's active document frame and Solution Explorer selection. VisualStudio.Extensibility has no diff API.
- VSSDK also provides theme brushes (`EnvironmentColors`), DialogPage options, Output window panes and info bars directly. Because it compiles against the 17.14 SDK, the extension requires Visual Studio 2022 17.14 or newer (the manifest's floor is `[17.14,)`).
- The Community Toolkit would only add a dependency on top of APIs the extension uses directly.

**Consequences.** The package loads in-process, so all OMP I/O is asynchronous and never blocks the UI thread (`JoinableTaskFactory`, no `.Result`/`.Wait()`); the package loads in the background and only when a command runs or the tool window is restored. Everything that doesn't need Visual Studio lives in `Omp.Core` (netstandard2.0) and `src/OhMyPi.VisualStudio/Logic`, where plain unit tests cover it.
