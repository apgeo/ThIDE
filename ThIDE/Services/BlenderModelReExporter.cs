// Re-export for the Blender module: when the workspace has no built model, run Therion on a copy of
// the active thconfig that exports only the model, in a temporary folder. The project folder gets
// no file: the copy names its sources by absolute path and writes its outputs next to itself.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender.Sources;
using Therion.Build;
using Therion.Cave3D;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Resources;

namespace ThIDE.Services;

/// <summary>Exports the active project's 3-D model with Therion into a temporary folder.</summary>
public sealed class BlenderModelReExporter : IModelReExporter
{
    /// <summary>How long a finished export folder is kept: the render that asked for it reads the
    /// model within seconds, the rest is for reading its log.</summary>
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    private readonly Func<string?> _activeThconfig;
    private readonly ITherionCompiler _compiler;
    private readonly string _root;

    /// <param name="activeThconfig">The active thconfig's full path, or null when none.</param>
    /// <param name="root">The folder that holds one sub-folder per export.</param>
    public BlenderModelReExporter(Func<string?> activeThconfig, ITherionCompiler compiler, string root)
    {
        _activeThconfig = activeThconfig ?? throw new ArgumentNullException(nameof(activeThconfig));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>The temporary folder ThIDE uses for re-exports.</summary>
    public static string DefaultRoot() => Path.Combine(Path.GetTempPath(), "ThIDE", "blender-reexport");

    public async Task<ModelArtifact?> ReExportAsync(CancellationToken cancellationToken = default)
    {
        var config = _activeThconfig();
        if (string.IsNullOrEmpty(config) || !File.Exists(config)) return null;

        Prune();
        string folder = Path.Combine(_root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        // Latin-1 both ways keeps every byte of the project's config as it was.
        string text = ModelExportConfig.Create(config, ReadLatin1);
        string entry = Path.Combine(folder, "model.thconfig");
        await File.WriteAllTextAsync(entry, text, Encoding.Latin1, cancellationToken).ConfigureAwait(false);

        var result = await _compiler.CompileAsync(entry, progress: null, cancellationToken).ConfigureAwait(false);

        var model = new FileInfo(Path.Combine(folder, ModelExportConfig.ModelFileName));
        if (model.Exists && model.Length > 0)
            return new ModelArtifact(model.FullName, CaveSourceFormat.Lox, model.Length, model.LastWriteTimeUtc);

        // Therion's own message stays English, as everywhere its output is shown.
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        string detail = error?.Message ?? $"exit code {result.ExitCode}";
        throw new ModelSourceNotFoundException(string.Format(
            System.Globalization.CultureInfo.CurrentCulture, Tr.Get("Blender_ReExport_Failed"),
            Path.GetFileName(config), detail, result.LogPath ?? folder));
    }

    private static string? ReadLatin1(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.Latin1) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Removes export folders older than <see cref="KeepFor"/>; a folder in use by
    /// another process is left for next time.</summary>
    private void Prune()
    {
        if (!Directory.Exists(_root)) return;
        var cutoff = DateTime.UtcNow - KeepFor;
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
