// typed .th2 object options. thbook v6.4.0 §"point"/"line"/"area" pp.25-32.
// Therion source-of-truth: therion/src/thpoint.cxx / thline.cxx / tharea.cxx.
//
// point/line/area objects carry `-option value…` settings (-id, -subtype, -orientation, -scale,
// -clip, -place, -text, -value, -name, …). This parses them from the token stream into a
// structured list with typed accessors, while the raw text is still kept on the AST node for
// round-tripping.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>One <c>-name value…</c> option on a .th2 object. <see cref="Name"/> excludes the dash.</summary>
public readonly record struct Th2Option(string Name, string Value, SourceSpan Span);

/// <summary>
/// The parsed option set of a .th2 point / line / area object, with typed accessors for the
/// common options. Unknown options are still retained so nothing is lost.
/// </summary>
public sealed class Th2OptionList
{
    public static readonly Th2OptionList Empty = new(ImmutableArray<Th2Option>.Empty);

    public ImmutableArray<Th2Option> Options { get; }

    public Th2OptionList(ImmutableArray<Th2Option> options) => Options = options;

    /// <summary>The raw value of option <paramref name="name"/> (dash-less), or null.</summary>
    public string? Get(string name)
    {
        foreach (var o in Options)
            if (string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)) return o.Value;
        return null;
    }

    /// <summary>True if the option is present (even valueless flags).</summary>
    public bool Has(string name) => Get(name) is not null;

    private double? GetDouble(string name) =>
        Get(name) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;

    private bool? GetBool(string name) => Get(name) switch
    {
        null => null,
        var v when v.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
        var v when v.Equals("off", StringComparison.OrdinalIgnoreCase) => false,
        _ => null,
    };

    // ---- common accessors (point + line + area) ----
    public string? Id => Get("id");
    public string? Subtype => Get("subtype");
    public double? Orientation => GetDouble("orientation") ?? GetDouble("orient");
    public string? Scale => Get("scale");
    public string? Place => Get("place");
    public bool? Clip => GetBool("clip");
    public bool? Visibility => GetBool("visibility");
    public string? Align => Get("align");
    public string? Text => Get("text");
    public string? Value => Get("value");
    public string? Name => Get("name");      // station ref (point type station)
    public string? From => Get("from");
    public string? Scrap => Get("scrap");    // section cross-ref
    public string? Context => Get("context");

    // ---- line-specific ----
    public bool? Reverse => GetBool("reverse");
    public string? Close => Get("close");    // on/off/auto
    public string? Outline => Get("outline");// in/out/none
    public string? Mark => Get("mark");
    public string? Head => Get("head");
    public string? Direction => Get("direction");

    /// <summary>
    /// Parses options starting at <paramref name="start"/> in the token list. A token of the form
    /// <c>-name</c> (dash + letter) begins an option; the following non-option tokens are its value
    /// (joined). Negative numeric tokens (<c>-5</c>) are treated as values, not option names.
    /// </summary>
    public static Th2OptionList Parse(ImmutableArray<TherionToken> tokens, int start)
    {
        if (tokens.Length <= start) return Empty;
        var b = ImmutableArray.CreateBuilder<Th2Option>();
        int i = start;
        while (i < tokens.Length)
        {
            if (!IsOptionName(tokens[i].Text)) { i++; continue; }
            var name = tokens[i].Text[1..];
            var nameSpan = tokens[i].Span;
            i++;
            var sb = new System.Text.StringBuilder();
            int valueStart = i;
            int prevEnd = -1;
            while (i < tokens.Length && !IsOptionName(tokens[i].Text))
            {
                // Insert a space only where the source had whitespace, so adjacent tokens the
                // lexer split (4@cave, [10 5], station:fixed) recombine into their original text.
                if (prevEnd >= 0 && tokens[i].Span.StartOffset != prevEnd) sb.Append(' ');
                sb.Append(tokens[i].Text);
                prevEnd = tokens[i].Span.StartOffset + tokens[i].Span.Length;
                i++;
            }
            var span = valueStart < i
                ? SpanUnion(nameSpan, tokens[i - 1].Span)
                : nameSpan;
            b.Add(new Th2Option(name, Unquote(sb.ToString()), span));
        }
        return b.Count == 0 ? Empty : new Th2OptionList(b.ToImmutable());
    }

    private static bool IsOptionName(string t) =>
        t.Length >= 2 && t[0] == '-' && (char.IsLetter(t[1]) || t[1] == '-');

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static SourceSpan SpanUnion(SourceSpan a, SourceSpan b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        int start = Math.Min(a.StartOffset, b.StartOffset);
        int end = Math.Max(a.StartOffset + a.Length, b.StartOffset + b.Length);
        var startLoc = a.StartOffset <= b.StartOffset ? a.Start : b.Start;
        var endLoc = a.StartOffset + a.Length >= b.StartOffset + b.Length ? a.End : b.End;
        return new SourceSpan(a.FilePath, startLoc, endLoc, start, end - start);
    }
}
