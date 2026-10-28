// `therion-cli build` — compile one thconfig, or every thconfig under a folder, headless, with a
// summary (or JSON) at the end. Exit code 0 only when every build succeeded.

using System.Text.Json;
using Therion.Build;
using Therion.Build.Automation;
using Therion.Processing.Abstractions;
using Therion.Workspace;

namespace Therion.Cli;

public static class BuildVerb
{
    private sealed class FixedTool(string path) : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ToolInfo?>(new ToolInfo(toolId, path, null, "command line"));
    }

    /// <summary>
    /// Runs the verb. <paramref name="configure"/> adjusts the Therion process (tests use it to stand
    /// in for Therion). Returns 0 all succeeded, 1 a build failed, 2 usage error.
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr,
        IExternalToolLocator? locator = null, Func<System.Diagnostics.ProcessStartInfo, System.Diagnostics.ProcessStartInfo>? configure = null,
        CancellationToken ct = default)
    {
        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            stderr.WriteLine("error: 'build' needs a thconfig or a folder.");
            return 2;
        }
        string target = args[1];
        bool all = false, quiet = false, json = false, stopOnFailure = false, noLog = false;
        string? therion = null;
        for (int i = 2; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--all": all = true; break;
                case "--quiet": quiet = true; break;
                case "--json": json = true; break;
                case "--stop-on-failure": stopOnFailure = true; break;
                case "--no-log": noLog = true; break;
                case "--therion" when i + 1 < args.Count: therion = args[++i]; break;
                default: stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2;
            }
        }

        IReadOnlyList<string> entries;
        if (Directory.Exists(target))
        {
            if (!all) { stderr.WriteLine("error: a folder needs --all (build every thconfig under it)."); return 2; }
            entries = ThconfigDiscovery.Scan(target, new ThconfigSniffer());
            if (entries.Count == 0) { stderr.WriteLine($"error: no thconfig under {target}."); return 2; }
        }
        else if (File.Exists(target)) entries = [Path.GetFullPath(target)];
        else { stderr.WriteLine($"error: not found: {target}"); return 2; }

        var compiler = new TherionCompiler(locator ?? (therion is null ? new ExternalToolLocator() : new FixedTool(therion)));
        var options = new TherionCompileOptions { Quiet = quiet, LogFile = noLog ? null : TherionCompiler.DefaultLogFileName, ConfigureProcess = configure };
        var outcomes = new List<BuildOutcome>();
        foreach (var entry in entries)
        {
            stderr.WriteLine($"build: {entry}");
            var lines = new SyncProgress<CompilerOutputLine>(l => { if (!quiet) stderr.WriteLine("  " + l.Text); });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CompileResult result;
            try { result = await compiler.CompileAsync(entry, options, lines, ct).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                outcomes.Add(new BuildOutcome(entry, false, true, -1, 0, 0, [], null, clock.Elapsed));
                break;
            }
            var outcome = new BuildOutcome(entry, result.ExitCode == 0, false, result.ExitCode,
                result.Diagnostics.Count(d => d.Severity == Therion.Core.DiagnosticSeverity.Error),
                result.Diagnostics.Count(d => d.Severity == Therion.Core.DiagnosticSeverity.Warning),
                result.Artifacts.Select(a => a.Path).ToList(), result.LogPath, clock.Elapsed);
            outcomes.Add(outcome);
            if (!outcome.Succeeded && stopOnFailure) break;
        }

        var root = Directory.Exists(target) ? Path.GetFullPath(target) : null;
        if (json)
            stdout.WriteLine(JsonSerializer.Serialize(outcomes.Select(o => new
            {
                entry = o.Entry, exitCode = o.ExitCode, succeeded = o.Succeeded, errors = o.Errors, warnings = o.Warnings,
                artifacts = o.Artifacts, logPath = o.LogPath, elapsedMs = (long)o.Elapsed.TotalMilliseconds,
            }), new JsonSerializerOptions { WriteIndented = true }));
        else
            foreach (var line in BatchSummary.Lines(outcomes, root)) stdout.WriteLine(line);

        return outcomes.Count == entries.Count && outcomes.All(o => o.Succeeded) ? 0 : 1;
    }
}
