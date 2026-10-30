using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;
using Therion.Syntax;
using Therion.Workspace.Import;

namespace Therion.Mcp.Tools;

/// <summary>The survey formats this server can translate into Therion.</summary>
public enum ImportFormat
{
    /// <summary>Compass <c>.dat</c>.</summary>
    Compass,
    /// <summary>Survex <c>.svx</c>.</summary>
    Survex,
    /// <summary>GPS waypoints, <c>.gpx</c> — becomes a survey of fixed stations.</summary>
    Gpx,
    /// <summary>A table of shots, <c>.csv</c> / <c>.tsv</c> (a spreadsheet, PocketTopo or DistoX export).</summary>
    Csv,
}

/// <param name="Text">The generated Therion source, on a dry run. Null once it has been written.</param>
/// <param name="Truncated">The text was longer than the byte budget and was cut.</param>
/// <param name="Target">Workspace-relative path of the .th, when one was named.</param>
/// <param name="Warnings">What looked wrong in a shot table (line, value), so the result can be checked.</param>
public sealed record ImportResult(
    string Format,
    string? Text,
    bool Truncated,
    string? Target,
    MutationResult? Mutation,
    IReadOnlyList<string>? Warnings = null);

/// <summary>Ring R2 — translating another program's survey file into Therion.</summary>
[McpServerToolType]
public sealed class ImportTools(IWorkspaceHost host, MutationEngine mutations)
{
    // Create-only, like the scaffolds: it writes a new .th and refuses an existing one.
    [McpServerTool(Name = "import_survey", Title = "Import survey",
        ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Translates a Compass (.dat), Survex (.svx), GPX (.gpx) or shot-table (.csv/.tsv: "
               + "from to length compass clino, with or without a header) file into Therion source. "
               + "Returns the generated text by default; give a target and dryRun:false to write it "
               + "to a new .th, which is never overwritten. The format is taken from the extension "
               + "unless you name it. Compass column-order overrides (FORMAT) and .mak project files "
               + "are not decoded, so check the result before trusting it.")]
    public async Task<ToolResult<ImportResult>> ImportSurvey(
        [Description("Workspace-relative path of the file to import, e.g. 'raw/cave.dat'.")]
        string source,
        [Description("Workspace-relative .th to create. Omit to only look at the generated text.")]
        string? target = null,
        [Description("compass, survex, gpx, or csv. Taken from the source's extension when omitted.")]
        string? format = null,
        [Description("Name for the generated survey (GPX and csv; the others carry their own names). "
                   + "Defaults to 'gps' for GPX and the file's name for csv.")]
        string? surveyName = null,
        [Description("Preview without writing. Defaults to true — pass false to create the target.")]
        bool dryRun = true,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ImportResult>.Failure(error);

        var root = snapshot!.Root;

        if (!WorkspacePaths.TryResolve(root, source, out var sourceFull, out var sourceReason))
            return Fail(ToolErrorCodes.PathOutsideWorkspace, sourceReason);
        if (!File.Exists(sourceFull))
            return Fail(ToolErrorCodes.FileNotFound, $"No such file: {source}");

        if (!TryResolveFormat(format, sourceFull, out var resolved, out var formatError))
            return Fail(ToolErrorCodes.InvalidArgument, formatError);

        string? targetFull = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (!WorkspacePaths.TryResolve(root, target, out targetFull, out var targetReason))
                return Fail(ToolErrorCodes.PathOutsideWorkspace, targetReason);
            if (!Path.GetExtension(targetFull).Equals(".th", StringComparison.OrdinalIgnoreCase))
                return Fail(ToolErrorCodes.InvalidArgument, "The imported survey must be written to a '.th' file.");
        }
        else if (!dryRun)
        {
            return Fail(ToolErrorCodes.InvalidArgument, "Give a target .th to write the import to.");
        }

        string text;
        try
        {
            text = EncodingResolver.ReadAllText(sourceFull);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ToolErrorCodes.ReadFailed, ex.Message);
        }

        // GpxImporter.Parse swallows malformed XML and returns no waypoints, so ToTherion would happily
        // emit an empty survey and this tool would call it a success. Ask before translating.
        if (resolved is ImportFormat.Gpx && GpxImporter.Parse(text).Count == 0)
            return Fail(ToolErrorCodes.ImportFailed,
                $"'{source}' holds no waypoints — it is empty, or it is not gpx.");

        surveyName = string.IsNullOrWhiteSpace(surveyName)
            ? resolved == ImportFormat.Csv ? ScaffoldValueRules.SafeSurveyName(Path.GetFileNameWithoutExtension(sourceFull)) : "gps"
            : surveyName;

        if (!ScaffoldValueRules.IsSurveyName(surveyName))
            return Fail(ToolErrorCodes.InvalidArgument, $"'{surveyName}' is not a survey name: letters, digits, '_', '-' and '/', no spaces.");

        string therion;
        var issues = new List<ShotIssue>();
        try
        {
            therion = SurveyImport.Translate(Enum.Parse<SurveyImportFormat>(resolved.ToString()), text, surveyName, issues: issues);
        }
        catch (Exception ex)
        {
            // The importers are string→string and do not promise to validate their input.
            return Fail(ToolErrorCodes.ImportFailed, $"'{source}' is not readable as {Name(resolved)}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(therion))
            return Fail(ToolErrorCodes.ImportFailed, $"'{source}' produced no survey data.");

        var relativeTarget = targetFull is null ? null : WorkspacePaths.ToRelative(root, targetFull);

        var warnings = issues.Count == 0 ? null : issues.Select(ShotTableReader.Describe).ToList();
        if (targetFull is null)
            return Success(resolved, Cap(therion, maxBytes, out var truncated), truncated, null, null, warnings);

        var plan = new MutationPlan([new CreateFile(targetFull, therion)]);
        var applied = await mutations.ApplyAsync(plan, dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<ImportResult>.Failure(failure);

        return dryRun
            ? Success(resolved, Cap(therion, maxBytes, out var cut), cut, relativeTarget, applied.Data, warnings)
            : Success(resolved, null, false, relativeTarget, applied.Data, warnings);
    }

    private static bool TryResolveFormat(string? format, string sourcePath, out ImportFormat resolved, out string error)
    {
        error = "";

        if (!string.IsNullOrWhiteSpace(format))
        {
            if (ToolEnums.TryParse(format, out resolved)) return true;
            error = $"Unknown format '{format}'. Use one of: {ToolEnums.Names<ImportFormat>()}.";
            return false;
        }

        if (SurveyImport.TryResolveFormat(null, sourcePath, out var byExtension, out _))
        {
            resolved = Enum.Parse<ImportFormat>(byExtension.ToString());
            return true;
        }

        error = $"Cannot tell what '{Path.GetExtension(sourcePath)}' is. Name the format: {ToolEnums.Names<ImportFormat>()}.";
        resolved = default;
        return false;
    }

    private static string Cap(string text, int maxBytes, out bool truncated)
    {
        var capped = ToolLimits.Utf8Prefix(text, ToolLimits.ClampBytes(maxBytes));
        truncated = capped.Length < text.Length;
        return capped;
    }

    private static string Name(ImportFormat format) => format.ToString().ToLowerInvariant();

    private static ToolResult<ImportResult> Success(
        ImportFormat format, string? text, bool truncated, string? target, MutationResult? mutation, IReadOnlyList<string>? warnings = null) =>
        ToolResult<ImportResult>.Success(new ImportResult(Name(format), text, truncated, target, mutation, warnings));

    private static ToolResult<ImportResult> Fail(string code, string message) =>
        ToolResult<ImportResult>.Failure(code, message);
}
