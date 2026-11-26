// `therion-cli render-worker` — renders the render bundles that arrive in a folder, one at a time,
// and leaves what they render in another: for a machine with Blender (a GPU) that has nothing else
// of ThIDE. How the folders are filled (a synced share, an SFTP drop) is not its business. A bundle
// is data: its own player and scripts are never run (see RenderWorker).

using Therion.Blender.Execution;
using Therion.Blender.Player;

namespace Therion.Cli;

public static class RenderWorkerVerb
{
    /// <summary>Runs the verb; <paramref name="args"/> is the full argv (args[0] == "render-worker").
    /// Returns 0 when it stopped as asked, 2 for a usage error.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        string? inbox = Option(args, "--inbox"), outbox = Option(args, "--outbox");
        if (inbox is null || outbox is null)
        {
            stderr.WriteLine("error: 'render-worker' needs --inbox <dir> and --outbox <dir>.");
            return 2;
        }
        if (!Directory.Exists(inbox))
        {
            stderr.WriteLine($"error: the inbox is not a folder: {inbox}");
            return 2;
        }
        var limits = new RenderBundleLimits();
        var maxTime = TimeSpan.FromHours(72);
        var poll = TimeSpan.FromSeconds(5);
        if (Number(args, "--max-hours", stderr, out double? hours) is false) return 2;
        if (hours is { } h) maxTime = TimeSpan.FromHours(h);
        if (Number(args, "--max-triangles", stderr, out double? triangles) is false) return 2;
        if (triangles is { } t) limits = limits with { MaxTriangles = (long)t };
        if (Number(args, "--max-bundle-gb", stderr, out double? gb) is false) return 2;
        if (gb is { } g) limits = limits with { MaxZipBytes = (long)(g * 1024 * 1024 * 1024) };
        if (Number(args, "--poll", stderr, out double? seconds) is false) return 2;
        if (seconds is { } s) poll = TimeSpan.FromSeconds(s);
        var modules = new List<string>();
        for (int i = 1; i < args.Count - 1; i++)
            if (args[i] == "--modules") modules.Add(Path.GetFullPath(args[i + 1]));

        var options = new RenderWorkerOptions
        {
            Inbox = Path.GetFullPath(inbox),
            Outbox = Path.GetFullPath(outbox),
            Scratch = Path.GetFullPath(Option(args, "--scratch") ?? Path.Combine(Path.GetTempPath(), "thide-render-worker")),
            Limits = limits,
            MaxTime = maxTime,
            ModuleFolders = modules,
            Poll = poll,
        };
        string? blender = Option(args, "--blender");
        var worker = new RenderWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), options,
            () => blender, line => stderr.WriteLine(line));
        bool once = args.Contains("--once");
        stderr.WriteLine($"render worker: {options.Inbox} -> {options.Outbox}{(once ? " (until the inbox is empty)" : "")}");
        int taken = await worker.RunAsync(once, ct).ConfigureAwait(false);
        stdout.WriteLine($"{taken} job(s)");
        return 0;
    }

    private static bool? Number(IReadOnlyList<string> args, string name, TextWriter stderr, out double? value)
    {
        value = null;
        if (Option(args, name) is not { } text) return null;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0)
        {
            value = v;
            return true;
        }
        stderr.WriteLine($"error: {name} takes a number above 0, not '{text}'.");
        return false;
    }

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (int i = 1; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
