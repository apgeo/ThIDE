// Implementation Plan §9bis.3 — generated artifact discovery.

using System.Collections.Immutable;
using Therion.Processing.Abstractions;

namespace Therion.Build;

/// <summary>Discovers Therion-generated output files in a working directory.</summary>
public interface IOutputArtifactCollector
{
    ImmutableArray<OutputArtifact> Collect(string workingDirectory, DateTimeOffset? since = null);
}

public sealed class OutputArtifactCollector : IOutputArtifactCollector
{
    /// <summary>Known extensions per §9bis.3.</summary>
    public static readonly ImmutableDictionary<string, string> KnownKinds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".lox"]  = "Loch 3D model",
            [".3d"]   = "Survex 3D model",
            [".pdf"]  = "PDF map",
            [".svg"]  = "SVG map",
            [".xvi"]  = "XVI sketch",
            [".shp"]  = "Shapefile",
            [".kml"]  = "KML",
            [".dxf"]  = "DXF",
            [".html"] = "HTML",
            [".png"]  = "PNG image",
            [".tlx"]  = "Therion log",
            [".dbf"]  = "DBF",
        }.ToImmutableDictionary();

    public ImmutableArray<OutputArtifact> Collect(string workingDirectory, DateTimeOffset? since = null)
    {
        if (!Directory.Exists(workingDirectory)) return ImmutableArray<OutputArtifact>.Empty;

        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 4,
            IgnoreInaccessible = true,
        };

        var builder = ImmutableArray.CreateBuilder<OutputArtifact>();
        foreach (var path in Directory.EnumerateFiles(workingDirectory, "*", enumOpts))
        {
            var ext = Path.GetExtension(path);
            if (!KnownKinds.TryGetValue(ext, out var kind)) continue;

            FileInfo info;
            try { info = new FileInfo(path); }
            catch { continue; }

            var when = (DateTimeOffset)info.LastWriteTimeUtc;
            if (since is { } floor && when < floor) continue;

            builder.Add(new OutputArtifact(path, kind, info.Length, when));
        }
        return builder.ToImmutable();
    }
}
