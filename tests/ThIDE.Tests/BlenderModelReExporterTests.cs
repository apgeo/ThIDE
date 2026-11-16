// Re-export for the Blender module: the active thconfig's model exported into a temporary folder,
// with a fake Therion; and the source provider choosing it when the build output is missing or
// older than the sources. All files are invented.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.Tests;

public class BlenderModelReExporterTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "thide-reexport-proj-" + Guid.NewGuid().ToString("N"));
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "thide-reexport-tmp-" + Guid.NewGuid().ToString("N"));

    public BlenderModelReExporterTests()
    {
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "cave.thconfig"),
            "source cave.th\nlayout plan\n  scale 1 500\nendlayout\nexport map -o cave.pdf -layout plan\nsystem \"touch x\"\n");
        File.WriteAllText(Path.Combine(_project, "cave.th"), "survey cave\nendsurvey\n");
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _project, _scratch })
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private string Config => Path.Combine(_project, "cave.thconfig");

    /// <summary>Records the entry it was given and, like Therion, writes the model next to it.</summary>
    private sealed class FakeTherion(bool writesModel) : ITherionCompiler
    {
        public string? Entry { get; private set; }
        public string? EntryText { get; private set; }

        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Entry = entryPointPath;
            EntryText = File.ReadAllText(entryPointPath, Encoding.Latin1);
            if (writesModel)
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(entryPointPath)!, ModelExportConfig.ModelFileName), "LOX");
                return ValueTask.FromResult(new CompileResult(0, ImmutableArray<Diagnostic>.Empty, ImmutableArray<OutputArtifact>.Empty));
            }
            var error = Diagnostic.Create("TH_TEST", DiagnosticSeverity.Error, "invented failure", SourceSpan.None);
            return ValueTask.FromResult(new CompileResult(1, [error], ImmutableArray<OutputArtifact>.Empty));
        }
    }

    [Fact]
    public async Task Exports_into_its_own_folder_and_leaves_the_project_untouched()
    {
        var therion = new FakeTherion(writesModel: true);
        var before = Directory.GetFiles(_project);

        var model = await new BlenderModelReExporter(() => Config, therion, _scratch).ReExportAsync();

        Assert.NotNull(model);
        Assert.Equal(CaveSourceFormat.Lox, model!.Format);
        Assert.StartsWith(Path.GetFullPath(_scratch), model.Path, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(_scratch), therion.Entry!, StringComparison.Ordinal);
        Assert.Contains($"source \"{Path.Combine(_project, "cave.th")}\"", therion.EntryText);
        Assert.DoesNotContain("cave.pdf", therion.EntryText);
        Assert.DoesNotContain("system", therion.EntryText);
        Assert.Equal(before, Directory.GetFiles(_project));
    }

    [Fact]
    public async Task A_failed_export_reports_therions_error()
    {
        var reExporter = new BlenderModelReExporter(() => Config, new FakeTherion(writesModel: false), _scratch);

        var ex = await Assert.ThrowsAsync<ModelSourceNotFoundException>(() => reExporter.ReExportAsync());

        Assert.Contains("invented failure", ex.Message);
    }

    [Fact]
    public async Task No_active_thconfig_exports_nothing()
    {
        var therion = new FakeTherion(writesModel: true);
        Assert.Null(await new BlenderModelReExporter(() => null, therion, _scratch).ReExportAsync());
        Assert.Null(therion.Entry);
    }

    [Fact]
    public async Task Old_export_folders_are_pruned()
    {
        var old = Path.Combine(_scratch, "old");
        Directory.CreateDirectory(old);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));

        await new BlenderModelReExporter(() => Config, new FakeTherion(writesModel: true), _scratch).ReExportAsync();

        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public async Task Provider_reexports_when_the_build_output_is_older_than_the_sources()
    {
        var lox = Path.Combine(_project, "cave.lox");
        File.WriteAllText(lox, "LOX");
        File.SetLastWriteTimeUtc(lox, DateTime.UtcNow.AddHours(-1));
        var provider = new BlenderSourceProvider(() => _project, new OutputArtifactCollector(),
            reExporter: new BlenderModelReExporter(() => Config, new FakeTherion(writesModel: true), _scratch),
            sourcesModifiedUtc: () => BlenderSourceProvider.NewestSource([Config, Path.Combine(_project, "cave.th")]));

        var source = await provider.AcquireAsync(ModelSourceRequest.ForWorkspace(allowReExport: true));

        Assert.Equal(ModelSourceKind.ReExported, source.Model.Kind);
        Assert.StartsWith(Path.GetFullPath(_scratch), source.Model.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_keeps_a_build_output_newer_than_the_sources()
    {
        var lox = Path.Combine(_project, "cave.lox");
        File.WriteAllText(lox, "LOX");
        File.SetLastWriteTimeUtc(lox, DateTime.UtcNow.AddHours(1));
        var therion = new FakeTherion(writesModel: true);
        var provider = new BlenderSourceProvider(() => _project, new OutputArtifactCollector(),
            reExporter: new BlenderModelReExporter(() => Config, therion, _scratch),
            sourcesModifiedUtc: () => BlenderSourceProvider.NewestSource([Config]));

        var source = await provider.AcquireAsync(ModelSourceRequest.ForWorkspace(allowReExport: true));

        Assert.Equal(ModelSourceKind.WorkspaceArtifact, source.Model.Kind);
        Assert.Null(therion.Entry);
    }

    [Fact]
    public void NewestSource_skips_missing_files()
    {
        Assert.Null(BlenderSourceProvider.NewestSource([Path.Combine(_project, "missing.th")]));
        Assert.NotNull(BlenderSourceProvider.NewestSource([Config, Path.Combine(_project, "missing.th")]));
    }
}
