# Language Server (EXT-05)

`therion-lsp` exposes ThIDE's parser + semantics as a [Language Server](https://microsoft.github.io/language-server-protocol/),
so any LSP client (VSCode, Neovim, Helix, Emacs/eglot, …) gets Therion diagnostics in its own editor.

It speaks JSON-RPC over **stdio** and currently publishes **diagnostics** on open/change
(`.th` files are parsed + semantically bound; `.th2`, `.thconfig`/`.thc`, `.xvi` are parsed).
Hover / go-to-definition can be layered on later — the parser/semantic engines already support them.

## Building / running

```bash
dotnet build src/Therion.Lsp        # produces the `therion-lsp` executable
therion-lsp                          # speaks LSP over stdin/stdout
```

## Client configuration

Point your editor's LSP client at the `therion-lsp` binary for Therion file types. For example, with
Neovim's `nvim-lspconfig` you'd register a server whose `cmd` is `{ "therion-lsp" }` and
`filetypes` are `{ "therion" }`; VSCode clients launch it as a stdio server transport.

## Capabilities

- `initialize` → `textDocumentSync = full`, `serverInfo`.
- `textDocument/didOpen` / `didChange` → `textDocument/publishDiagnostics`.
- `textDocument/didClose` → clears diagnostics.
- `textDocument/completion` → on the first word of a line: ThIDE's snippets (built-ins plus your
  `snippets.json`, filtered by file kind, with variables such as the date, the recent team and the
  next station already filled in for the cursor position) and Therion's command keywords. The
  editor's richer completion — flag values, data fields, station names — is not offered over LSP
  yet; snippets from a project's `.thide/snippets.json` are not read either.
- `shutdown` / `exit`.

The diagnostic mapping (Therion → LSP severities + 0-based ranges) lives in
`Therion.Lsp.DiagnosticProvider` and is unit-tested.
