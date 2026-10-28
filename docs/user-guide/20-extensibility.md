# 20. Extensibility (CLI, LSP, AI assistants, plugins, hooks)

> [← Back to the User Guide home](README.md)

For power users, ThIDE's understanding of Therion is available *outside* the GUI and can be extended.
None of this is needed for everyday use — but it's there when you want to automate or integrate.

## The command-line tool (`therion-cli`)

A **headless** tool that runs the same parsing/analysis as the app — perfect for scripts, CI, or a
quick check without opening the GUI:

```sh
therion-cli validate path/to/project.thconfig   # report diagnostics
therion-cli lint     path/to/file.th             # style / convention lints
therion-cli format   path/to/file.th --write     # re-indent in place
therion-cli stats    path/to/project.thconfig    # length/depth/etc.
therion-cli deps     path/to/project.thconfig --dot   # include graph (Graphviz)
therion-cli gis      path/to/project.thconfig --format kml --out entrances.kml
therion-cli new      --list                      # project templates (built-in and imported)
therion-cli new      single-cave --out ~/caves/pit --var project=pit
```

Also `dump-ast` and `list-stations`. Full command list and examples:
[docs/usage.md → Command-line tools](../usage.md#command-line-tools).

## The language server (`therion-lsp`)

An **editor-agnostic Language Server** that provides Therion diagnostics over stdio — point any
LSP-capable editor (VS Code, Neovim, …) at it to get ThIDE's checks in your editor of choice. Setup
and client configuration: **[docs/lsp.md](../lsp.md)**.

## AI assistants (MCP)

> **Experimental — work in progress.** The MCP integration and the Assistant panel are young and
> still changing. Expect rough edges, and settings or behaviour that move between releases.

ThIDE can let a **local AI assistant** answer questions about your project — *"what's wrong with this
cave?"*, *"is it all one connected piece?"*, *"where is station 12 declared?"* — without you pasting
files into a chat window. It does this by speaking the **Model Context Protocol (MCP)**, the same way
tools like Claude or a local model in **LM Studio** talk to their tools.

Three ways to use it:

- **The built-in Assistant panel (no setup beyond a local model).** **View → Assistant** opens a
  chat inside ThIDE that talks to a local model served by **LM Studio** (or any OpenAI-compatible
  endpoint — set it under **Preferences → MCP**). It answers from the live project, and every
  change it proposes stops on an **Allow / Deny** card first — nothing is written until you allow it.
- **Against files on disk (no ThIDE needed).** A small program, `therion-mcp`, runs ThIDE's engines
  headlessly. Your AI host (LM Studio, Claude Code, …) launches it and can then ask about a project
  folder. It reads your files; it can also format, scaffold, import/export, and run Therion — or, if
  you prefer, be limited to **read-only** with a single switch.
- **Against the app you have open.** Turn on **Preferences → MCP → "Enable the in-app AI tools
  server"** and the *running* ThIDE offers the same abilities plus live ones: the assistant can see the
  file you're editing (including unsaved changes), open a file, focus a panel, or reveal a station in
  the 3D view. Whether it may *touch* the window — not just read it — is governed by the **"Follow the
  agent"** checkbox next to that setting; turn it off to keep the assistant's hands off your layout
  while still letting it answer questions.

**Is it safe?** The in-app server listens only on your own machine (loopback), behind a random
password, and is **off by default**. An assistant can never reach outside your project folder, and an
edit to a file you're currently editing (with unsaved changes) is refused rather than allowed to clobber
your work. If you're cautious, start it read-only.

Setting up a specific AI host (LM Studio, Claude Code) is a few lines of configuration — see the
step-by-step in **[docs/mcp-host-setup.md](../mcp-host-setup.md)**.

## Semantic-rule plugins

You can add your **own validation rules** without rebuilding ThIDE:

1. Write a small .NET type implementing **`ISemanticRule`**.
2. Drop the compiled `.dll` in `%AppData%/ThIDE/plugins`.
3. Enable **Load plugins** in [Settings → Extensions](19-settings-and-preferences.md#extensions) and
   restart.

Your rule then contributes diagnostics like the built-in ones. The API and examples:
**[docs/plugins.md](../plugins.md)**. Rules can also be configured via a `rules.json`.

## Script hooks

Run an external command at key moments:

1. Enable **script hooks** in [Settings → Extensions](19-settings-and-preferences.md#extensions).
2. The three single commands (**on open**, **on save**, **on build**) work as before — started and
   forgotten. `{file}` is the file path (for build, the thconfig being compiled), now quoted so a
   path with spaces stays one argument; *on save* now also follows **Save All** and auto-save.
3. **More hooks** adds as many as you like, each for one event — *WorkspaceOpened*, *FileOpened*,
   *FileSaved*, *BuildStarted*, *BuildFinished*, *BuildFailed* — with a timeout. Their output goes to
   the **Automation** panel (*View → Automation*) unless marked *detached*.

Commands may use `{file}`, `${file}`, `${fileDirname}`, `${fileBasename}`, `${workspaceRoot}`,
`${activeThconfig}`, `${entry}` and `${env:NAME}`. Every hook also gets environment variables:
`THIDE_EVENT`, `THIDE_FILE`, `THIDE_WORKSPACE`, `THIDE_THCONFIG`, `THIDE_ENTRY`, and after a build
`THIDE_BUILD_EXIT`, `THIDE_BUILD_ERRORS`, `THIDE_BUILD_WARNINGS`, `THIDE_BUILD_LOG`, `THIDE_OUTPUTS`
(separated like `PATH`) and `THIDE_ELAPSED_MS` — so *BuildFinished* can publish the PDF, *BuildFailed*
can notify you. Use them for backups, post-processing, notifications or a version-control commit.

### Project hooks and tasks (`.thide/`)

A project can carry its own automation in its `.thide/` folder (committed with the cave):

```jsonc
// .thide/hooks.json
{ "version": 1, "hooks": [
  { "event": "BuildFinished", "command": "rsync -a output/ server:/caves/${env:USER}/", "timeoutSeconds": 120 },
  { "event": "FileSaved", "task": "lint" }
] }

// .thide/tasks.json
{ "version": 1, "tasks": [
  { "id": "lint", "title": "Check names", "command": "python3 tools/check.py ${file}", "problemMatcher": "file-line" },
  { "id": "all", "title": "Build every cave", "type": "builtin", "builtin": "batch-build", "entries": ["caves/a.thconfig", "caves/b.thconfig"] },
  { "id": "therion-all", "title": "Therion on the master", "command": "therion", "args": ["all.thconfig"], "problemMatcher": "therion" }
] }
```

Tasks run from **Tools → Tasks** or the command palette (*Task: …*). A task's `problemMatcher`
(`therion`, or `file-line` for `path:line[:col]: message`) turns its output into entries in the
Diagnostics panel. A task can be `shell` (the default), `builtin` (`build`, `batch-build`) or
`macro`.

**Trust.** Commands from a project only run once you trust its folder: the first time something
would run, ThIDE names the folder and asks. Your answer is remembered; **Settings → Extensions →
Trusted folders** lists them, and you can remove one there. Reading the project's snippets and
templates never asks. The switch *Read hooks, tasks and macros from the project's .thide folder*
turns all of it off.

## Where things live

- Plugins: `%AppData%/ThIDE/plugins/*.dll`
- Rule config: `rules.json` (app data)
- Snippets: `snippets.json`; file templates: `templates/files/*.json` — in app data for yourself, and
  in the project's `.thide/` folder (or its private app-data twin, per **Preferences ▸ General**) for
  the project. See [The editor → Snippets and new files](07-editing.md#snippets-and-new-files).
- The CLI and LSP are **separate executables** shipped alongside the app, not the GUI itself.

See also the developer-facing [docs/architecture.md](../architecture.md) if you want to reuse the
underlying libraries in your own .NET program.

---

Next: [Keyboard shortcuts →](21-keyboard-shortcuts.md)
