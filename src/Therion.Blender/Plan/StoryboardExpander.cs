// A storyboard made into a film of one cave. A composite preset (Presets/CompositePreset) names no
// station: its routes are symbols, which only the model can make into stations. Here, where the
// model's geometry is at hand (the render service and the bundle writer, after the conversion and
// before the lowering), a shot whose route has "branches": "auto" becomes the shots of a tour of
// the cave's branches (TourBuilder), and a route from "newest" flies the passage surveyed last.
// What could not be done as asked is said in the notices.
//
// Beside it, presets inlined: a storyboard that names a user's preset is written with what the
// preset says in its shots, so that it renders the same wherever only the built-in presets are
// known (a render queue that starts it again after a restart).

using System.Globalization;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>A storyboard made into one cave's film, with the tours made for it.</summary>
public sealed record ExpandedStoryboard
{
    public required Storyboard Storyboard { get; init; }

    /// <summary>The tours that took the place of a shot, in the order of the shots.</summary>
    public IReadOnlyList<Tour> Tours { get; init; } = [];

    /// <summary>What the tours left out or cut short, and what was done for a symbol.</summary>
    public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>Makes a storyboard's symbols into a cave's stations: see the file header.</summary>
public static class StoryboardExpander
{
    /// <summary>How long a tour may be when its shot does not say (seconds).</summary>
    public const double DefaultTourSeconds = 180.0;

    /// <summary>True when a shot of <paramref name="storyboard"/> has a route only the model can
    /// make into stations.</summary>
    public static bool IsSymbolic(Storyboard storyboard, Func<string, RenderPreset?>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        presets ??= BuiltInPresets.ByName;
        return storyboard.Shots.Any(s => s is not null && Symbol(RouteOf(s, presets)) is not null);
    }

    /// <summary>
    /// <paramref name="storyboard"/> as a film of the cave of <paramref name="geometry"/>: every
    /// tour shot made into its tour's shots, every route from <see cref="RouteParams.Newest"/>
    /// into a route between two stations. A storyboard without symbols comes back as it is.
    /// </summary>
    /// <param name="language">The language of the texts a tour writes (<c>en</c>, <c>ro</c>) when
    /// the storyboard does not say one (<see cref="Storyboard.Language"/>).</param>
    /// <exception cref="ArgumentException">A tour cannot be made for this model; the message
    /// names the shot.</exception>
    public static ExpandedStoryboard Expand(
        Storyboard storyboard, GeometryResult geometry, IReadOnlyList<SourceLead>? leads = null,
        Func<string, RenderPreset?>? presets = null, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(geometry);
        presets ??= BuiltInPresets.ByName;
        if (!IsSymbolic(storyboard, presets)) return new ExpandedStoryboard { Storyboard = storyboard };
        language = storyboard.Language ?? language;

        var shots = new List<ShotSpec>();
        var tours = new List<Tour>();
        var notices = new List<string>();
        for (int i = 0; i < storyboard.Shots.Count; i++)
        {
            var shot = storyboard.Shots[i];
            var route = shot is null ? null : RouteOf(shot, presets);
            string name = string.IsNullOrWhiteSpace(shot?.Name) ? $"Shot {i + 1}" : shot.Name!;
            switch (Symbol(route))
            {
                case Kind.Tour:
                    var tour = TourOf(storyboard, shot!, route!, geometry, leads, presets, language, name, i);
                    var look = tour.Storyboard.Defaults;
                    var tourShots = tour.Storyboard.Shots;
                    for (int j = 0; j < tourShots.Count; j++)
                    {
                        var t = tourShots[j];
                        shots.Add(t with
                        {
                            Engine = t.Engine ?? shot!.Engine ?? look.Engine,
                            Materials = t.Materials ?? shot!.Materials ?? look.Materials,
                            Lighting = t.Lighting ?? shot!.Lighting ?? look.Lighting,
                            Labels = t.Labels ?? shot!.Labels ?? look.Labels,
                            // A tour shows the whole cave, or what the shot says of the reveal.
                            Reveal = t.Reveal ?? shot!.Reveal ?? (storyboard.Defaults.Reveal is null ? null : ShotReveal.None),
                            In = j == 0 ? shot!.In : t.In,
                            Texts = j == 0 ? t.Texts ?? shot!.Texts : t.Texts,
                        });
                    }
                    tours.Add(tour);
                    notices.AddRange(tour.Notices.Select(n => $"{name}: {n}"));
                    break;
                case Kind.Newest:
                    var (newest, notice) = Newest(geometry, route!);
                    notices.Add($"{name}: {notice}");
                    var camera = shot!.Camera ?? presets(shot.Preset!)!.Spec.Camera;
                    shots.Add(shot with { Camera = camera with { Template = CameraTemplate.Route, Route = newest } });
                    break;
                default:
                    shots.Add(shot!);
                    break;
            }
        }
        return new ExpandedStoryboard { Storyboard = storyboard with { Shots = shots }, Tours = tours, Notices = notices };
    }

