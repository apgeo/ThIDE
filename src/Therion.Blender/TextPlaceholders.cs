// Placeholders in the texts written over the picture. A preset is made before anyone knows which
// cave it will show, so its texts cannot name the cave; "{cave}" stands for the name and is
// filled in when a spec is lowered for a model. A text that is nothing but placeholders the model
// has no value for is left out: a preset whose title is "{cave}" writes no title over a model
// without a name.
//
// A placeholder is a name between braces. Names that are not known stay as they are written, so
// a text that happens to hold braces is not changed. Filling in is one pass: what a value holds is
// not looked at again.

using System.Text;

namespace Therion.Blender;

/// <summary>Fills in the placeholders of the texts of an <see cref="OverlaySpec"/>.</summary>
public static class TextPlaceholders
{
    /// <summary>Stands for the cave's name (<see cref="CaveName"/>).</summary>
    public const string Cave = "{cave}";

    /// <summary>The placeholders a text may hold, as they are written.</summary>
    public static readonly IReadOnlyList<string> Known = [Cave];

    /// <summary>The values <paramref name="meta"/> gives the placeholders, by their names
    /// without the braces. A model without a name gives <see cref="Cave"/> nothing.</summary>
    public static IReadOnlyDictionary<string, string> ValuesOf(SceneMeta? meta)
        => new Dictionary<string, string>(StringComparer.Ordinal) { ["cave"] = CaveName(meta) };

    /// <summary>
    /// The cave's name as the model has it, or nothing: the title of the model itself, which a
    /// <c>.3d</c> file has (<see cref="SceneMetaSource.Title"/>); else the title of the root of
    /// its survey tree, which a <c>.lox</c> file has; else, when the root has one survey under
    /// it and no title of its own, that survey's. A title that is only the name of a file is no
    /// name: a <c>.3d</c> file that was given none has its own file's name there, or that of the
    /// file it was made from.
    /// </summary>
    public static string CaveName(SceneMeta? meta)
    {
        if (meta is null) return "";
        if (!string.IsNullOrWhiteSpace(meta.Source.Title) && !IsAFileName(meta.Source.Title.Trim(), meta.Source.Path))
            return meta.Source.Title.Trim();
        var root = meta.Surveys.FirstOrDefault(s => s.ParentId == s.Id);
        if (root is null) return "";
        if (!string.IsNullOrWhiteSpace(root.Title)) return root.Title.Trim();
        var top = meta.Surveys.Where(s => s.ParentId == root.Id && s.Id != root.Id).ToList();
        return top.Count == 1 && !string.IsNullOrWhiteSpace(top[0].Title) ? top[0].Title!.Trim() : "";
    }

    /// <summary>True when <paramref name="text"/> holds a placeholder that is known.</summary>
    public static bool HasAny(string? text)
        => text is not null && Known.Any(known => text.Contains(known, StringComparison.Ordinal));

    /// <summary>True when a model's title is the name of a model file, or of the file the model
    /// was read from, with its extension or without.</summary>
    private static bool IsAFileName(string title, string? sourcePath)
    {
        if (title.EndsWith(".3d", StringComparison.OrdinalIgnoreCase) || title.EndsWith(".lox", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        string file = sourcePath[(sourcePath.LastIndexOfAny(['/', '\\']) + 1)..];
        int dot = file.LastIndexOf('.');
        return title.Equals(file, StringComparison.OrdinalIgnoreCase)
               || (dot > 0 && title.Equals(file[..dot], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><paramref name="spec"/> with the placeholders of its overlays filled in from
    /// <paramref name="meta"/>; the same spec when none of its texts holds one.</summary>
    public static SceneSpec Resolve(SceneSpec spec, SceneMeta? meta)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var overlays = Resolve(spec.Labels.Overlays, ValuesOf(meta));
        return ReferenceEquals(overlays, spec.Labels.Overlays) ? spec : spec with { Labels = spec.Labels with { Overlays = overlays } };
    }

    /// <summary>
    /// <paramref name="overlays"/> with the placeholders of its texts filled in from
    /// <paramref name="values"/> (names without braces): the title and its subtitle, the credits
    /// and corner lines, the end card, the captions and the corner captions. A text that held a
    /// placeholder and is blank afterwards is left out: a title, a line and an end card are then
    /// none, and a caption is dropped. The same object when no text holds a placeholder.
    /// </summary>
    public static OverlaySpec Resolve(OverlaySpec overlays, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentNullException.ThrowIfNull(values);
        bool Holds(string? text) => text is not null && values.Keys.Any(name => text.Contains("{" + name + "}", StringComparison.Ordinal));
        if (!Holds(overlays.Title) && !Holds(overlays.Subtitle) && !Holds(overlays.Credits) && !Holds(overlays.Corner)
            && !Holds(overlays.EndCard?.Text) && !Holds(overlays.EndCard?.Subtitle)
            && !overlays.Captions.Any(c => Holds(c.Text)) && !overlays.CornerCaptions.Any(c => Holds(c.Text)))
            return overlays;

        string? Line(string? text) => Holds(text) ? NullIfBlank(Fill(text!, values)) : text;
        IReadOnlyList<CaptionSpec> Timed(IReadOnlyList<CaptionSpec> captions)
            => captions.Any(c => Holds(c.Text))
                ? captions.Select(c => Holds(c.Text) ? c with { Text = Fill(c.Text, values) } : c)
                    .Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToList()
                : captions;

        var card = overlays.EndCard;
        if (card is not null && (Holds(card.Text) || Holds(card.Subtitle)))
        {
            string text = Holds(card.Text) ? Fill(card.Text, values) : card.Text;
            card = string.IsNullOrWhiteSpace(text) ? null : card with { Text = text, Subtitle = Line(card.Subtitle) };
        }
        return overlays with
        {
            Title = Line(overlays.Title),
            Subtitle = Line(overlays.Subtitle),
            Credits = Line(overlays.Credits),
            Corner = Line(overlays.Corner),
            EndCard = card,
            Captions = Timed(overlays.Captions),
            CornerCaptions = Timed(overlays.CornerCaptions),
        };
    }

    /// <summary><paramref name="text"/> with every <c>{name}</c> that <paramref name="values"/>
    /// has replaced by its value, in one pass; other braces stay.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        var filled = new StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf('{', at);
            int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (close < 0)
            {
                filled.Append(text, at, text.Length - at);
                break;
            }
            if (values.TryGetValue(text[(open + 1)..close], out string? value))
            {
                filled.Append(text, at, open - at).Append(value);
                at = close + 1;
            }
            else
            {
                // Not a placeholder: the brace stays, and what follows it is looked at again.
                filled.Append(text, at, open + 1 - at);
                at = open + 1;
            }
        }
        return filled.ToString();
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
