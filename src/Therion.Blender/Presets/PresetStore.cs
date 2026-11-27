// User preset CRUD (BA-B9, FR-12). Stores each user preset as one versioned JSON file in a
// directory (filename = a slug of the name; saving the same name overwrites = update; two names
// of one slug get two files, the file of a name is found by the name it holds).
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
    /// parse or cannot be read are skipped, not fatal — one bad file must not hide the rest
    /// (NFR-05); a folder that cannot be listed has none.</summary>
    public IReadOnlyList<RenderPreset> Load()
    {
        var presets = new List<RenderPreset>();
        foreach (var path in Files(Extension))
        {
            try { presets.Add(PresetSerializer.ReadFile(path) with { BuiltIn = false }); }
            catch (Exception ex) when (Unreadable(ex))
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
        var path = PathFor(preset.Name, Extension, PresetName);
        PresetSerializer.WriteFile(preset with { BuiltIn = false }, path);
        return path;
    }

    /// <summary>Deletes the preset with this name; returns true if a file was removed.</summary>
    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (FileOf(name, Extension, PresetName) is not { } path) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Extension for composite preset files (a storyboard template).</summary>
    public const string CompositeExtension = ".composite.json";

    /// <summary>Every readable composite preset of the user's, sorted by name; unreadable files
    /// are skipped, as for <see cref="Load"/>.</summary>
    public IReadOnlyList<CompositePreset> LoadComposites()
    {
        var composites = new List<CompositePreset>();
        foreach (var path in Files(CompositeExtension))
        {
            try { composites.Add(CompositePresetSerializer.ReadFile(path) with { BuiltIn = false }); }
            catch (Exception ex) when (Unreadable(ex)) { }
        }
        composites.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return composites;
    }

    /// <summary>Saves (or overwrites) a composite preset; returns the file written.</summary>
    public string SaveComposite(CompositePreset composite)
    {
        ArgumentNullException.ThrowIfNull(composite);
        if (string.IsNullOrWhiteSpace(composite.Name))
            throw new ArgumentException("A composite preset needs a name to be saved.", nameof(composite));
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(composite.Name, CompositeExtension, CompositeName);
        CompositePresetSerializer.WriteFile(composite with { BuiltIn = false }, path);
        return path;
    }

    /// <summary>Deletes the composite preset with this name; true if a file was removed.</summary>
    public bool DeleteComposite(string name)
    {
        if (FileOf(Guard(name), CompositeExtension, CompositeName) is not { } path) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>True when a user preset with this name exists.</summary>
    public bool Exists(string name) => FileOf(Guard(name), Extension, PresetName) is not null;

    /// <summary>What is wrong with <paramref name="name"/> as the name of a user's preset (or,
    /// with <paramref name="composite"/>, of a user's composite preset); null when nothing is. A
    /// built-in one's name, in any case, is taken: the built-in would be found first by it.</summary>
    public static PresetNameProblem? NameProblem(string? name, bool composite = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return PresetNameProblem.Empty;
        string trimmed = name.Trim();
        bool builtIn = composite ? BuiltInComposites.ByName(trimmed) is not null : BuiltInPresets.ByName(trimmed) is not null;
        return builtIn ? PresetNameProblem.BuiltIn : null;
    }

    private static string PresetName(string path) => PresetSerializer.ReadFile(path).Name;

    private static string CompositeName(string path) => CompositePresetSerializer.ReadFile(path).Name;

    /// <summary>The file that holds the preset of this very name, else a new one named after it.
    /// Two names whose slugs are one ("My preset" and "my-preset") get two files: the second is
    /// told apart by a short hash of its name.</summary>
    private string PathFor(string name, string extension, Func<string, string> nameIn)
    {
        if (FileOf(name, extension, nameIn) is { } kept) return kept;
        string slug = Slug(name), path = Path.Combine(Directory, slug + extension);
        return File.Exists(path) ? Path.Combine(Directory, slug + "-" + Hash(name) + extension) : path;
    }

    /// <summary>The file that holds the preset of this very name (ordinal), whatever it is
    /// called: a file saved before the slug folded diacritics keeps its old name.</summary>
    private string? FileOf(string name, string extension, Func<string, string> nameIn)
    {
        string slug = Slug(name);
        // The file named after it first, then the rest.
        var files = Files(extension).OrderBy(f => Path.GetFileName(f).StartsWith(slug, StringComparison.Ordinal) ? 0 : 1);
        foreach (string path in files)
        {
            try
            {
                if (string.Equals(nameIn(path), name, StringComparison.Ordinal)) return path;
            }
            catch (Exception ex) when (Unreadable(ex)) { }
        }
        return null;
    }

    /// <summary>The files of a kind in the folder; none when it is not there or cannot be listed.</summary>
    private List<string> Files(string extension)
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? [.. System.IO.Directory.EnumerateFiles(Directory, "*" + extension)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool Unreadable(Exception ex)
        => ex is PresetFormatException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException;

    private static string Hash(string name)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();

    private static string Guard(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <summary>A filesystem-safe, stable slug for a preset name: lowercase, letters without
    /// their diacritics (ș is s), ASCII alphanumerics kept, every other run collapsed to a
    /// single dash.</summary>
    internal static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        bool lastDash = false;
        string folded = new([.. name.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);
        foreach (char c in folded)
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

/// <summary>Why a name cannot be a user preset's.</summary>
public enum PresetNameProblem
{
    /// <summary>It is blank.</summary>
    Empty,

    /// <summary>A built-in preset (or composite) has it, in some case.</summary>
    BuiltIn,
}
