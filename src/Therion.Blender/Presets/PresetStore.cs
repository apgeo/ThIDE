// User preset CRUD (BA-B9, FR-12). Stores each user preset as one versioned JSON file in a
// directory (filename = a slug of the name; saving the same name overwrites = update).
// Built-in presets are code (BuiltInPresets), never stored here. File I/O lives in this
// service, not in the data types (project rule). Cross-platform: Path.Combine, invariant.

namespace Therion.Blender.Presets;

/// <summary>Reads, writes and deletes user render presets under a directory.</summary>
public sealed class PresetStore
{
    /// <summary>Extension for preset files.</summary>
    public const string Extension = ".preset.json";

    /// <summary>The directory user presets live in (created on first save).</summary>
    public string Directory { get; }

    public PresetStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>Every readable user preset, sorted by name (ordinal). Files that fail to
    /// parse are skipped, not fatal — one bad file must not hide the rest (NFR-05).</summary>
    public IReadOnlyList<RenderPreset> Load()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var presets = new List<RenderPreset>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            try { presets.Add(PresetSerializer.ReadFile(path) with { BuiltIn = false }); }
            catch (Exception ex) when (ex is PresetFormatException or IOException or System.Text.Json.JsonException)
            {
                // Skip unreadable/foreign files; the UI surfaces the rest.
            }
        }
        presets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return presets;
    }

    /// <summary>Saves (or overwrites) a preset. Returns the file path written. The preset is
    /// stored as a user preset (<see cref="RenderPreset.BuiltIn"/> forced false).</summary>
    public string Save(RenderPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new ArgumentException("A preset needs a name to be saved.", nameof(preset));
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(preset.Name);
        PresetSerializer.WriteFile(preset with { BuiltIn = false }, path);
        return path;
    }

    /// <summary>Deletes the preset with this name; returns true if a file was removed.</summary>
    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var path = PathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>True when a user preset with this name exists.</summary>
    public bool Exists(string name) => File.Exists(PathFor(Guard(name)));

    private string PathFor(string name) => Path.Combine(Directory, Slug(name) + Extension);

    private static string Guard(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <summary>A filesystem-safe, stable slug for a preset name: lowercase, ASCII
    /// alphanumerics kept, every other run collapsed to a single dash.</summary>
    internal static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        bool lastDash = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "preset" : slug;
    }
}
