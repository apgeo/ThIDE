using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Structural;

namespace Therion.Mcp.Tools;

/// <param name="Dip">Angle below horizontal, 0 (flat) to 90 (vertical).</param>
/// <param name="Strike">Right-hand-rule strike: dip direction − 90, degrees clockwise from north.</param>
/// <param name="DipDirection">Azimuth of steepest descent, degrees clockwise from north.</param>
/// <param name="RmsResidual">RMS distance of the points from the fitted plane, in metres. 0 is perfectly planar.</param>
/// <param name="ErrorReason">Why the fit failed, when <c>valid</c> is false; null otherwise.</param>
public sealed record FittedPlaneDto(
    string Name,
    bool Valid,
    double? Dip,
    double? Strike,
    double? DipDirection,
    int PointCount,
    double? RmsResidual,
    string? ErrorReason);

/// <param name="DeclinationApplied">
/// Degrees added to every azimuth. 0 means the azimuths are magnetic-north; non-zero means true-north.
/// </param>
public sealed record StructuralResult(
    IReadOnlyList<FittedPlaneDto> Planes,
    double DeclinationApplied,
    string DeclinationSource,
    string? DeclinationNote);

/// <summary>Ring R1 — fitting geological planes (strike/dip) to shots aimed at bedding and joints.</summary>
[McpServerToolType]
public sealed class StructuralTools(WorkspaceHost host)
{
    [McpServerTool(Name = "structural_analysis", Title = "Structural analysis", ReadOnly = true, Idempotent = true)]
    [Description("Fits geological planes to structural shots in one .th file, reporting strike, dip "
               + "and dip direction per plane. Shots are recognised by a keyword in the from-station "
               + "name — 'geo' by default. Azimuths are magnetic-north unless a declination is "
               + "applied. A plane needs at least three points; fewer is reported as an invalid "
               + "plane with a reason, not as an error.")]
    public async Task<ToolResult<StructuralResult>> StructuralAnalysis(
        [Description("Workspace-relative path to a .th file, e.g. 'caves/upper.th'.")]
        string file,
        [Description("Substring matched against from-station names to find structural shots. Defaults to 'geo'.")]
        string? keyword = null,
        [Description("'survey' to use the file's own declination command, or a number of degrees east. Omit for none.")]
        string? declination = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<StructuralResult>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, file, out var full, out var reason))
            return ToolResult<StructuralResult>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (!Path.GetExtension(full).Equals(".th", StringComparison.OrdinalIgnoreCase))
            return ToolResult<StructuralResult>.Failure(ToolErrorCodes.InvalidArgument,
                "Structural analysis reads a .th survey file.");

        if (!snapshot.Model.PerFile.TryGetValue(full, out var model))
            return ToolResult<StructuralResult>.Failure(ToolErrorCodes.FileNotFound,
                $"'{file}' is not part of the loaded project. Call list_files to see what is.");

        var detection = new DetectionOptions();
        if (!string.IsNullOrWhiteSpace(keyword))
            detection = detection with { NameKeywords = ImmutableArray.Create(keyword) };

        if (!TryResolveDeclination(declination, model.Declination, out var declinationOptions, out var inputs))
            return ToolResult<StructuralResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"declination must be 'survey' or a number of degrees, not '{declination}'.");

        var result = Therion.Structural.StructuralAnalysis.Analyze(model, new StructuralOptions
        {
            Detection = detection,
            Declination = declinationOptions,
            DeclinationInputs = inputs,
        });

        var planes = new List<FittedPlaneDto>(result.Planes.Length);
        for (int i = 0; i < result.Planes.Length; i++)
        {
            var plane = result.Planes[i];
            planes.Add(new FittedPlaneDto(
                Name: result.Batches[i].Name,
                Valid: plane.IsValid,
                Dip: plane.IsValid ? Round(plane.Dip) : null,
                Strike: plane.IsValid ? Round(plane.Strike) : null,
                DipDirection: plane.IsValid ? Round(plane.DipDirection) : null,
                PointCount: plane.PointCount,
                RmsResidual: plane.IsValid ? Round(plane.RmsResidual) : null,
                ErrorReason: plane.ErrorReason));
        }

        return ToolResult<StructuralResult>.Success(new StructuralResult(
            Planes: planes,
            DeclinationApplied: Round(result.Declination.Delta),
            DeclinationSource: result.Declination.Effective.ToString().ToLowerInvariant(),
            DeclinationNote: result.Declination.Note));
    }

    /// <summary>
    /// Mirrors the CLI verb: <c>survey</c> takes the declination the file declares, a number is used
    /// verbatim (degrees east), anything else is a caller error.
    /// </summary>
    private static bool TryResolveDeclination(
        string? declination, double? surveyDeclared, out DeclinationOptions options, out DeclinationInputs inputs)
    {
        options = new DeclinationOptions();
        inputs = default;

        if (string.IsNullOrWhiteSpace(declination)) return true;

        if (declination.Equals("survey", StringComparison.OrdinalIgnoreCase))
        {
            options = new DeclinationOptions { Source = DeclinationSource.SurveyDeclared };
            inputs = new DeclinationInputs(SurveyDeclaredDegrees: surveyDeclared);
            return true;
        }

        if (double.TryParse(declination, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees))
        {
            options = new DeclinationOptions { Source = DeclinationSource.Manual, ManualDegrees = degrees };
            return true;
        }

        return false;
    }

    /// <summary>A tenth of a degree is already finer than a hand-held compass reads.</summary>
    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
