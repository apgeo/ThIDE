// A render bundle: a scene plan, its meshes, the player and two run scripts in one zip (or one
// folder), for rendering on a machine that has Blender and nothing of ThIDE. A zip holds one
// folder, named after the zip, so that unpacking it anywhere makes that folder and nothing else:
//
//   README.txt              what the files are and how to render them
//   LICENSE, NOTICE         the player's licence (MPL-2.0), and what the player is and where its
//                           source is
//   render.sh, render.cmd   start Blender with the player; --draft plays the draft plan, --compose
//                           writes the output again from the frames that are there
//   out/<name>.srt, .vtt    the captions as subtitles, where the movie will be (when it has any)
//   render.py               the player's entry script
//   scene-plan.json         the plan
//   scene-plan.draft.json   the same plan with a fast look (optional)
//   thide_player/...        the player
//   model.ply ...           the meshes, the images and any other file the plan names, at their
//                           plan paths (surface.ply and surface.png are a terrain and its image,
//                           photos/ holds the photos of the annotations)
//
// The files come from disk or from whoever makes the bundle, as streams. The README is in English
// or in Romanian, and whoever makes the bundle may give it an opening and sections of their own.
//
// A zip is deterministic: entries in ordinal order, one fixed time stamp, fixed permissions, so
// the same content under the same name gives the same bytes every time on the same .NET runtime.
// Writing one starts no process; rendering it is for whoever unpacks it (or
// Therion.Blender.Execution).

using System.IO.Compression;
using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Sources;
using Therion.CaveGeometry;

namespace Therion.Blender.Player;

/// <summary>What goes into a render bundle.</summary>
public sealed record RenderBundleContent
{
    /// <summary>The plan, written as <c>scene-plan.json</c>. Its output directory lies beside
    /// it, so a render stays inside the bundle's folder.</summary>
    public required ScenePlan Plan { get; init; }

    /// <summary>A second plan with a fast look (<see cref="ScenePlanBuilder.Draft"/>), written
    /// as <c>scene-plan.draft.json</c>; the run scripts play it with <c>--draft</c>.</summary>
    public ScenePlan? Draft { get; init; }

    /// <summary>The files the plans name, as plan path → the file on disk: every model's mesh,
    /// every material's image, every photo, and the modules of the plan's module folders. A
    /// photo is checked when the bundle is written: a PNG or a JPEG, no larger than
    /// <see cref="PlanImage"/> allows.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    /// <summary>More files the plans name, as plan path → what opens the file's content, for
    /// content that is not on disk: a mesh that was just made, a module. Each is opened once
    /// for every time the bundle is written, read to its end and closed; a photo is opened a
    /// second time, to be checked. A path is in <see cref="Files"/> or here, not in both.</summary>
    public IReadOnlyDictionary<string, Func<Stream>> Streams { get; init; } = new Dictionary<string, Func<Stream>>();

    /// <summary>The language the README is written in: "en" or "ro"
    /// (<see cref="RenderBundleWriter.ReadmeLanguages"/>). A culture name is taken by its
    /// language ("ro-RO"); any other language gives the English one.</summary>
    public string Language { get; init; } = "en";

    /// <summary>What the README says first, under its title, in place of this library's two
    /// lines on what the folder is: for whoever makes bundles under a name of their own. Null
    /// keeps the library's.</summary>
    public string? ReadmeIntro { get; init; }

    /// <summary>Sections the README gets after its own and before what the scene leaves out:
    /// what the bundle was made from, whom to ask. The text is written as it is, in whatever
    /// language it is in.</summary>
    public IReadOnlyList<RenderBundleReadmeSection> ReadmeSections { get; init; } = [];

    /// <summary>The settings the plan was made from that it does not carry
    /// (<see cref="ScenePlanBuilder.NotCarried"/>); the README names them.</summary>
    public IReadOnlyList<string> NotCarried { get; init; } = [];

