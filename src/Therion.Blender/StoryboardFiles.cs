// The files a storyboard names beside the model: the photos of its annotations, its HDRI images
// and its files of survey dates. In a storyboard's file they are named relative to that file, as
// an annotations file names its photos; whoever reads the file makes them absolute (Rooted) and,
// when it holds them to a folder (an agent's workspace), looks at each (Named).

namespace Therion.Blender;

/// <summary>The files a <see cref="Storyboard"/> names: see the file header.</summary>
public static class StoryboardFiles
{
    /// <summary>The storyboard with the files it names relative to <paramref name="folder"/> made
    /// absolute; one named absolutely stays as it is.</summary>
    public static Storyboard Rooted(Storyboard storyboard, string folder)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Map(storyboard, file => Path.GetFullPath(Path.Combine(folder, file)));
    }

    /// <summary>Every file the storyboard names, as it names it, each once.</summary>
    public static IReadOnlyList<string> Named(Storyboard storyboard)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        var files = new List<string>();
        Map(storyboard, file =>
        {
            if (!files.Contains(file, StringComparer.Ordinal)) files.Add(file);
            return file;
        });
        return files;
    }

    /// <summary>The storyboard with every file it names passed through <paramref name="file"/>.</summary>
    public static Storyboard Map(Storyboard storyboard, Func<string, string> file)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(file);
        string? In(string? name) => string.IsNullOrWhiteSpace(name) ? name : file(name);
        IReadOnlyList<AnnotationSpec>? Photos(IReadOnlyList<AnnotationSpec>? annotations)
            => annotations?.Select(a => a?.Image is null ? a! : a with { Image = In(a.Image) }).ToList();
        LightingSpec? Lit(LightingSpec? lighting) => lighting?.HdriPath is null ? lighting : lighting with { HdriPath = In(lighting.HdriPath) };
        RevealSpec? Dated(RevealSpec? reveal) => reveal?.DatesPath is null ? reveal : reveal with { DatesPath = In(reveal.DatesPath) };
        var defaults = storyboard.Defaults;
        return storyboard with
        {
            Defaults = defaults with { Annotations = Photos(defaults.Annotations), Lighting = Lit(defaults.Lighting), Reveal = Dated(defaults.Reveal) },
            Shots = storyboard.Shots.Select(s => s is null ? s! : s with
            {
                Annotations = Photos(s.Annotations), Lighting = Lit(s.Lighting),
                Reveal = s.Reveal is { Spec: { } own } reveal ? reveal with { Spec = Dated(own) } : s.Reveal,
            }).ToList(),
        };
    }
}
