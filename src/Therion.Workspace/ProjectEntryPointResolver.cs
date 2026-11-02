// Implementation Plan §6.1 — entry-point discovery.
// Order: files named as configs (.thconfig/.thc, bare `thconfig`) first; only when there are
// none, a content sniff of the other extensionless files.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Workspace;

public sealed class ProjectEntryPointResolver : IProjectEntryPointResolver
{
    private static readonly string[] ConfigExtensions = { ".thconfig", ".thc" };

    private readonly IThconfigSniffer _sniffer;
    private readonly WorkspaceOptions _options;

    public ProjectEntryPointResolver(IThconfigSniffer sniffer, WorkspaceOptions? options = null)
    {
        _sniffer = sniffer;
        _options = options ?? new WorkspaceOptions();
    }

    public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken cancellationToken = default)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (File.Exists(pathOrFolder))
        {
            // Single file: accept as-is regardless of extension.
            return new ValueTask<EntryPointResolution>(new EntryPointResolution(
                ImmutableArray.Create(Path.GetFullPath(pathOrFolder)),
                Path.GetFullPath(pathOrFolder),
                ImmutableArray<Diagnostic>.Empty));
        }

        if (!Directory.Exists(pathOrFolder))
        {
            diagnostics.Add(Diagnostic.Create(
                "TH_WS_001",
                DiagnosticSeverity.Error,
                $"Path not found: {pathOrFolder}",
                SourceSpan.None));
            return new ValueTask<EntryPointResolution>(new EntryPointResolution(
                ImmutableArray<string>.Empty, null, diagnostics.ToImmutable()));
        }

        // Folder scan.
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = _options.RecursiveFolderScan,
            MaxRecursionDepth = _options.MaxFolderScanDepth,
            IgnoreInaccessible = true,
        };

        // Files named as configs (.thconfig/.thc, or the bare `thconfig`) win; only when there are
        // none are other extensionless files sniffed, as ThconfigDiscovery does. Survey files
        // (.th/.th2) are never candidates, and the files the app generates are skipped.
        var named = new List<string>();
        var noExt = new List<string>();

        foreach (var file in Directory.EnumerateFiles(pathOrFolder, "*", enumOptions))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(ProjectLocalConfig.GeneratedFilePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(file);
            if (Array.Exists(ConfigExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) ||
                (ext.Length == 0 && string.Equals(name, "thconfig", StringComparison.OrdinalIgnoreCase)))
                named.Add(file);
            else if (ext.Length == 0) noExt.Add(file);
        }

        var candidates = ImmutableArray.CreateBuilder<string>();
        foreach (var f in named) candidates.Add(Path.GetFullPath(f));

        // Content-based autodetect on extensionless files.
        if (candidates.Count == 0)
        {
            foreach (var f in noExt)
            {
                if (_sniffer.Probe(f) == SnifferVerdict.Likely)
                    candidates.Add(Path.GetFullPath(f));
            }
        }

        string? selected = candidates.Count == 1 ? candidates[0] : null;
        if (candidates.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                "TH_WS_002",
                DiagnosticSeverity.Warning,
                $"No Therion configuration file found in '{pathOrFolder}'.",
                SourceSpan.None));
        }

        return new ValueTask<EntryPointResolution>(new EntryPointResolution(
            candidates.ToImmutable(), selected, diagnostics.ToImmutable()));
    }
}
