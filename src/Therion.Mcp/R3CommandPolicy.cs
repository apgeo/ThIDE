using Therion.Processing.Abstractions;

namespace Therion.Mcp;

/// <summary>How <c>run_command</c> treats a shell command (doc 03 §C.3).</summary>
public enum R3CommandGate
{
    /// <summary>Benign and reversible — runs without confirmation.</summary>
    Allowed,

    /// <summary>Writes to the project or is otherwise consequential — needs <c>confirm:true</c>.</summary>
    Gated,

    /// <summary>Opens an OS/modal dialog, or needs editor focus — never run through this tool.</summary>
    Excluded,
}

/// <summary>The two scopes an app command lives in (mirrors <see cref="ShellCommandIds"/>).</summary>
public enum R3CommandScope
{
    /// <summary>Fires wherever focus is; reachable through the shell command map.</summary>
    Shell,

    /// <summary>Caret-scoped: only meaningful with an editor focused, so not run through this tool.</summary>
    Editor,
}

/// <param name="Id">The <see cref="ShellCommandIds"/> token.</param>
/// <param name="Title">A short English label (model-facing, so English per D-008).</param>
/// <param name="Category">A coarse grouping — Build, View, File, Navigate, Search, Edit, Tools.</param>
public sealed record R3Command(
    string Id, string Title, string Category, R3CommandGate Gate, R3CommandScope Scope);