    /// <summary>What the render shows less of than was asked for
    /// (<see cref="ScenePlanBuilder.Notices"/>): for whoever writes the bundle to pass on.</summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>The captions of the scene, written into the bundle as subtitles
    /// (<see cref="Subtitles"/>) where the plan's output will be, under its base name.</summary>
    public IReadOnlyList<CaptionSpec> Captions { get; init; } = [];
}

/// <summary>A section of a bundle's README that whoever makes the bundle adds.</summary>
/// <param name="Title">Its heading, written as it is; the README's own are in capitals.</param>
/// <param name="Text">Its lines, written as they are.</param>
public sealed record RenderBundleReadmeSection(string Title, string Text);

/// <summary>Writes render bundles: everything Blender needs to render a scene plan, in one zip
/// or folder.</summary>
public static class RenderBundleWriter
{
    /// <summary>The draft plan's name in a bundle.</summary>
    public const string DraftPlanFileName = "scene-plan.draft.json";

    public const string ReadmeFileName = "README.txt";
    public const string ShellScriptName = "render.sh";
    public const string WindowsScriptName = "render.cmd";

    /// <summary>The player's licence in a bundle: the Mozilla Public License 2.0.</summary>
    public const string LicenseFileName = "LICENSE";

    /// <summary>What the player is and where its source is.</summary>
    public const string NoticeFileName = "NOTICE";

    /// <summary>Where the player's source is.</summary>
    public const string SourceUrl = "https://github.com/apgeo/ThIDE";

    /// <summary>Where <see cref="FromModel"/> lets the render write, relative to the plan.</summary>
    public const string OutputDirectory = "out";

    /// <summary>The languages a README can be written in (<see cref="RenderBundleContent.Language"/>).</summary>
    public static readonly IReadOnlyList<string> ReadmeLanguages = ["en", "ro"];

    /// <summary>The oldest Blender the player plays a plan in.</summary>
    public const string MinimumBlender = "4.2";

    /// <summary>The time stamp of every entry of a zip.</summary>
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Unix permissions: rw-r--r-- and rwxr-xr-x. In a zip entry's external attributes they stand
    // in the upper 16 bits, beside the type of a regular file (0x8000).
    private const UnixFileMode PlainMode = (UnixFileMode)0x1A4;
    private const UnixFileMode ExecutableMode = (UnixFileMode)0x1ED;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>What the README says about each setting <see cref="ScenePlanBuilder.NotCarried"/>
    /// can name, in English and in Romanian.</summary>
    private static readonly Dictionary<string, (string En, string Ro)> NotCarriedText = new(StringComparer.Ordinal)
    {
        ["lighting.hdriPath"] = (
            "the HDRI image lighting (the scene has a dim plain background instead)",
            "iluminarea cu o imagine HDRI (scena are în schimb un fundal simplu, slab luminat)"),
    };

