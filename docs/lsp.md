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
- `shutdown` / `exit`.

The diagnostic mapping (Therion → LSP severities + 0-based ranges) lives in
`Therion.Lsp.DiagnosticProvider` and is unit-tested.
