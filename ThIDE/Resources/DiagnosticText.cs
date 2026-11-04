// Diagnostic messages and hints in the UI language. The Therion.* libraries stay English: each
// diagnostic carries its invariant English Message/Hint. The app holds one template per message
// shape in Strings.resx — Diag_Msg_<code>, Diag_Msg_<code>__<variant>, Diag_Hint_<code>[__<variant>] —
// written exactly as the library builds the English text, with the interpolated parts as {0}, {1}, …
// The English template is matched against the English text to recover those parts, which are then
// formatted into the localized template. No template, or one that does not reproduce the English
// text exactly, falls back to the library's English.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Core;

namespace ThIDE.Resources;

public static class DiagnosticText
{
    internal const string MessagePrefix = "Diag_Msg_";
    internal const string HintPrefix = "Diag_Hint_";
    private const string VariantSeparator = "__";

    private sealed record Template(string Key, string English, Regex Pattern, int Literal);

    // code → its templates, most specific (longest literal text) first. Built once from the English resx.
    private static readonly Lazy<(Dictionary<string, Template[]> Messages, Dictionary<string, Template[]> Hints)> Catalog =
        new(Load);

    private static readonly ConcurrentDictionary<(string Code, string Text, bool Hint, string Culture), string> Cache = new();

    /// <summary>The diagnostic's message in the UI language, or its English <see cref="Diagnostic.Message"/>.</summary>
    public static string Message(Diagnostic d) => Translate(d.Code.Value, d.Message, hint: false);

    /// <summary>The diagnostic's hint in the UI language, its English hint, or null when it has none.</summary>
    public static string? Hint(Diagnostic d) =>
        string.IsNullOrEmpty(d.Hint) ? d.Hint : Translate(d.Code.Value, d.Hint, hint: true);

    /// <summary>The editor hover text: "Severity: message", and the hint on a second line.</summary>
    public static string Tooltip(Diagnostic d)
    {
        var hint = Hint(d);
        var severity = Tr.Get("Diag_Sev_" + d.Severity);
        return string.IsNullOrEmpty(hint) ? $"{severity}: {Message(d)}" : $"{severity}: {Message(d)}\n{hint}";
    }

    /// <summary>
    /// <paramref name="english"/> (a library message or hint of <paramref name="code"/>) in the UI language;
    /// <paramref name="english"/> itself when no template reproduces it.
    /// </summary>
    public static string Translate(string code, string english, bool hint)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(english)) return english;
        var table = hint ? Catalog.Value.Hints : Catalog.Value.Messages;
        if (!table.TryGetValue(code, out var templates)) return english;
        if (Cache.Count > 20_000) Cache.Clear();
        return Cache.GetOrAdd((code, english, hint, Tr.Culture.Name), _ => Format(templates, english));
    }

    /// <summary>The template keys (both prefixes) whose code is <paramref name="code"/>.</summary>
    internal static IEnumerable<string> KeysFor(string code) =>
        (Catalog.Value.Messages.TryGetValue(code, out var m) ? m : Array.Empty<Template>())
        .Concat(Catalog.Value.Hints.TryGetValue(code, out var h) ? h : Array.Empty<Template>())
        .Select(t => t.Key);

    /// <summary>The code a template key belongs to (the key without its prefix and variant).</summary>
    internal static string CodeOf(string key)
    {
        var rest = key.StartsWith(MessagePrefix, StringComparison.Ordinal) ? key[MessagePrefix.Length..] : key[HintPrefix.Length..];
        int v = rest.IndexOf(VariantSeparator, StringComparison.Ordinal);
        return v < 0 ? rest : rest[..v];
    }

    private static string Format(Template[] templates, string english)
    {
        foreach (var t in templates)
        {
            var m = t.Pattern.Match(english);
            if (!m.Success) continue;
            var args = new List<object>();
            for (int i = 0; m.Groups["a" + i.ToString(CultureInfo.InvariantCulture)] is { Success: true } g; i++)
                args.Add(g.Value);
            try
            {
                // The parts must rebuild the English text exactly, or they were not split as the library joined them.
                if (!string.Equals(string.Format(CultureInfo.InvariantCulture, t.English, args.ToArray()), english, StringComparison.Ordinal))
                    continue;
                var localized = Tr.Find(t.Key);
                return string.IsNullOrEmpty(localized)
                    ? english
                    : string.Format(Tr.Culture, localized, args.ToArray());
            }
            catch (FormatException) { return english; }
        }
        return english;
    }

    private static (Dictionary<string, Template[]>, Dictionary<string, Template[]>) Load()
    {
        var messages = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
        var hints = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
        var set = Tr.Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        if (set is not null)
        {
            foreach (System.Collections.DictionaryEntry e in set)
            {
                if (e.Key is not string key || e.Value is not string english) continue;
                var table = key.StartsWith(MessagePrefix, StringComparison.Ordinal) ? messages
                    : key.StartsWith(HintPrefix, StringComparison.Ordinal) ? hints
                    : null;
                if (table is null || ToPattern(english) is not { } p) continue;
                var code = CodeOf(key);
                if (!table.TryGetValue(code, out var list)) table[code] = list = new();
                list.Add(new Template(key, english, p.Pattern, p.Literal));
            }
        }
        static Dictionary<string, Template[]> Sorted(Dictionary<string, List<Template>> d) =>
            d.ToDictionary(kv => kv.Key,
                kv => kv.Value.OrderByDescending(t => t.Literal).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return (Sorted(messages), Sorted(hints));
    }

    /// <summary>
    /// The anchored regex for a composite-format template: literal text escaped, each {n} a lazy named
    /// group (a repeated index must repeat the same text). Null for a malformed template.
    /// </summary>
    internal static (Regex Pattern, int Literal)? ToPattern(string template)
    {
        var sb = new StringBuilder("^");
        var seen = new HashSet<int>();
        int literal = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append(Regex.Escape("{")); literal++; i++; continue; }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append(Regex.Escape("}")); literal++; i++; continue; }
            if (c == '{')
            {
                int close = template.IndexOf('}', i);
                if (close < 0 || !int.TryParse(template.AsSpan(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                    return null;
                sb.Append(seen.Add(n) ? $"(?<a{n}>.*?)" : $"\\k<a{n}>");
                i = close;
                continue;
            }
            if (c == '}') return null;
            sb.Append(Regex.Escape(c.ToString()));
            literal++;
        }
        // Group indexes must run 0..n-1 so Format can read them in order.
        for (int k = 0; k < seen.Count; k++) if (!seen.Contains(k)) return null;
        sb.Append('$');
        return (new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), literal);
    }
}
