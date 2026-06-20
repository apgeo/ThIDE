// Implementation Plan §6.1 — entry-point discovery.
// Order: no-extension first ? .thconfig/.thc ? syntax-based autodetect.

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

        var noExt = new List<string>();
        var withExt = new List<string>();
        var others = new List<string>();

        foreach (var file in Directory.EnumerateFiles(pathOrFolder, "*", enumOptions))
        {
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext)) noExt.Add(file);
            else if (Array.Exists(ConfigExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                withExt.Add(file);
            else others.Add(file);
        }

        var candidates = ImmutableArray.CreateBuilder<string>();
        foreach (var f in noExt) candidates.Add(Path.GetFullPath(f));
        foreach (var f in withExt) candidates.Add(Path.GetFullPath(f));

        // Syntax-based autodetect on remaining files.
        foreach (var f in others)
        {
            if (_sniffer.Probe(f) == SnifferVerdict.Likely)
                candidates.Add(Path.GetFullPath(f));
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
