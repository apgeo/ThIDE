// `therion-cli blender <model> --tour`: a tour of the cave's branches. The main passage and the
// side passages worth a visit are worked out from the model, brought within a time budget and
// written as a storyboard, which is then rendered, drafted or bundled like any storyboard; or
// only written (--write-storyboard), to be edited by hand and rendered with --storyboard.

using System.Globalization;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.Cli;

public static partial class BlenderVerb
{
    /// <summary>The options that say what a tour is; they go with <c>--tour</c>.</summary>
    private static readonly string[] TourOptions =
    [
        "--trunk", "--tour-start", "--tour-via", "--tour-end", "--tour-seconds", "--branch-seconds", "--branch-mode", "--branch",
        "--peek-short", "--min-length", "--max-branches", "--no-lead-branches", "--include", "--exclude", "--tour-depth",
        "--tour-language", "--write-storyboard",
    ];

    /// <summary>
    /// The tour <c>--tour</c> (or <c>--tour-file &lt;tour.json&gt;</c>) asks for, worked out for
    /// the model: null without the option. Its table and notices go to
    /// <paramref name="stderr"/>; with <c>--write-storyboard &lt;file&gt;</c> its storyboard is
    /// written and the file's path goes to <paramref name="stdout"/>.
    /// </summary>
    /// <param name="done">True when writing the storyboard was all that was asked for: neither
    /// a draft nor a bundle.</param>
    private static bool TryPlanTour(
        IReadOnlyList<string> args, string source, bool hasStoryboard, TextWriter stdout, TextWriter stderr, out Tour? tour, out bool done)
    {
        tour = null;
        done = false;
        string? file = Option(args, "--tour-file");
        if (!Flag(args, "--tour") && file is null && !Flag(args, "--tour-file"))
        {
            if (TourOptions.FirstOrDefault(o => Flag(args, o)) is not { } alone) return true;
            stderr.WriteLine($"error: {alone} goes with a tour (--tour, or --tour-file <tour.json>).");
            return false;
        }
        if (hasStoryboard || Option(args, "--spec") is not null || Option(args, "--export") is not null)
        {
            stderr.WriteLine("error: --tour makes a storyboard of its own: it does not go with --storyboard, --spec or --export.");
            return false;
        }

        if (SceneOptions.FirstOrDefault(o => Flag(args, o)) is { } said)
        {
            stderr.WriteLine($"error: {said} does not go with a tour: write its storyboard (--write-storyboard <file>) and say it there.");
            return false;
        }
        string? keep = Option(args, "--write-storyboard");
        if (keep is null && Flag(args, "--write-storyboard"))
        {
            stderr.WriteLine("error: --write-storyboard takes the file to write (<name>.storyboard.json).");
            return false;
        }

        var spec = new TourSpec();
        if (file is not null || Flag(args, "--tour-file"))
        {
            if (file is null || !File.Exists(file))
            {
                stderr.WriteLine($"error: tour file not found: {file}");
                return false;
            }
            try { spec = TourSpecSerializer.ReadFile(file); }
            catch (Exception ex) when (ex is TourFormatException or IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: bad tour file: {ex.Message}");
                return false;
            }
        }
        if (!TryReadTourOptions(args, stderr, ref spec)) return false;

        IReadOnlyList<SourceLead>? leads = null;
        if (Options(args, "--leads").Any())
        {
            var marked = new SceneSpec { Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true } } };
            if (!TryReadLeads(args, source, marked, stderr, out var listed)) return false;
            leads = listed;
        }

        try
        {
            var geometry = GeometryStage.Build(CaveModelReader.ReadFile(source));
            tour = TourBuilder.Build(geometry, spec, leads);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: {ex.Message.Split(" (Parameter", 2)[0]}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaveFileFormatException)
        {
            stderr.WriteLine($"error: cannot read '{source}' for its tour: {ex.Message}");
            return false;
        }
        if (StoryboardValidator.Validate(tour.Storyboard) is { Count: > 0 } errors)
        {
            stderr.WriteLine("error: the tour does not make a valid storyboard:");
            foreach (var error in errors) stderr.WriteLine($"  {error.Path}: {error.Message}");
            return false;
        }

        stderr.WriteLine(tour.TableText());
        stderr.WriteLine(string.Create(CultureInfo.InvariantCulture, $"a tour of {tour.Storyboard.Shots.Count} shot(s), {tour.Seconds:0.0} s"));
        foreach (string notice in tour.Notices) stderr.WriteLine($"note: {notice}");

        if (keep is { } path)
        {
            try
            {
                if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder) Directory.CreateDirectory(folder);
                StoryboardSerializer.WriteFile(tour.Storyboard, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return false;
            }
            stdout.WriteLine(Path.GetFullPath(path));
            // The file was what was asked for, unless a draft or a bundle is asked for too.
            done = !Flag(args, "--draft") && Option(args, "--bundle") is null;
        }
        return true;
    }

    /// <summary>The tour's section of a bundle's README.</summary>
    private static Therion.Blender.Player.RenderBundleReadmeSection TourReadme(Tour tour, string language)
        => new(language == "ro" ? "Turul galeriilor" : "Branch tour", tour.ReadmeText());

    /// <summary>One of an enum's names, in any case: not a number, and not several names.</summary>
    private static bool Word<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        foreach (string name in Enum.GetNames<TEnum>())
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<TEnum>(name);
                return true;
            }
        value = default;
        return false;
    }

    private static bool TryReadTourOptions(IReadOnlyList<string> args, TextWriter stderr, ref TourSpec spec)
    {
        bool Number(string option, double least, out double? value)
        {
            value = null;
            if (Option(args, option) is not { } text) return !Flag(args, option) || Missing(option);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= least)
            {
                value = n;
                return true;
            }
            stderr.WriteLine($"error: {option} takes a number, {least.ToString(CultureInfo.InvariantCulture)} or more, not '{text}'.");
            return false;
        }
        bool Missing(string option)
        {
            stderr.WriteLine($"error: {option} takes a value.");
            return false;
        }
        bool Mode(string text, string option, out TourBranchMode mode)
        {
            if (Word(text, out mode)) return true;
            stderr.WriteLine($"error: {option} takes cut, return, peek or skip, not '{text}'.");
            return false;
        }

        if (Option(args, "--trunk") is { } trunk)
        {
            if (!Word(trunk, out TourTrunkKind kind))
            {
                stderr.WriteLine($"error: --trunk takes auto, longest, farthest, deepest, through or via, not '{trunk}'.");
                return false;
            }
            spec = spec with { Trunk = kind };
        }
        if (Option(args, "--tour-start") is { } start) spec = spec with { Start = start };
        if (Option(args, "--tour-end") is { } end) spec = spec with { End = end };
        if (Options(args, "--tour-via").ToList() is { Count: > 0 } via)
            spec = spec with { Via = via, Trunk = spec.Trunk == TourTrunkKind.Auto ? TourTrunkKind.Via : spec.Trunk };

        if (!Number("--tour-seconds", 1, out double? tourSeconds) || !Number("--branch-seconds", 1, out double? branchSeconds)
            || !Number("--min-length", 0, out double? minLength) || !Number("--max-branches", 0, out double? maxCount)
            || !Number("--tour-depth", 1, out double? depth))
            return false;
        if (tourSeconds is { } whole) spec = spec with { TourSeconds = whole };
        if (branchSeconds is { } each) spec = spec with { BranchSeconds = each };
        if (minLength is { } least) spec = spec with { MinLength = least };
        if (maxCount is { } most) spec = spec with { MaxCount = (int)Math.Min(most, int.MaxValue) };
        if (depth is { } deep) spec = spec with { Depth = (int)Math.Min(deep, 16) };

        if (Option(args, "--branch-mode") is { } all)
        {
            if (!Mode(all, "--branch-mode", out var mode)) return false;
            spec = spec with { Mode = mode };
        }
        var modes = new Dictionary<string, TourBranchMode>(spec.Modes, StringComparer.Ordinal);
        foreach (string one in Options(args, "--branch"))
        {
            int at = one.LastIndexOf('=');
            if (at <= 0 || at == one.Length - 1)
            {
                stderr.WriteLine($"error: --branch takes <id>=<cut|return|peek|skip> (\"B5=return\"), not '{one}'.");
                return false;
            }
            if (!Mode(one[(at + 1)..], "--branch", out var mode)) return false;
            modes[one[..at]] = mode;
        }
        if (modes.Count > 0) spec = spec with { Modes = modes };
        if (Flag(args, "--peek-short")) spec = spec with { PeekShort = true };
        if (Flag(args, "--no-lead-branches")) spec = spec with { WithLeads = false };
        if (Options(args, "--include").ToList() is { Count: > 0 } include) spec = spec with { Include = [.. spec.Include, .. include] };
        if (Options(args, "--exclude").ToList() is { Count: > 0 } exclude) spec = spec with { Exclude = [.. spec.Exclude, .. exclude] };

        if (Option(args, "--tour-language") is { } language)
        {
            if (!TourBuilder.Languages.Contains(language, StringComparer.Ordinal))
            {
                stderr.WriteLine($"error: --tour-language takes one of: {string.Join(", ", TourBuilder.Languages)}.");
                return false;
            }
            spec = spec with { Language = language };
        }
        if (Option(args, "--preset") is { } preset)
        {
            if (BuiltInPresets.ByName(preset) is null)
            {
                stderr.WriteLine($"error: unknown preset '{preset}'. Available: {string.Join(", ", BuiltInPresets.All.Select(p => $"\"{p.Name}\""))}.");
                return false;
            }
            spec = spec with { Preset = preset };
        }
        return true;
    }
}
