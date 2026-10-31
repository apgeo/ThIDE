// The vendored CaveView.js runtime and the stand-alone model page, read from embedded resources.
using System.Reflection;

namespace Therion.CaveView;

/// <summary>
/// The vendored CaveView.js runtime (the viewer bundle, its workers, stylesheet, logo, the
/// WebGL detector and the interface catalogues) and the files of the stand-alone model page,
/// read from this assembly's embedded resources. Paths are relative and forward-slashed,
/// e.g. <c>js/CaveView2.min.js</c>; the runtime is meant to be served as the viewer's
/// <c>home</c> directory, so the bundle finds its workers and catalogues under it.
/// </summary>
public static class CaveViewRuntime
{
    /// <summary>The distribution version of the vendored bundle (<c>CV2.VERSION</c>).</summary>
    public const string Version = "2.9.0-slx.22";

    /// <summary>The interface languages the runtime carries a catalogue for (English is built in).</summary>
    public static IReadOnlyList<string> Languages { get; } = new[] { "en", "ro", "it", "sl" };

    private const string RuntimePrefix = "CaveView/";
    private const string PagePrefix = "Page/";

    private static readonly Assembly Self = typeof(CaveViewRuntime).Assembly;

    // Resource names keyed by their normalised relative path. MSBuild's %(RecursiveDir) is
    // written with the build host's separator, so a Windows build names "CaveView/js\x.js".
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Resources = new(() =>
        Self.GetManifestResourceNames().ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal));

    /// <summary>Every runtime file, as a path relative to the runtime's home directory.</summary>
    public static IReadOnlyList<string> Files => List(RuntimePrefix);

    /// <summary>Every file of the stand-alone model page, as a path relative to the page.</summary>
    public static IReadOnlyList<string> PageFiles => List(PagePrefix);

    /// <summary>True when the runtime's viewer bundle is present.</summary>
    public static bool IsAvailable => Resources.Value.ContainsKey(RuntimePrefix + "js/CaveView2.min.js");

    /// <summary>Opens a runtime file by its relative path, or returns null for one the runtime does not have.</summary>
    public static Stream? Open(string relativePath) => OpenResource(RuntimePrefix, relativePath);

    /// <summary>Opens a file of the stand-alone page by its relative path, or returns null.</summary>
    public static Stream? OpenPageFile(string relativePath) => OpenResource(PagePrefix, relativePath);

    /// <summary>Reads a file of the stand-alone page as UTF-8 text, or returns null.</summary>
    public static string? ReadPageText(string relativePath)
    {
        using var s = OpenPageFile(relativePath);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static IReadOnlyList<string> List(string prefix) =>
        Resources.Value.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    private static Stream? OpenResource(string prefix, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var key = prefix + relativePath.Replace('\\', '/').TrimStart('/');
        return Resources.Value.TryGetValue(key, out var name) ? Self.GetManifestResourceStream(name) : null;
    }
}