    /// <summary>
    /// Converts a cave model and lowers <paramref name="spec"/> to a plan for it: the content of a
    /// bundle. The mesh is written into <paramref name="workDirectory"/> and read from there when
    /// the bundle is written, and so are the terrain and its image when the spec draws the
    /// terrain surface and the model has one; the caller removes the folder afterwards.
    /// </summary>
    /// <param name="spec">The scene. Its source paths are ignored; its output is written to
    /// <see cref="OutputDirectory"/> inside the bundle under the spec's base name.</param>
    /// <param name="draft">Also make a draft plan with <see cref="ScenePlanBuilder.Draft"/>'s
    /// defaults.</param>
    /// <param name="geometry">The conversion's geometry options.</param>
    /// <param name="leads">Leads to mark, as the render service passes them.</param>
    /// <exception cref="ArgumentException">The spec is invalid or cannot be planned for this
    /// model.</exception>
    public static RenderBundleContent FromModel(
        ResolvedModelSource source, SceneSpec spec, string workDirectory, bool draft = false,
        GeometryOptions? geometry = null, IReadOnlyList<SourceLead>? leads = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        var conversion = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions
        {
            OutputDirectory = workDirectory,
            Geometry = geometry ?? new GeometryOptions(),
            Leads = leads ?? [],
            IncludeSurface = spec.Surface.Mode != SurfaceMode.Off,
        });
        var manifest = conversion.Manifest;
        var placed = spec with
        {
            Source = spec.Source with
            {
                PlyPath = manifest.ModelPath, SurfacePlyPath = manifest.SurfacePath, SurfaceImagePath = manifest.SurfaceImagePath,
            },
            Output = spec.Output with { OutputDirectory = OutputDirectory },
        };
        var plan = ScenePlanBuilder.FromSpec(placed, CameraFraming.FromGeometry(conversion.Geometry), conversion.Meta);
        // What the plan names of what the conversion wrote: the mesh, and the terrain with its
        // image when the plan draws them.
        var written = new[] { manifest.ModelPath, manifest.SurfacePath, manifest.SurfaceImagePath }.OfType<string>();
        var files = PlayerWriter.FilesOf(plan).ToDictionary(f => f.Path, f => written.First(w => Path.GetFileName(w) == f.Path));
        return new RenderBundleContent
        {
            Plan = plan,
            Draft = draft ? ScenePlanBuilder.Draft(plan) : null,
            Files = files,
            NotCarried = ScenePlanBuilder.NotCarried(spec),
            Notices = ScenePlanBuilder.Notices(placed, conversion.Meta),
            Captions = plan.Output.Kind == PlanOutputKind.Still ? [] : Subtitles.OneAtATime(spec.Labels.Overlays.Captions),
        };
    }

    /// <summary>Writes the bundle as a zip file, replacing one that is there. Its one folder is
    /// named after the file (<see cref="FolderName"/>).</summary>
    /// <returns>The paths of the zip's entries, in their order.</returns>
    /// <exception cref="ArgumentException">The content is not a bundle that renders on its
    /// own: see <see cref="WriteZip(RenderBundleContent, Stream, string)"/>.</exception>
    public static IReadOnlyList<string> WriteZip(RenderBundleContent content, string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        var entries = Entries(content);
        string top = FolderName(zipPath);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (folder is not null) Directory.CreateDirectory(folder);
        try
        {
            using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
            WriteZip(entries, stream, top);
        }
        catch
        {
            try { File.Delete(zipPath); } catch (IOException) { /* best effort */ }
            throw;
        }
        return entries.Select(e => top + "/" + e.Path).ToList();
    }

    /// <summary>Writes the bundle as a zip into <paramref name="destination"/>, which stays
    /// open. Every entry lies in one folder, <paramref name="name"/> made a name that every file
    /// system takes (see <see cref="FolderName"/>).</summary>
    /// <returns>The paths of the zip's entries, in their order.</returns>
    /// <exception cref="ArgumentException">A plan is invalid; its output directory does not lie
    /// beside it; a path of <see cref="RenderBundleContent.Files"/> is not a path inside the
    /// bundle or is one the bundle uses itself; a model's file or a module folder is not among
    /// the files; a photo's file is not an image a plan carries.</exception>
    /// <exception cref="FileNotFoundException">A file of
    /// <see cref="RenderBundleContent.Files"/> does not exist.</exception>
    public static IReadOnlyList<string> WriteZip(RenderBundleContent content, Stream destination, string name)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(name);
        var entries = Entries(content);
        string top = SafeName(name);
        WriteZip(entries, destination, top);
        return entries.Select(e => top + "/" + e.Path).ToList();
    }

    /// <summary>The folder a bundle's zip holds, and makes when it is unpacked: the zip's file
    /// name without its extension, with every character that some file system refuses replaced
    /// by '_', no dot or space at its end, and '_' after a name Windows keeps for a device
    /// ("CON_"); "render-bundle" when nothing is left of it.</summary>
    public static string FolderName(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        return SafeName(Path.GetFileNameWithoutExtension(zipPath));
    }

    private static string SafeName(string name)
    {
        var safe = new StringBuilder(name.Length);
        foreach (char c in name)
            safe.Append(c < ' ' || "<>:\"/\\|?*".Contains(c) ? '_' : c);
        // Windows drops dots and spaces at the end of a name; they are taken off here, so that
        // the folder has the same name everywhere.
        string result = safe.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length == 0) return "render-bundle";
        string stem = result.Split('.')[0];
        return ReservedOnWindows.Contains(stem.TrimEnd(' ')) ? stem + "_" + result[stem.Length..] : result;
    }

    private static readonly HashSet<string> ReservedOnWindows = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the bundle into a folder, as unpacking its zip would. Files of the same names are
    /// replaced, the player's package as a whole; anything else in the folder stays.
    /// </summary>
    /// <returns>The paths written, relative to the folder.</returns>
    /// <exception cref="ArgumentException">See <see cref="WriteZip(RenderBundleContent, Stream)"/>.</exception>
    public static IReadOnlyList<string> WriteFolder(RenderBundleContent content, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var entries = Entries(content);
        string package = Path.Combine(folder, PlayerFiles.PackageName);
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        foreach (var entry in entries)
        {
            string target = Path.Combine(folder, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                input.CopyTo(output);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, entry.Executable ? ExecutableMode : PlainMode);
        }
        return entries.Select(e => e.Path).ToList();
    }

    /// <summary>The README of a bundle with this content: what it is, how to render it, what
    /// it will write, and what the plan does not carry; in the content's language, with the
    /// opening and the sections its maker gives it.</summary>
    public static string Readme(RenderBundleContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var plan = content.Plan;
        bool romanian = (content.Language ?? "").Split('-', '_')[0].Equals("ro", StringComparison.OrdinalIgnoreCase);
        string T(string en, string ro) => romanian ? ro : en;
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');
        void Lines(string en, string ro)
        {
            foreach (string line in T(en, ro).Split('\n')) Line(line);
        }

        string title = plan.Name is { Length: > 0 } name ? name : T("A cave scene", "O scenă de peșteră");
        Line(title);
        Line(new string('=', Math.Min(title.Length, 72)));
        Line();
        if (content.ReadmeIntro is { } intro)
            foreach (string line in intro.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')) Line(line);
        else
            Lines(
                "A scene for Blender: a cave model, a camera move and a look, ready to render. Nothing in\nthis folder needs ThIDE.",
                "O scenă pentru Blender: un model de peșteră, o mișcare a camerei și un aspect, gata de\nrandat. Nimic din acest dosar nu are nevoie de ThIDE.");
        Line();
        Lines(
            $"Needs Blender {MinimumBlender} or newer (tested with 4.2 LTS, 4.5 LTS and 5.0), from blender.org or\nyour system's packages. A Blender from the Microsoft Store cannot be started this way.",
            $"Este nevoie de Blender {MinimumBlender} sau mai nou (testat cu 4.2 LTS, 4.5 LTS și 5.0), de pe blender.org\nsau din pachetele sistemului. Un Blender din Microsoft Store nu poate fi pornit astfel.");
        Line();
        Line(T("HOW TO RENDER", "CUM SE RANDEAZĂ"));
        Line();
        Lines(
            "Unpack the bundle: it unpacks into one folder, the one this file is in. In that folder:",
            "Dezarhivați pachetul: se dezarhivează într-un singur dosar, cel în care se află acest\nfișier. În acel dosar:");
        Line();
        Line("  Linux, macOS:   sh render.sh");
        Line("  Windows:        render.cmd");
        if (content.Draft is not null)
        {
            Line();
            Lines(
                "A quick draft first, to see the camera move and the framing:",
                "Mai întâi o previzualizare rapidă, pentru a vedea mișcarea camerei și încadrarea:");
            Line();
            Line("  Linux, macOS:   sh render.sh --draft");
            Line("  Windows:        render.cmd --draft");
        }
        Line();
        Lines(
            "The scripts look for Blender on the PATH (and in /Applications on a Mac, in\n\"C:\\Program Files\\Blender Foundation\" on Windows). To use another Blender, name it:",
            "Scripturile caută Blender în PATH (și în /Applications pe Mac, în\n\"C:\\Program Files\\Blender Foundation\" pe Windows). Pentru a folosi alt Blender, numiți-l:");
        Line();
        Line("  Linux, macOS:   BLENDER=/path/to/blender sh render.sh");
        Line(T(
            "  Windows:        set \"BLENDER=C:\\path\\to\\blender.exe\"   and then   render.cmd",
            "  Windows:        set \"BLENDER=C:\\path\\to\\blender.exe\"   și apoi   render.cmd"));
        Line();
        Line(T("Or start Blender yourself in this folder:", "Sau porniți chiar dumneavoastră Blender în acest dosar:"));
        Line();
        Line("  blender -b --factory-startup --python render.py");
        if (content.Draft is not null)
            Line($"  blender -b --factory-startup --python render.py -- --plan {DraftPlanFileName}");
        Line();
        Line(T(
            "To look at the scene in Blender's window instead of rendering it:",
            "Pentru a privi scena în fereastra Blender în loc de a o randa:"));
        Line();
        Line("  blender --factory-startup --python render.py -- --build-only");
        if (plan.HasTexts)
        {
            Line();
            Line(T("TWO STAGES", "DOUĂ ETAPE"));
            Line();
            Lines(
                "This scene has texts over the picture. It is rendered in two stages: the scene as PNG\nframes into the folder \"frames\", then the output from those frames with the texts over\nthem. The frames are removed when the output is written.",
                "Această scenă are texte peste imagine. Se randează în două etape: scena, sub formă de\ncadre PNG, în dosarul \"frames\", apoi filmul sau imaginile din acele cadre, cu textele\npeste ele. Cadrele sunt șterse după ce acestea au fost scrise.");
            Line();
            Lines(
                "If the render is stopped, start it again: the frames that are there are kept and the\nrender goes on. To change a text (\"texts\" in scene-plan.json: a title, a caption) without\nrendering the scene again, keep the frames the first time and compose from them after:",
                "Dacă randarea este oprită, porniți-o din nou: cadrele existente sunt păstrate și randarea\ncontinuă. Pentru a schimba un text (\"texts\" în scene-plan.json: un titlu, o subtitrare) fără\na randa din nou scena, păstrați cadrele prima dată și compuneți apoi din ele:");
            Line();
            Line(T(
                "  Linux, macOS:   sh render.sh --keep-frames      then   sh render.sh --compose",
                "  Linux, macOS:   sh render.sh --keep-frames      apoi   sh render.sh --compose"));
            Line(T(
                "  Windows:        render.cmd --keep-frames        then   render.cmd --compose",
                "  Windows:        render.cmd --keep-frames        apoi   render.cmd --compose"));
            Line();
            Lines(
                "The second stage refuses frames that were rendered from another scene: change anything\nbut the texts and the output's name, and the frames are rendered again.",
                "A doua etapă refuză cadrele randate dintr-o altă scenă: dacă schimbați altceva decât\ntextele și numele fișierului de ieșire, cadrele sunt randate din nou.");
        }
        Line();
        Line(T("WHAT IT WRITES", "CE SCRIE"));
        Line();
        foreach (string line in Writes(plan, "", romanian)) Line(line);
        if (content.Draft is { } draft)
            foreach (string line in Writes(draft, T("The draft: ", "Previzualizarea: "), romanian)) Line(line);
        if (content.Captions.Count > 0)
        {
            string srt = SubtitlePath(plan, Subtitles.SrtExtension), vtt = Path.GetFileName(SubtitlePath(plan, Subtitles.VttExtension));
            Line(T(
                $"{srt} and {vtt} are there already: the captions as subtitles, for a video player or a web page.",
                $"{srt} și {vtt} există deja: subtitrările, pentru un player video sau o pagină web."));
        }
        Line();
        Line(T("THE FILES", "FIȘIERELE"));
        Line();
        var files = new List<(string, string)>
        {
            (ReadmeFileName, T("this file", "acest fișier")),
            ($"{ShellScriptName}, {WindowsScriptName}", T("start Blender with the player", "pornesc Blender cu playerul")),
            (PlayerFiles.EntryFileName, T(
                "the script Blender runs: it plays scene-plan.json with thide_player",
                "scriptul rulat de Blender: redă scene-plan.json cu thide_player")),
            (ScenePlanSerializer.FileName, T(
                $"the scene: models, camera track, look, output (JSON, plan version {plan.Version})",
                $"scena: modele, traseul camerei, aspect, ieșire (JSON, versiunea {plan.Version} a planului)")),
        };
        if (content.Draft is not null)
            files.Add((DraftPlanFileName, T("the same scene with a fast look", "aceeași scenă cu un aspect rapid")));
        files.Add((PlayerFiles.PackageName + "/", T(
            $"the player (Python), version {PlayerFiles.Version}", $"playerul (Python), versiunea {PlayerFiles.Version}")));
        files.Add(($"{LicenseFileName}, {NoticeFileName}", T(
            "the player's licence (MPL-2.0), and where its source is", "licența playerului (MPL-2.0) și unde se află sursa lui")));
        foreach (string path in content.Files.Keys.Concat(content.Streams.Keys).Select(Normalized).Order(StringComparer.Ordinal))
            files.Add((path, plan.Models.Any(m => Normalized(m.Path) == path) ? T("a model's mesh", "rețeaua de poligoane a unui model")
                : (plan.TexturedMaterials ?? []).Any(m => m.Image is { } image && Normalized(image) == path)
                    ? T("the image of a material", "imaginea unui material")
                    : (plan.Annotations ?? []).Any(a => a.Image is { } image && Normalized(image) == path)
                        ? T("a photo shown in the scene", "o fotografie afișată în scenă")
                        : T("a file the plan names", "un fișier numit de plan")));
        int width = files.Max(f => f.Item1.Length) + 2;
        foreach (var (path, what) in files)
            Line("  " + path.PadRight(width) + what);
        Line();
        Lines(
            "Every value in the plans is resolved: positions in metres (X east, Y north, Z up), times in\nseconds, colours as linear RGB. Change a plan with any text editor and render again.",
            "Fiecare valoare din planuri este rezolvată: poziții în metri (X est, Y nord, Z sus), timpi în\nsecunde, culori în RGB liniar. Modificați un plan cu orice editor de text și randați din nou.");
        Line();
        Lines(
            $"The player is part of ThIDE and is subject to the Mozilla Public License 2.0 (LICENSE). Its\nsource is in the ThIDE repository: {SourceUrl}",
            $"Playerul face parte din ThIDE și este supus licenței Mozilla Public License 2.0 (LICENSE).\nSursa lui se află în depozitul ThIDE: {SourceUrl}");

        foreach (var section in content.ReadmeSections ?? [])
        {
            Line();
            Line(section.Title);
            Line();
            foreach (string line in (section.Text ?? "").ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')) Line(line);
        }

        if (content.NotCarried.Count > 0)
        {
            Line();
            Line(T("NOT IN THIS SCENE", "CE LIPSEȘTE DIN ACEASTĂ SCENĂ"));
            Line();
            Lines(
                "The settings this scene was made from also asked for the following, which a scene plan\ncannot say yet. The render does not show them:",
                "Setările din care a fost făcută această scenă cereau și următoarele, pe care un plan de\nscenă nu le poate încă descrie. Randarea nu le arată:");
            Line();
            foreach (string lost in content.NotCarried)
                Line("  - " + (NotCarriedText.TryGetValue(lost, out var what) ? $"{T(what.En, what.Ro)} ({lost})" : lost));
        }
        return text.ToString();
    }

    /// <summary>What a plan's render writes, in plain words.</summary>
    private static IEnumerable<string> Writes(ScenePlan plan, string prefix, bool romanian)
    {
        string T(string en, string ro) => romanian ? ro : en;
        var output = plan.Output;
        string folder = Normalized(output.Directory);
        string In(string file) => folder.Length == 0 ? file : folder + "/" + file;
        int frames = plan.OutputFrameCount;
        string seconds = (frames / (double)plan.Timeline.Fps).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string look = $"{plan.Look.Width}x{plan.Look.Height} {T("pixels", "pixeli")}, {EngineName(plan.Look.Engine, romanian)}";
        if (plan.Blend is { } blend)
            yield return prefix + T(
                $"{Normalized(blend.Path)}: the built scene as a Blender file, to change by hand or render later.",
                $"{Normalized(blend.Path)}: scena construită, ca fișier Blender, de modificat manual sau de randat mai târziu.");
        if (!output.Render)
        {
            yield return prefix + T(
                "nothing is rendered: the scene is only built and saved.", "nu se randează nimic: scena este doar construită și salvată.");
            yield break;
        }
        switch (output.Kind)
        {
            case PlanOutputKind.Video:
                string extension = output.Container.ToString().ToLowerInvariant();
                string video = In(output.BaseName + "." + extension), firstFrame = In(output.BaseName + "_0001.png");
                yield return prefix + T(
                    $"{video}, a video of {frames} frames at {plan.Timeline.Fps} fps ({seconds} s), {look}.",
                    $"{video}, un video de {frames} cadre la {plan.Timeline.Fps} fps ({seconds} s), {look}.");
                if (prefix.Length == 0)
                    yield return T(
                        $"  (A Blender that cannot write video writes numbered PNG frames instead: {firstFrame} ...)",
                        $"  (Un Blender care nu poate scrie video scrie în schimb cadre PNG numerotate: {firstFrame} ...)");
                break;
            case PlanOutputKind.Frames:
                string first = In(output.BaseName + "_0001.png");
                string final = In(output.BaseName + "_" + frames.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                yield return prefix + T($"{first} to {final}, {look}.", $"{first} până la {final}, {look}.");
                break;
            default:
                yield return prefix + T(
                    $"{In(output.BaseName + ".png")}, one picture, {look}.", $"{In(output.BaseName + ".png")}, o imagine, {look}.");
                break;
        }
    }

    private static string EngineName(PlanEngine engine, bool romanian) => engine switch
    {
        PlanEngine.Cycles => "Cycles",
        PlanEngine.Eevee => "EEVEE",
        _ => romanian ? "Workbench (vizualizarea solidă a Blender)" : "Workbench (Blender's solid view)",
    };

    // ---- the entries ----

    private sealed record Entry(string Path, Func<Stream> Open, bool Executable = false);

    private static List<Entry> Entries(RenderBundleContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Plan);
        Check(content.Plan, "plan");
        if (content.Draft is { } draft) Check(draft, "draft");

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ScenePlanSerializer.FileName, DraftPlanFileName, ReadmeFileName, ShellScriptName, WindowsScriptName,
            LicenseFileName, NoticeFileName, PlayerFiles.EntryFileName,
        };
        var files = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
        void Add(string planPath, Func<Stream> open)
        {
            string path = ScenePlanValidator.IsPathBesideThePlan(planPath) ? Normalized(planPath) : "";
            if (path.Length == 0)
                throw new ArgumentException($"'{planPath}' is not a path inside the bundle.", nameof(content));
            if (reserved.Contains(path) || path.StartsWith(PlayerFiles.PackageName + "/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"'{planPath}' is a file of the bundle itself.", nameof(content));
            if (!files.TryAdd(path, open))
                throw new ArgumentException($"'{planPath}' is given twice.", nameof(content));
        }
        foreach (var (planPath, source) in content.Files ?? new Dictionary<string, string>())
        {
            Add(planPath, () => File.OpenRead(source));
            if (!File.Exists(source))
                throw new FileNotFoundException($"The file for '{planPath}' does not exist: {source}", source);
        }
        foreach (var (planPath, open) in content.Streams ?? new Dictionary<string, Func<Stream>>())
        {
            if (open is null)
                throw new ArgumentException($"'{planPath}' has nothing to open it.", nameof(content));
            Add(planPath, () => open() ?? throw new InvalidOperationException($"What opens '{planPath}' gave no stream."));
        }
        var checkedPhotos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in new[] { content.Plan, content.Draft }.OfType<ScenePlan>())
        {
            var photos = PlayerWriter.PhotosOf(plan);
            foreach (var (path, what) in PlayerWriter.FilesOf(plan))
            {
                if (!files.TryGetValue(Normalized(path), out var open))
                    throw new ArgumentException($"The file of {what} ({path}) is not among the bundle's files.", nameof(content));
                if (photos.Contains(path) && checkedPhotos.Add(Normalized(path))) PlayerWriter.CheckPhoto(path, what, open);
            }
            foreach (string folder in plan.ModuleFolders)
                if (!files.Keys.Any(f => f.StartsWith(Normalized(folder) + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"The module folder '{folder}' has no files in the bundle.", nameof(content));
        }

        var entries = new List<Entry>
        {
            Text(ScenePlanSerializer.FileName, ScenePlanSerializer.Write(content.Plan)),
            Text(ReadmeFileName, Readme(content)),
            Text(ShellScriptName, BundleFile(ShellScriptName).ReplaceLineEndings("\n"), executable: true),
            Text(WindowsScriptName, BundleFile(WindowsScriptName).ReplaceLineEndings("\r\n")),
            Text(LicenseFileName, BundleFile(LicenseFileName).ReplaceLineEndings("\n")),
            Text(NoticeFileName, BundleFile(NoticeFileName).ReplaceLineEndings("\n")),
        };
        if (content.Draft is { } d) entries.Add(Text(DraftPlanFileName, ScenePlanSerializer.Write(d)));
        if (content.Captions is { Count: > 0 } captions)
        {
            entries.Add(Text(SubtitlePath(content.Plan, Subtitles.SrtExtension), Subtitles.Srt(captions)));
            entries.Add(Text(SubtitlePath(content.Plan, Subtitles.VttExtension), Subtitles.Vtt(captions)));
        }
        entries.AddRange(PlayerFiles.JobFiles.Select(f => Text(f.Path, f.Text)));
        entries.AddRange(files.Select(f => new Entry(f.Key, f.Value)));
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }

    private static void Check(ScenePlan plan, string which)
    {
        var errors = ScenePlanValidator.Validate(plan);
        if (errors.Count > 0)
            throw new ArgumentException(
                $"The {which} is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")), nameof(plan));
        if (!ScenePlanValidator.IsPathBesideThePlan(plan.Output.Directory))
            throw new ArgumentException(
                $"The {which}'s output directory '{plan.Output.Directory}' does not lie beside it; a bundle renders into its own folder.",
                nameof(plan));
    }

    /// <summary>Where a subtitle file of the plan lies in the bundle: where the plan's output
    /// will be, under its base name.</summary>
    public static string SubtitlePath(ScenePlan plan, string extension)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string folder = Normalized(plan.Output.Directory);
        return (folder.Length == 0 ? "" : folder + "/") + plan.Output.BaseName + extension;
    }

    private static Entry Text(string path, string text, bool executable = false)
    {
        byte[] bytes = Utf8NoBom.GetBytes(text);
        return new Entry(path, () => new MemoryStream(bytes, writable: false), executable);
    }

    private static void WriteZip(List<Entry> entries, Stream destination, string top)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true, Utf8NoBom);
        foreach (var entry in entries)
        {
            var zipped = zip.CreateEntry(top + "/" + entry.Path, CompressionLevel.Optimal);
            zipped.LastWriteTime = EntryTime;
            zipped.ExternalAttributes = (0x8000 | (int)(entry.Executable ? ExecutableMode : PlainMode)) << 16;
            using var input = entry.Open();
            using var output = zipped.Open();
            input.CopyTo(output);
        }
    }

    /// <summary>A plan path with forward slashes and without <c>.</c> or empty parts; "" for
    /// the plan's own folder.</summary>
    private static string Normalized(string path)
        => string.Join('/', path.Split('/', '\\').Where(part => part.Length > 0 && part != "."));

    /// <summary>A file every bundle has as it is: a run script, the licence, the notice.</summary>
    private static string BundleFile(string name)
    {
        var assembly = typeof(RenderBundleWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream("bundle/" + name)
                           ?? throw new InvalidOperationException($"The bundle file {name} is not in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
