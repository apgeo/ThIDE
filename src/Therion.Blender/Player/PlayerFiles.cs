// The player's Python, as shipped in this assembly. The files live beside this one
// (thide_player/, render.py, single_file.py) and are embedded as they are; nothing here runs them.

using System.Reflection;
using System.Text.RegularExpressions;

namespace Therion.Blender.Player;

/// <summary>One file of the player: its path relative to the job folder, with forward slashes,
/// and its text with LF line endings.</summary>
public sealed record PlayerFile(string Path, string Text);

/// <summary>The Python player that builds a Blender scene from a scene plan.</summary>
public static partial class PlayerFiles
{
    /// <summary>The script Blender is started with; it plays the plan that lies beside it.</summary>
    public const string EntryFileName = "render.py";

    /// <summary>The player's package folder.</summary>
    public const string PackageName = "thide_player";

    private const string ResourcePrefix = "player/";

    private static readonly Lazy<IReadOnlyList<PlayerFile>> Loaded = new(() =>
    {
        var assembly = typeof(PlayerFiles).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => new PlayerFile(n[ResourcePrefix.Length..].Replace('\\', '/'), Read(assembly, n)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    });

    private static IEnumerable<PlayerFile> Package
        => Loaded.Value.Where(f => f.Path.StartsWith(PackageName + "/", StringComparison.Ordinal));

    /// <summary>The entry script and the package: what a job folder gets.</summary>
    public static IReadOnlyList<PlayerFile> JobFiles
        => Loaded.Value.Where(f => f.Path == EntryFileName).Concat(Package).ToList();

    /// <summary>The package's files under the names Python imports them by
    /// (<c>thide_player.modules.look</c>), for a script that carries them inside itself.</summary>
    internal static IReadOnlyList<(string Module, string Text)> PackageModules
        => Package.Select(f => (ModuleName(f.Path), f.Text)).OrderBy(m => m.Item1, StringComparer.Ordinal).ToList();

    /// <summary>The end of a single-file script: it loads the modules from the script itself.</summary>
    internal static string SingleFileStart => Loaded.Value.Single(f => f.Path == "single_file.py").Text;

    /// <summary>The version the player reports (<c>THIDE:player=</c>).</summary>
    public static string Version => Constant("PLAYER_VERSION", "\"([^\"]+)\"");

    /// <summary>The newest plan version the player reads; equal to
    /// <see cref="BlenderModule.ScenePlanSchemaVersion"/>.</summary>
    public static int PlanVersion => int.Parse(Constant("PLAN_VERSION", "(\\d+)"), System.Globalization.CultureInfo.InvariantCulture);

    private static string Constant(string name, string valuePattern)
    {
        string init = Loaded.Value.Single(f => f.Path == PackageName + "/__init__.py").Text;
        var match = Regex.Match(init, "^" + name + " = " + valuePattern + "$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"The player does not define {name}.");
    }

    private static string ModuleName(string path)
    {
        string name = path[..^".py".Length].Replace('/', '.');
        return name.EndsWith(".__init__", StringComparison.Ordinal) ? name[..^".__init__".Length] : name;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
