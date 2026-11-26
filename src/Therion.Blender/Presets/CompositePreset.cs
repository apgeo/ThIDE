// Composite presets: a whole film as a reusable template. A composite is a storyboard that names no
// model and no station of one: its routes are symbolic (the entrance, the farthest station, the
// passage surveyed last, a tour of the branches) and its texts carry placeholders ({cave}). It is
// made into a film of a given cave where the model is read (Plan/StoryboardExpander), and is kept
// beside the user presets (PresetStore) as one versioned JSON file: a name, a description and the
// storyboard as a storyboard's file has it.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Blender.Presets;

/// <summary>A named storyboard template (built-in or the user's): see the file header.</summary>
public sealed record CompositePreset
{
    public int Version { get; init; } = BlenderModule.PresetSchemaVersion;

    /// <summary>Display name (also the basis for the on-disk file slug).</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>True for the shipped ones (read-only in the UI).</summary>
    public bool BuiltIn { get; init; }

    /// <summary>The film. Its output folder is for whoever renders it.</summary>
    public required Storyboard Storyboard { get; init; }
}

/// <summary>Reads and writes <see cref="CompositePreset"/>s as JSON.</summary>
public static class CompositePresetSerializer
{
    public static string Write(CompositePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var root = new JsonObject
        {
            ["version"] = preset.Version,
            ["name"] = preset.Name,
        };
        if (preset.Description is not null) root["description"] = preset.Description;
        root["storyboard"] = JsonNode.Parse(StoryboardSerializer.Write(preset.Storyboard));
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .ReplaceLineEndings("\n") + "\n";
    }

    public static void WriteFile(CompositePreset preset, string path)
        => File.WriteAllText(path, Write(preset), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <exception cref="PresetFormatException">The text is not a composite preset; the message
    /// quotes nothing of it beyond the key at fault.</exception>
    public static CompositePreset Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new PresetFormatException("A composite preset is a JSON object.");
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("version" or "name" or "description" or "storyboard"))
                    throw new PresetFormatException($"A composite preset has no key '{property.Name}'.");
            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int v))
                throw new PresetFormatException("A composite preset's \"version\" is an integer.");
            if (v > BlenderModule.PresetSchemaVersion || v < 1)
                throw new PresetFormatException($"This composite preset is version {v}; this build understands {BlenderModule.PresetSchemaVersion}.");
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                throw new PresetFormatException("A composite preset has a name.");
            string? description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            if (!root.TryGetProperty("storyboard", out var storyboard) || storyboard.ValueKind != JsonValueKind.Object)
                throw new PresetFormatException("A composite preset has a storyboard.");
            Storyboard film;
            try { film = StoryboardSerializer.Read(storyboard.GetRawText()); }
            catch (StoryboardFormatException ex) { throw new PresetFormatException("storyboard: " + ex.Message); }
            return new CompositePreset { Version = v, Name = name.GetString()!, Description = description, Storyboard = film };
        }
        catch (JsonException ex)
        {
            throw new PresetFormatException($"Malformed composite preset JSON: {JsonFault.NotJson(ex)}");
        }
    }

    public static CompositePreset ReadFile(string path) => Read(File.ReadAllText(path));
}

/// <summary>The composite presets that ship: films any cave can be made into.</summary>
public static class BuiltInComposites
{
    // Before All, which is made from it.
    private static readonly OutputSpec Video = new() { Kind = OutputKind.Video, BaseName = "cave-film" };

    public static IReadOnlyList<CompositePreset> All { get; } = [Showcase(), GrandTour(), ExplorationHistory()];

    public static CompositePreset? ByName(string name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Round the cave from outside, along its main passage, and down round it.</summary>
    public static CompositePreset Showcase() => new()
    {
        Name = "Showcase",
        Description = "Orbit, then the main passage from the entrance to the farthest station, then a helix down.",
        BuiltIn = true,
        Storyboard = new Storyboard
        {
            Name = "{cave}: showcase",
            Texts = new OverlaySpec { Title = "{cave}", TitleSeconds = 3 },
            Shots =
            [
                new ShotSpec { Name = "Outside", Preset = "Orbit showcase", Seconds = 12 },
                new ShotSpec
                {
                    Name = "Main passage", Preset = "Full flythrough", Seconds = 21,
                    In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 1 },
                    Camera = new CameraSpec { Template = CameraTemplate.Route, Route = new RouteParams() },
                },
                new ShotSpec { Name = "Way out", Preset = "Helix descent", Seconds = 11, In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.6 } },
            ],
            Output = Video with { BaseName = "cave-showcase" },
        },
    };

    /// <summary>Round the cave, then a tour of its main passage and the side passages worth a
    /// visit, and an end card.</summary>
    public static CompositePreset GrandTour() => new()
    {
        Name = "Grand tour",
        Description = "Orbit, then a tour of the main passage and its side passages (at most two minutes), then an end card.",
        BuiltIn = true,
        Storyboard = new Storyboard
        {
            Name = "{cave}: grand tour",
            Texts = new OverlaySpec { Title = "{cave}", TitleSeconds = 3, EndCard = new EndCardSpec { Text = "{cave}" } },
            Shots =
            [
                new ShotSpec { Name = "Outside", Preset = "Orbit showcase", Seconds = 10 },
                new ShotSpec
                {
                    Name = "Tour", Preset = "Full flythrough", Seconds = 120,
                    In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.6 },
                    Camera = new CameraSpec { Template = CameraTemplate.Route, Route = new RouteParams { Branches = RouteParams.AutoBranches } },
                },
            ],
            Output = Video with { BaseName = "cave-grand-tour" },
        },
    };

    /// <summary>The cave appearing in the order it was surveyed, with the years counted, then a
    /// flight along the passage found last.</summary>
    public static CompositePreset ExplorationHistory() => new()
    {
        Name = "Exploration history",
        Description = "The map revealed in the order of the survey dates, with a counter of the years, then a flight along the passage surveyed last.",
        BuiltIn = true,
        Storyboard = new Storyboard
        {
            Name = "{cave}: exploration history",
            Texts = new OverlaySpec { Title = "{cave}", TitleSeconds = 3 },
            Shots =
            [
                new ShotSpec
                {
                    Name = "History", Preset = "Orbit showcase", Seconds = 16,
                    Reveal = ShotReveal.Of(new RevealSpec { By = RevealBy.Date, Counter = true, End = 14 }),
                },
                new ShotSpec
                {
                    Name = "Found last", Preset = "Full flythrough", Seconds = 14, Reveal = ShotReveal.Hold,
                    In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 1 },
                    Camera = new CameraSpec { Template = CameraTemplate.Route, Route = new RouteParams { From = RouteParams.Newest } },
                },
            ],
            Output = Video with { BaseName = "cave-history" },
        },
    };
}
