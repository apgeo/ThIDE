namespace Therion.Mcp.Evals;

/// <summary>The ten prompt categories (MODEL-EVALS). Drives which metric a case feeds.</summary>
public enum Category
{
    Orientation,   // "what is this project / what's broken"
    Explain,       // diagnostic explanation
    Symbol,        // find refs / safe rename
    Qa,            // stats/graph exact-number Q&A   → qa_exact
    Format,        // formatting round-trip
    Scaffold,      // scaffold / import flow
    Export,        // export flow
    Build,         // build and read errors
    Audit,         // multi-step long session
    Repair,        // injected error, fix to lint-clean → repair_success@3
    Refusal,       // asks for something the tools can't do — must not hallucinate a tool
}

/// <summary>
/// A deterministic end-state check (D-011: no LLM judge). Pure data; <see cref="Grader"/> evaluates it
/// against the finished run and the live MCP server.
/// </summary>
public abstract record Check;

/// <summary>The workspace ends with zero error-severity diagnostics (get_diagnostics).</summary>
public sealed record LintClean : Check;

/// <summary>The model's final answer contains every one of <paramref name="Tokens"/> (case-insensitive).</summary>
public sealed record AnswerContains(params string[] Tokens) : Check;

/// <summary>
/// The grader computes ground truth by calling <paramref name="Tool"/> itself and reading the value at
/// <paramref name="Pointer"/> (a JSON pointer into the tool's <c>data</c>), then requires the model's final
/// answer to contain that exact value. This is qa_exact: the number is the library's, not a judge's.
/// </summary>
public sealed record AnswerMatchesComputed(string Tool, string Pointer, IReadOnlyDictionary<string, object?>? Args = null) : Check;

/// <summary>A file exists under the (working copy of the) workspace after the run.</summary>
public sealed record FileExists(string RelativePath) : Check;

/// <summary>
/// A file under the workspace holds every one of <paramref name="Tokens"/> (case-insensitive) after
/// the run. The end-state check for an authoring task: what matters is what the source now says, which
/// neither the model's prose nor a lint pass can establish — a thconfig lints clean without the export
/// line the user asked for.
/// </summary>
public sealed record FileContains(string RelativePath, params string[] Tokens) : Check;

/// <summary>
/// The model neither invented a tool (no call rejected as unknown) nor produced an empty answer — the
/// refusal/ambiguity case: it should say it can't, not hallucinate.
/// </summary>
public sealed record HandledGracefully : Check;

/// <param name="Id">Stable, unique. Also the row key in the details JSON.</param>
/// <param name="Workspace">Name of a committed fixture under <c>workspaces/</c> (copied per run so mutations don't dirty it).</param>
/// <param name="Prompt">The user turn handed to the model.</param>
public sealed record EvalCase(string Id, Category Category, string Workspace, string Prompt, Check Check);

/// <param name="SchemaValid">The call named a real tool and its arguments matched the schema.</param>
/// <param name="Ok">The tool returned <c>ok:true</c> (a well-formed but failing call is still schema-valid).</param>
public sealed record ToolCallRecord(string Tool, bool SchemaValid, bool Ok);

/// <param name="Turns">Model↔tool round-trips taken before the final answer.</param>
/// <param name="Tokens">Total tokens the endpoint reported, or 0 if it didn't.</param>
public sealed record CaseRun(
    EvalCase Case,
    string FinalText,
    IReadOnlyList<ToolCallRecord> Calls,
    int Turns,
    int Tokens,
    TimeSpan Wall,
    bool Passed,
    string Detail);
