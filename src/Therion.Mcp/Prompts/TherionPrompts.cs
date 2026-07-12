using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Therion.Mcp.Prompts;

/// <summary>
/// MCP prompts (T-04.3): a handful of ready-made task templates a host offers by name. Each is a short
/// instruction that leans on the existing tools, kept deliberately terse for small-model context. They
/// lead with the read tools, so they still do something useful under the <c>data</c> profile — the write
/// steps degrade to "here is the fix to apply" rather than failing. Registered in both profiles.
/// Descriptions are English (D-008).
/// </summary>
[McpServerPromptType]
public static class TherionPrompts
{
    [McpServerPrompt(Name = "audit_workspace", Title = "Audit the project")]
    [Description("Walk the project for problems and summarize what to fix first.")]
    public static string AuditWorkspace() =>
        "Audit this Therion project. Be concise.\n"
        + "1. Call get_diagnostics for every problem.\n"
        + "2. For each distinct code, call explain_diagnostic to understand it.\n"
        + "3. Call survey_graph and report any floating (disconnected, ungrounded) components.\n"
        + "4. Summarize: counts of errors and warnings, the most important issues, and what to fix first.";

    [McpServerPrompt(Name = "fix_diagnostic", Title = "Fix a diagnostic")]
    [Description("Investigate one diagnostic code and propose (or apply) the fix.")]
    public static string FixDiagnostic(
        [Description("The diagnostic code to fix, exactly as get_diagnostics reported it, e.g. TH_SEM_015.")]
        string code) =>
        $"Help fix the diagnostic {code} in this project.\n"
        + $"1. Call get_diagnostics to find every occurrence of {code}.\n"
        + $"2. Call explain_diagnostic('{code}') for what it means and an example fix.\n"
        + "3. Read the relevant lines with read_file.\n"
        + "4. Propose the exact edit for each occurrence. If format_file or rename_symbol fits and is "
        + "available, run it with dryRun:true first and show the plan before applying.";

    [McpServerPrompt(Name = "summarize_survey", Title = "Summarize the survey")]
    [Description("A plain-language summary of the cave: size, structure, and what's left to explore.")]
    public static string SummarizeSurvey() =>
        "Summarize this cave survey for a caver, in plain language. Be concise.\n"
        + "1. Call survey_stats for the totals (length, station count, per-survey breakdown).\n"
        + "2. Call survey_graph for the structure (connected pieces, junctions, dead-ends).\n"
        + "3. Call list_leads for the open leads.\n"
        + "4. Write a short summary: how big it is, how it's laid out, and what remains to push.";

    [McpServerPrompt(Name = "prepare_release", Title = "Check release readiness")]
    [Description("Check whether the project is ready to build and publish.")]
    public static string PrepareRelease() =>
        "Check whether this Therion project is ready to build and publish. Be concise.\n"
        + "1. Call get_diagnostics — report errors (blocking) separately from warnings.\n"
        + "2. Call survey_graph — flag any floating components.\n"
        + "3. If run_build is available, call it and report Therion's verdict and the artifacts it wrote.\n"
        + "4. List what must be fixed before release, most important first.";
}