/// <summary>
/// The <c>run_command</c> allowlist, classified once in code (doc 03 §C.3). Every
/// <see cref="ShellCommandIds"/> constant is listed exactly once — a coverage test fails the build if
/// a new command slips in unclassified, so a command can never become silently runnable by an agent.
/// The classification is deliberately conservative: OS/modal dialogs (the <c>scaffold-freeze</c> class)
/// are excluded, project writes are gated, and only toggles/navigation run freely.
/// </summary>
public static class R3CommandPolicy
{
    private static readonly R3Command[] All =
    [
        // ---- Shell scope: benign, reversible → Allowed --------------------------------------------
        new(ShellCommandIds.Build,               "Build the active project",        "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.Rebuild,             "Rebuild the active project",      "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.CancelBuild,         "Cancel the running build",        "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.OpenInLoch,          "Open the model in Loch",          "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.OpenInAven,          "Open the model in Aven",          "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.OpenOutputFolder,    "Open the output folder",          "Build",    R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleWorkspaceExplorer, "Toggle the Workspace panel",  "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleDiagnostics,   "Toggle the Diagnostics panel",    "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleObjectBrowser, "Toggle the Object Browser",       "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleOutline,       "Toggle the Outline panel",        "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleProject,       "Toggle the Project panel",        "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleLog,           "Toggle the Log panel",            "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleLivePreview,   "Toggle the Live Preview panel",   "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleMapViewer,     "Toggle the Map Viewer panel",     "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleModel3DViewer, "Toggle the 3D Model viewer",      "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleStructuralGeology, "Toggle the Structural Geology panel", "View", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleBlenderAnimation, "Toggle the Blender Animation panel", "View", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.SplitEditor,         "Split the editor",                "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ResetLayout,         "Reset the window layout",         "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.FloatActiveDocument, "Float the active document",       "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleWordWrap,      "Toggle word wrap",                "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ToggleFullScreen,    "Toggle full screen",              "View",     R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.GoBack,              "Navigate back",                   "Navigate", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.GoForward,           "Navigate forward",                "Navigate", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.NextProblem,         "Go to the next problem",          "Navigate", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.PreviousProblem,     "Go to the previous problem",      "Navigate", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.QuickOpen,           "Open the Go to File picker",      "Navigate", R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.FindInFiles,         "Open Find in Files",              "Search",   R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ReplaceInFiles,      "Open Replace in Files",           "Search",   R3CommandGate.Allowed, R3CommandScope.Shell),
        new(ShellCommandIds.ReopenClosedTab,     "Reopen the last closed tab",      "File",     R3CommandGate.Allowed, R3CommandScope.Shell),

        // ---- Shell scope: writes to the project → Gated (needs confirm:true) ----------------------
        new(ShellCommandIds.Save,                "Save the active file",            "File",     R3CommandGate.Gated,   R3CommandScope.Shell),
        // Reachable from the shell (a tool panel has focus), but the R2 rename_symbol tool is preferred.
        new(ShellCommandIds.RenameSymbol,        "Rename the symbol under the caret", "Edit",   R3CommandGate.Gated,   R3CommandScope.Shell),

        // ---- Shell scope: opens an OS/modal dialog → Excluded (use the parameterized tools) -------
        new(ShellCommandIds.OpenFile,            "Open a file…",                    "File",     R3CommandGate.Excluded, R3CommandScope.Shell),
        new(ShellCommandIds.OpenFolder,          "Open a folder…",                  "File",     R3CommandGate.Excluded, R3CommandScope.Shell),
        new(ShellCommandIds.OpenThconfig,        "Open a thconfig…",                "File",     R3CommandGate.Excluded, R3CommandScope.Shell),
        // These pop an OS save dialog (PickSaveFileAsync) or a modal window (QuickExportWindow.ShowDialog):
        // an agent that triggers one is stuck behind it with the owner window disabled — the scaffold-freeze
        // class doc 03 §C.3 excludes dialogs to prevent. The R2 tools are the safe path: generate_report,
        // scaffold_th2, export_gis/export_tables.
        new(ShellCommandIds.NewFile,             "New file…",                       "File",     R3CommandGate.Excluded, R3CommandScope.Shell),
        new(ShellCommandIds.GenerateReport,      "Generate an HTML report…",        "Tools",    R3CommandGate.Excluded, R3CommandScope.Shell),
        new(ShellCommandIds.QuickExport,         "Quick export…",                   "Build",    R3CommandGate.Excluded, R3CommandScope.Shell),
        new(ShellCommandIds.NewScrapScaffold,    "Insert a new scrap scaffold…",    "Edit",     R3CommandGate.Excluded, R3CommandScope.Shell),
        // An interactive picker overlay: pointless for an agent, which cannot fill it — use list_commands.
        new(ShellCommandIds.CommandPalette,      "Open the command palette",        "View",     R3CommandGate.Excluded, R3CommandScope.Shell),

        // ---- Editor scope → Excluded: caret-scoped, need a focused editor -------------------------
        new(ShellCommandIds.GoToDefinition,      "Go to definition",                "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.FindReferences,      "Find references",                 "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.PeekDefinition,      "Peek definition",                 "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.GoToMatchingBlock,   "Go to matching block",            "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.StepIntoInclude,     "Step into include",               "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.StepOutInclude,      "Step out of include",             "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.TriggerCompletion,   "Trigger completion",              "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.GoToLine,            "Go to line",                      "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.ToggleComment,       "Toggle comment",                  "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.FormatDocument,      "Format document",                 "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.EncloseInRegion,     "Enclose in region",              "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.QuickFixes,          "Show quick fixes",                "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.DuplicateLines,      "Duplicate lines",                 "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.MoveLinesUp,         "Move lines up",                   "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.MoveLinesDown,       "Move lines down",                 "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
        new(ShellCommandIds.SortLines,           "Sort selected lines",             "Edit",     R3CommandGate.Excluded, R3CommandScope.Editor),
    ];

    private static readonly IReadOnlyDictionary<string, R3Command> ById =
        All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    /// <summary>Every classified command (used by the coverage test).</summary>
    public static IReadOnlyList<R3Command> AllCommands => All;

    /// <summary>The commands <c>list_commands</c> advertises: shell-scope, and actually runnable (not excluded).</summary>
    public static IReadOnlyList<R3Command> RunnableCommands { get; } =
        All.Where(c => c.Scope == R3CommandScope.Shell && c.Gate != R3CommandGate.Excluded).ToList();

    /// <summary>Looks up a command's classification, or null when the id is unknown.</summary>
    public static R3Command? Find(string id) =>
        id is not null && ById.TryGetValue(id, out var c) ? c : null;
}
