# MCP server (`therion-mcp`)

> **Draft.** Ring R1 (read-only) is complete and usable. Mutations, the in-app host and the
> `--profile` flag land in later batches; this page grows with them.

`therion-mcp` exposes ThIDE's parser, semantics and workspace engines over the
[Model Context Protocol](https://modelcontextprotocol.io), so an LLM can answer questions about a
Therion project — what is broken, where a station is declared, whether the cave is one connected
piece — without you pasting files into a chat window.

It speaks MCP over **stdio**. The *host* (Claude Code, LM Studio, an Ollama bridge) spawns it as a
child process; running it in a terminal by hand does nothing useful.

ThIDE implements the **server**. The model lives in the host.

## What it can do today

Twenty read-only tools:

| Area | Tools |
|---|---|
| Project | `server_info`, `workspace_info`, `load_workspace`, `list_files`, `read_file` |
| Correctness | `get_diagnostics`, `explain_diagnostic` |
| Navigation | `list_symbols`, `goto_definition`, `find_references` |
| Survey | `survey_graph`, `survey_stats`, `deps_graph`, `list_stations` |
| Field notes | `list_todos`, `list_leads` |
| Calculation | `structural_analysis`, `convert_units`, `convert_coordinates`, `get_declination` |

Nothing here writes to your files.

## Building

```bash
dotnet build ThIDE.sln -m:1          # -m:1 is required; parallel MSBuild runs out of memory
```

The executable lands at `src/Therion.Mcp.Server/bin/Debug/net8.0/therion-mcp` (`.exe` on Windows).

```bash
therion-mcp --help
therion-mcp --version
therion-mcp --workspace /caves/pestera/project.thconfig
```

`--workspace` takes a `.thconfig`, a `.th`, or a project folder containing exactly one entry-point
candidate. Without it the model must call `load_workspace` before anything else works. The path is
checked at startup, so a typo fails loudly instead of producing a server that answers
"no workspace loaded" forever.

## Claude Code

```bash
claude mcp add therion -- /abs/path/to/therion-mcp --workspace /abs/path/to/project.thconfig
```

Then ask it something:

> What's wrong with this project? Use get_diagnostics, and explain any code I won't recognise.

## LM Studio (≥ 0.3.17)

Edit `mcp.json` (**Program ▸ Install ▸ Edit mcp.json**):

```json
{
  "mcpServers": {
    "therion": {
      "command": "/abs/path/to/therion-mcp",
      "args": ["--workspace", "/abs/path/to/project.thconfig"]
    }
  }
}
```

Use an absolute path to the executable: LM Studio does not spawn it through a shell, so `~` and
`PATH` lookups will not resolve.

## What the model sees

Every tool answers with the same envelope, so a model can branch on one field:

```json
{ "ok": true,  "data": { … } }
{ "ok": false, "error": { "code": "workspace_not_loaded", "message": "…" } }
```

Failures are *answers*, not protocol errors — a model that receives an exception ends its turn,
while one that receives `{"ok":false,"error":{"code":"symbol_not_found"}}` can try something else.
List-returning tools page with `offset`/`limit` and report `total` plus `truncated`.

## Safety

- **Read-only.** No tool in this batch writes a file, and every tool is annotated `readOnlyHint`.
- **Path jail.** Every path argument is canonicalized — symlinks and `..` resolved, component by
  component — and refused if it leaves the workspace root. `load_workspace` is the sole exception:
  it *defines* the root.
- **Prompt injection is real.** A `.th` file from a stranger can contain text aimed at the model,
  not at you. The jail is what keeps such a file from being able to read `~/.ssh`.
- **Result caps.** Text payloads default to 100 KB (`maxBytes`, hard ceiling 1 MB); lists default to
  200 entries (`limit`, ceiling 2000). Local models have small context windows.

## Troubleshooting

**The host shows no tools.** Run `therion-mcp --version` by hand first. If that works, check the
host is passing an absolute path to the executable.

**`get_declination` says `model_unavailable`.** ThIDE ships no `WMM.COF` — it is a public-domain
NOAA download. The error message names the three paths the server searches; put the file in one.

**Everything answers `workspace_not_loaded`.** The server was started without `--workspace`. Either
add it, or have the model call `load_workspace` first.

**Diagnosing a session.** The server logs to stderr at `Warning` and above; the host displays it.
Set `Logging__LogLevel__Default=Information` in the server's environment to see every tool call.

## Design notes

- Architecture, hosting modes and the security model: `.claude/mcp-integration/02-server-architecture.md`
- The live tool contract: `.claude/mcp-integration/TOOL-REGISTRY.md`
