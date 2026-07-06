# 20. Extensibility (CLI, LSP, plugins, hooks)

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
```

Also `dump-ast` and `list-stations`. Full command list and examples:
[docs/usage.md → Command-line tools](../usage.md#command-line-tools).

## The language server (`therion-lsp`)

An **editor-agnostic Language Server** that provides Therion diagnostics over stdio — point any
LSP-capable editor (VS Code, Neovim, …) at it to get ThIDE's checks in your editor of choice. Setup
and client configuration: **[docs/lsp.md](../lsp.md)**.

## Semantic-rule plugins

You can add your **own validation rules** without rebuilding ThIDE:

1. Write a small .NET type implementing **`ISemanticRule`**.
2. Drop the compiled `.dll` in `%AppData%/ThIDE/plugins`.
3. Enable **Load plugins** in [Settings → Extensions](19-settings-and-preferences.md#extensions) and
   restart.

Your rule then contributes diagnostics like the built-in ones. The API and examples:
**[docs/plugins.md](../plugins.md)**. Rules can also be configured via a `rules.json`.

## Script hooks

Run an external command at key moments — **on open**, **on save**, or **on build**:

1. Enable **script hooks** in [Settings → Extensions](19-settings-and-preferences.md#extensions).
2. Set the command for each event; use **`{file}`** for the file path (the active thconfig for
   build).

Use them to trigger a backup, a custom post-processor, a notification, or a version-control commit.
Disable on very large workspaces if they slow things down.

## Where things live

- Plugins: `%AppData%/ThIDE/plugins/*.dll`
- Rule config: `rules.json` (app data)
- The CLI and LSP are **separate executables** shipped alongside the app, not the GUI itself.

See also the developer-facing [docs/architecture.md](../architecture.md) if you want to reuse the
underlying libraries in your own .NET program.

---

Next: [Keyboard shortcuts →](21-keyboard-shortcuts.md)
