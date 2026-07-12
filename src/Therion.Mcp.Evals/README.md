# therion-mcp-evals

The deterministic evaluation harness for the Therion MCP server (**T-05.2**). It drives a local model
through the MCP tool loop over a committed prompt set and scores the **end state** with the library's own
answers — no LLM judge (D-011). One command produces one [MODEL-EVALS](../../.claude/mcp-integration/MODEL-EVALS.md)
row, so runs stay comparable across models, hosts, and months.

## How it works

For each case it takes a fresh working copy of a fixture workspace (so a mutation never dirties the
committed one), spawns `therion-mcp` on it over stdio (`full` profile), advertises the tools to the model,
runs the OpenAI-style tool loop, then **grades deterministically**:

- exact-number Q&A — the grader recomputes the answer from the server (`survey_graph`, `list_files`, …) and
  requires the model's reply to contain that value, so the check can't be fooled and no number is hard-coded;
- end-state checks — lint-clean (`get_diagnostics`), a file created (`scaffold_th2`, `export_tables`);
- graceful handling — the refusal/ambiguity cases pass only if the model didn't invent a tool.

Metrics: `call_validity`, `task_success`, `repair_success@3`, `qa_exact`, median tokens/wall.

## Run it

1. **Build** the solution (`dotnet build ThIDE.sln -m:1`) — this also builds the `therion-mcp` server the
   harness spawns.
2. **Start a local model** on an OpenAI-compatible endpoint. In **LM Studio**: load a model, start the local
   server (default `http://localhost:1234/v1`). **Ollama**: `http://localhost:11434/v1`.
3. **Run:**

```sh
therion-mcp-evals --model qwen3-coder-30b-a3b --endpoint http://localhost:1234/v1 \
                  --run-id R-001 --notes "Q4_K_M, 24GB" --out results/r-001
```

Prints a per-case pass/fail line, the scorecard, and the MODEL-EVALS row; `--out` also writes
`results/r-001.md` (the row) and `.json` (per-case detail). `--filter <substr>` runs a subset.

## Verify without a model

Both run in CI / on any machine, no GPU, no endpoint:

```sh
therion-mcp-evals --self-test   # suite integrity (unique ids, every category, fixtures present) + scorecard math
therion-mcp-evals --probe       # spawn the server on each fixture and print the ground truth the graders compute
```

`--probe` is how you confirm a new fixture actually parses and produces the diagnostics/graph its cases
assume, before spending GPU time.

## Add a case

Add an `EvalCase` to [`EvalSuite.cs`](EvalSuite.cs) (prefer an `AnswerMatchesComputed` or an end-state check
over free-text matching) and, if it needs a new project, a fixture under [`workspaces/`](workspaces). The
self-test enforces unique ids, category coverage, and that every referenced workspace exists.
