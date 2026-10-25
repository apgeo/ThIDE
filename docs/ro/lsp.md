# Serverul de limbaj (EXT-05)

`therion-lsp` expune parserul și semantica ThIDE ca [Language Server](https://microsoft.github.io/language-server-protocol/),
astfel încât orice client LSP (VSCode, Neovim, Helix, Emacs/eglot, …) primește diagnostice Therion în propriul editor.

Comunică prin JSON-RPC pe **stdio** și în prezent publică **diagnostice** la deschidere/modificare
(fișierele `.th` sunt parsate și legate semantic; `.th2`, `.thconfig`/`.thc`, `.xvi` sunt doar parsate).
Hover / salt la definiție pot fi adăugate ulterior — motoarele de parsare/semantică le suportă deja.

## Build / rulare

```bash
dotnet build src/Therion.Lsp        # produces the `therion-lsp` executable
therion-lsp                          # speaks LSP over stdin/stdout
```

## Configurarea clientului

Configurați clientul LSP al editorului să folosească executabilul `therion-lsp` pentru tipurile de
fișiere Therion. De exemplu, cu `nvim-lspconfig` din Neovim ați înregistra un server al cărui `cmd`
este `{ "therion-lsp" }` și ale cărui `filetypes` sunt `{ "therion" }`; clienții VSCode îl lansează ca
transport de server stdio.

## Capabilități

- `initialize` → `textDocumentSync = full`, `serverInfo`.
- `textDocument/didOpen` / `didChange` → `textDocument/publishDiagnostics`.
- `textDocument/didClose` → șterge diagnosticele.
- `shutdown` / `exit`.

Maparea diagnosticelor (Therion → severități LSP + intervale indexate de la 0) se află în
`Therion.Lsp.DiagnosticProvider` și este acoperită de teste unitare.
