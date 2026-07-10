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
}

/// <param name="Text">The generated Therion source, on a dry run. Null once it has been written.</param>
/// <param name="Truncated">The text was longer than the byte budget and was cut.</param>
/// <param name="Target">Workspace-relative path of the .th, when one was named.</param>
public sealed record ImportResult(
    string Format,
    string? Text,
    bool Truncated,
    string? Target,
    MutationResult? Mutation);

/// <summary>Ring R2 — translating another program's survey file into Therion.</summary>
[McpServerToolType]
public sealed class ImportTools(WorkspaceHost host, MutationEngine mutations)
{
    /// <summary>Extension → format. Anything else needs the caller to say which format it is.</summary>
    private static readonly Dictionary<string, ImportFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dat"] = ImportFormat.Compass,
        [".svx"] = ImportFormat.Survex,
        [".gpx"] = ImportFormat.Gpx,
    };

    // Create-only, like the scaffolds: it writes a new .th and refuses an existing one.
    [McpServerTool(Name = "import_survey", Title = "Import survey",
        ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Translates a Compass (.dat), Survex (.svx) or GPX (.gpx) file into Therion source. "
               + "Returns the generated text by default; give a target and dryRun:false to write it "
               + "to a new .th, which is never overwritten. The format is taken from the extension "
               + "unless you name it. Compass column-order overrides (FORMAT) and .mak project files "
               + "are not decoded, so check the result before trusting it.")]
    public async Task<ToolResult<ImportResult>> ImportSurvey(
        [Description("Workspace-relative path of the file to import, e.g. 'raw/cave.dat'.")]
        string source,
        [Description("Workspace-relative .th to create. Omit to only look at the generated text.")]
        string? target = null,
        [Description("compass, survex, or gpx. Taken from the source's extension when omitted.")]
        string? format = null,
        [Description("Name for the generated survey. GPX only; the others carry their own names.")]
        string surveyName = "gps",
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

        string therion;
        try
        {
            therion = Translate(resolved, text, surveyName);
        }
        catch (Exception ex)
        {
            // The importers are string→string and do not promise to validate their input.
            return Fail(ToolErrorCodes.ImportFailed, $"'{source}' is not readable as {Name(resolved)}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(therion))
            return Fail(ToolErrorCodes.ImportFailed, $"'{source}' produced no survey data.");

        var relativeTarget = targetFull is null ? null : WorkspacePaths.ToRelative(root, targetFull);

        if (targetFull is null)
            return Success(resolved, Cap(therion, maxBytes, out var truncated), truncated, null, null);

        var plan = new MutationPlan([new CreateFile(targetFull, therion)]);
        var applied = await mutations.ApplyAsync(plan, dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<ImportResult>.Failure(failure);

        return dryRun
            ? Success(resolved, Cap(therion, maxBytes, out var cut), cut, relativeTarget, applied.Data)
            : Success(resolved, null, false, relativeTarget, applied.Data);
    }

    private static string Translate(ImportFormat format, string text, string surveyName) => format switch
    {
        ImportFormat.Compass => CompassImporter.Import(text),
        ImportFormat.Survex => SurvexImporter.Import(text),
        ImportFormat.Gpx => GpxImporter.ToTherion(text, surveyName),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static bool TryResolveFormat(string? format, string sourcePath, out ImportFormat resolved, out string error)
    {
        error = "";

        if (!string.IsNullOrWhiteSpace(format))
        {
            if (ToolEnums.TryParse(format, out resolved)) return true;
            error = $"Unknown format '{format}'. Use one of: {ToolEnums.Names<ImportFormat>()}.";
            return false;
        }

        var extension = Path.GetExtension(sourcePath);
        if (ByExtension.TryGetValue(extension, out resolved)) return true;

        error = $"Cannot tell what '{extension}' is. Name the format: {ToolEnums.Names<ImportFormat>()}.";
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
        ImportFormat format, string? text, bool truncated, string? target, MutationResult? mutation) =>
        ToolResult<ImportResult>.Success(new ImportResult(Name(format), text, truncated, target, mutation));

    private static ToolResult<ImportResult> Fail(string code, string message) =>
        ToolResult<ImportResult>.Failure(code, message);
}
