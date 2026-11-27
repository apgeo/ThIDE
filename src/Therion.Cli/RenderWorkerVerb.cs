// `therion-cli render-worker` — renders the render bundles that arrive in a folder, one at a time,
// and leaves what they render in another: for a machine with Blender (a GPU) that has nothing else
// of ThIDE. How the folders are filled (a synced share, an SFTP drop) is not its business. With
// --server and --token-file it takes its jobs from a server instead (a render node of a server such
// as SilexGIS; see RenderNodeWorker). A bundle is data: its own player and scripts are never run.

using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Player;

namespace Therion.Cli;

public static class RenderWorkerVerb
{
    /// <summary>Runs the verb; <paramref name="args"/> is the full argv (args[0] == "render-worker").
    /// Returns 0 when it stopped as asked, 2 for a usage error, 3 when the server refused the node token.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        if (Option(args, "--server") is not null || Option(args, "--token-file") is not null)
            return await ServerAsync(args, stdout, stderr, ct).ConfigureAwait(false);
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
        if (!Shared(args, stderr, out var limits, out var maxTime, out var modules)) return 2;
        var poll = TimeSpan.FromSeconds(5);
        if (Number(args, "--poll", stderr, out double? seconds) is false) return 2;
        if (seconds is { } s) poll = TimeSpan.FromSeconds(s);

        var options = new RenderWorkerOptions
        {
            Inbox = Path.GetFullPath(inbox),
            Outbox = Path.GetFullPath(outbox),
            Scratch = Scratch(args, "render-worker"),
            Limits = limits,
            MaxTime = maxTime,
            ModuleFolders = modules,
            Poll = poll,
        };
        if (!Private(args, options.Scratch, stderr)) return 2;
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

    /// <summary>The limits, the time limit and the module folders both modes take.</summary>
    private static bool Shared(IReadOnlyList<string> args, TextWriter stderr, out RenderBundleLimits limits, out TimeSpan maxTime, out List<string> modules)
    {
        limits = new RenderBundleLimits();
        maxTime = TimeSpan.FromHours(72);
        modules = [];
        if (Number(args, "--max-hours", stderr, out double? hours) is false) return false;
        if (hours is { } h)
        {
            if (h > RenderWorkerOptions.LongestMaxTime.TotalHours)
            {
                stderr.WriteLine($"error: --max-hours takes at most {RenderWorkerOptions.LongestMaxTime.TotalHours:0} (30 days), not '{h}'.");
                return false;
            }
            maxTime = TimeSpan.FromHours(h);
        }
        if (Number(args, "--max-triangles", stderr, out double? triangles) is false) return false;
        if (triangles is { } t) limits = limits with { MaxTriangles = (long)t };
        if (Number(args, "--max-bundle-gb", stderr, out double? gb) is false) return false;
        if (gb is { } g) limits = limits with { MaxZipBytes = (long)(g * 1024 * 1024 * 1024) };
        for (int i = 1; i < args.Count - 1; i++)
            if (args[i] == "--modules") modules.Add(Path.GetFullPath(args[i + 1]));
        return true;
    }

    /// <summary>The server mode: claims jobs from <c>--server</c> with the token of <c>--token-file</c>.</summary>
    private static async Task<int> ServerAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        string? server = Option(args, "--server"), tokenFile = Option(args, "--token-file");
        if (server is null || tokenFile is null)
        {
            stderr.WriteLine("error: 'render-worker' needs both --server <url> and --token-file <file> to take jobs from a server.");
            return 2;
        }
        string? problem = "it is not an absolute URL";
        if (!Uri.TryCreate(server, UriKind.Absolute, out var url) || !RenderNodeProtocol.IsAllowedAddress(url, out problem))
        {
            stderr.WriteLine($"error: --server cannot be used: {problem}.");
            return 2;
        }
        if (!RenderNodeToken.TryRead(tokenFile, out string token, out string? tokenProblem))
        {
            stderr.WriteLine($"error: --token-file: {tokenProblem}.");
            return 2;
        }
        if (!Shared(args, stderr, out var limits, out var maxTime, out var modules)) return 2;
        if (Number(args, "--poll-seconds", stderr, out double? seconds) is false) return 2;
        var options = new RenderNodeOptions
        {
            Server = url,
            Token = token,
            NodeName = Option(args, "--node-name") ?? Environment.MachineName,
            Scratch = Scratch(args, "render-node"),
            Limits = limits,
            MaxTime = maxTime,
            ModuleFolders = modules,
            Poll = TimeSpan.FromSeconds(seconds ?? 30),
            WorkerVersion = typeof(RenderWorkerVerb).Assembly.GetName().Version?.ToString(),
        };
        if (!Private(args, options.Scratch, stderr)) return 2;
        string? blender = Option(args, "--blender");
        using var worker = new RenderNodeWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), options,
            () => blender, line => stderr.WriteLine(line));
        var run = await worker.RunAsync(args.Contains("--once"), ct).ConfigureAwait(false);
        stdout.WriteLine($"{run.Jobs} job(s)");
        return run.Unauthorized ? 3 : 0;
    }

    /// <summary>The scratch folder: <c>--scratch</c>, or by default a folder of the user's own
    /// cache folder (never a fixed name in the temporary folder every user shares).</summary>
    private static string Scratch(IReadOnlyList<string> args, string name)
        => Option(args, "--scratch") is { } scratch ? Path.GetFullPath(scratch) : PrivateFolders.Of(name);

    /// <summary>Makes the scratch folder the user's alone, or says why it cannot be used. The
    /// default folder is ThIDE's own and is made private; one the operator names is checked only.</summary>
    private static bool Private(IReadOnlyList<string> args, string scratch, TextWriter stderr)
    {
        try
        {
            PrivateFolders.Ensure(scratch, tighten: Option(args, "--scratch") is null);
            return true;
        }
        catch (UnsafeFolderException ex)
        {
            stderr.WriteLine($"error: the scratch folder cannot be used: {ex.Message}");
            return false;
        }
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