    /// <summary>
    /// <paramref name="storyboard"/> with every preset it names that is not a built-in one written
    /// into its shots: the shot names no preset then, and has the preset's camera, look, labels in
    /// the scene, reveal and length where it said none of its own. It lowers to the same film.
    /// </summary>
    public static Storyboard InlinePresets(Storyboard storyboard, Func<string, RenderPreset?> presets)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(presets);
        var defaults = storyboard.Defaults;
        return storyboard with
        {
            Shots = storyboard.Shots.Select(shot =>
            {
                if (shot?.Preset is not { } name || BuiltInPresets.ByName(name) is not null || presets(name) is not { } preset) return shot!;
                return InlinePreset(shot, preset, defaults);
            }).ToList(),
        };
    }

    /// <summary>
    /// <paramref name="shot"/> with what <paramref name="preset"/> gives it written in, and no
    /// preset named: what the shot says itself stays. For a preset of the user's, which is known
    /// only where it was saved, or one whose name a built-in preset also has.
    /// </summary>
    public static ShotSpec InlinePreset(ShotSpec shot, RenderPreset preset, StoryboardDefaults? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(preset);
        var spec = preset.Spec;
        return shot with
        {
            Preset = null,
            Seconds = shot.Seconds ?? spec.Animation.DurationSeconds,
            Camera = shot.Camera ?? spec.Camera,
            Engine = shot.Engine ?? spec.Engine,
            Materials = shot.Materials ?? spec.Materials,
            Lighting = shot.Lighting ?? spec.Lighting,
            // The preset's labels in the scene, and the texts the shot had without them.
            Labels = shot.Labels ?? spec.Labels with { Overlays = new OverlaySpec() },
            Texts = shot.Texts ?? shot.Labels?.Overlays ?? defaults?.Labels?.Overlays ?? new OverlaySpec(),
            Reveal = shot.Reveal ?? (spec.Reveal is { } reveal ? ShotReveal.Of(reveal) : ShotReveal.None),
        };
    }

    private enum Kind { Tour, Newest }

    private static RouteParams? RouteOf(ShotSpec shot, Func<string, RenderPreset?> presets)
    {
        var camera = shot.Camera ?? (shot.Preset is { } name ? presets(name)?.Spec.Camera : null);
        return camera is { Template: CameraTemplate.Route } ? camera.Route : null;
    }

    private static Kind? Symbol(RouteParams? route)
        => route is null ? null
            : route.Branches is not null ? Kind.Tour
            : string.Equals(route.From, RouteParams.Newest, StringComparison.OrdinalIgnoreCase) ? Kind.Newest
            : null;

    /// <summary>
    /// The tour the shot <paramref name="index"/> of <paramref name="storyboard"/> stands for,
    /// as rules: what the expansion builds for it, and what a branch table is made from.
    /// </summary>
    /// <param name="language">The language when the storyboard does not say one.</param>
    /// <exception cref="ArgumentException">The shot is not a tour of the branches.</exception>
    public static TourSpec TourSpecOf(Storyboard storyboard, int index, Func<string, RenderPreset?>? presets = null, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        presets ??= BuiltInPresets.ByName;
        if (index < 0 || index >= storyboard.Shots.Count || storyboard.Shots[index] is not { } shot)
            throw new ArgumentException($"The storyboard has no shot {index + 1}.", nameof(index));
        var route = RouteOf(shot, presets);
        if (Symbol(route) != Kind.Tour)
            throw new ArgumentException($"shots[{index}] is not a tour of the cave's branches.", nameof(index));
        return TourSpecOf(storyboard, shot, route!, storyboard.Language ?? language, index);
    }

    private static TourSpec TourSpecOf(Storyboard storyboard, ShotSpec shot, RouteParams route, string language, int index)
    {
        if (!string.Equals(route.Branches, RouteParams.AutoBranches, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"shots[{index}].camera.route.branches: a route's branches are \"{RouteParams.AutoBranches}\" or not said.", nameof(storyboard));
        bool via = route.Via.Count > 0;
        return new TourSpec
        {
            Name = storyboard.Name,
            Start = route.From is null || string.Equals(route.From, RouteParams.Entrance, StringComparison.OrdinalIgnoreCase) ? null : route.From,
            Trunk = via ? TourTrunkKind.Via : TourTrunkKind.Auto,
            Via = route.Via,
            End = via ? route.To : null,
            Include = route.Include ?? [],
            Exclude = route.Exclude ?? [],
            Priority = route.Priority,
            TourSeconds = shot.Seconds ?? DefaultTourSeconds,
            Preset = shot.Preset ?? new TourSpec().Preset,
            Fps = storyboard.Fps,
            Language = TourBuilder.Languages.Contains(language, StringComparer.Ordinal) ? language : "en",
        };
    }

    private static Tour TourOf(
        Storyboard storyboard, ShotSpec shot, RouteParams route, GeometryResult geometry, IReadOnlyList<SourceLead>? leads,
        Func<string, RenderPreset?> presets, string language, string name, int index)
    {
        var spec = TourSpecOf(storyboard, shot, route, language, index);
        try { return TourBuilder.Build(geometry, spec, leads, presets); }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"{name}: {ex.Message.Split(" (Parameter", 2)[0]}", nameof(storyboard));
        }
    }

    /// <summary>The route along the passage surveyed last: between the two stations of the legs
    /// of the last survey day that lie farthest apart, from the one nearer an entrance. Without
    /// survey dates, the main passage.</summary>
    private static (RouteParams Route, string Notice) Newest(GeometryResult geometry, RouteParams route)
    {
        var model = geometry.RecenteredModel;
        var field = geometry.Reveal is { Source: RevealSource.Date } dated
            ? dated
            : RevealField.Build(model, new RevealOptions { Source = RevealSource.Date }, geometry.Offset);
        var main = route with { From = null, To = null };
        if (field is null || field.Legs.Count == 0)
            return (main, "the model has no survey dates: the shot flies the main passage.");

        double last = field.Max;
        var at = new Dictionary<CaveVector3, int>();
        for (int s = 0; s < model.Stations.Count; s++) at.TryAdd(model.Stations[s].Position, s);
        var stations = new List<int>();
        foreach (var leg in field.Legs)
        {
            if (Math.Max(leg.FromValue, leg.ToValue) < last - 1.0 / 366) continue;
            foreach (var end in new[] { leg.From, leg.To })
                if (at.TryGetValue(end, out int s) && !stations.Contains(s)) stations.Add(s);
        }
        if (stations.Count < 2)
            return (main, "the legs surveyed last are not on the model's stations: the shot flies the main passage.");

        // The two farthest apart; on a long list, every k-th is looked at.
        int step = Math.Max(1, stations.Count / 300);
        int a = stations[0], b = stations[1];
        double best = -1;
        for (int i = 0; i < stations.Count; i += step)
            for (int j = i + 1; j < stations.Count; j += step)
            {
                double d = (model.Stations[stations[i]].Position - model.Stations[stations[j]].Position).Length;
                if (d > best) (best, a, b) = (d, stations[i], stations[j]);
            }
        if (model.Stations.FirstOrDefault(s => (s.Flags & CaveStationFlags.Entrance) != 0) is { } entrance
            && (model.Stations[b].Position - entrance.Position).Length < (model.Stations[a].Position - entrance.Position).Length)
            (a, b) = (b, a);

        string from = NameOf(model, a), to = NameOf(model, b);
        int year = (int)Math.Floor(last);
        return (route with { From = from, To = to, Via = [], Stations = [] },
            $"flies the passage surveyed last ({year.ToString(CultureInfo.InvariantCulture)}), {from} to {to}.");
    }

    /// <summary>How a route names a station: by its name, or as name@survey where the name alone
    /// is in more than one place.</summary>
    private static string NameOf(CaveModel model, int station)
    {
        var s = model.Stations[station];
        bool alone = model.Stations.Where(o => o.Name == s.Name).Select(o => o.Position).Distinct().Count() == 1;
        if (alone || s.SurveyId is not { } id) return s.Name;
        return model.Surveys.FirstOrDefault(v => v.Id == id) is { } survey ? $"{s.Name}@{survey.Name}" : s.Name;
    }
}
