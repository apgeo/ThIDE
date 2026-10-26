// Selector language shared with the out-of-process AT-SPI driver (tools/uidrive): `#Id`, `name:Text`,
// `name~:regex`, `role:Button`, `type:TextBox`, `text~:regex`, `state:selected`, or a bare string
// (= exact name). Terms joined by `;` must all hold, ` >> ` chains descendant steps, and a trailing
// `@n` picks the n-th match (0-based). Pure, so it is unit-tested without a UI.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ThIDE.Services.UiAutomation;

/// <summary>A snapshot of one automation element, as the selector and the formatters see it.</summary>
public sealed record UiElementInfo(
    string Role,
    string Type,
    string Name,
    string Id,
    string? Text,
    IReadOnlyCollection<string> States,
    (int X, int Y, int Width, int Height)? Bounds);

public sealed class UiSelector
{
    private readonly IReadOnlyList<IReadOnlyList<Term>> _steps;

    private UiSelector(IReadOnlyList<IReadOnlyList<Term>> steps, int? index, string source)
    {
        _steps = steps;
        Index = index;
        Source = source;
    }

    /// <summary>The descendant steps; an element must match the last one, inside matches of the earlier ones.</summary>
    public int StepCount => _steps.Count;

    /// <summary>The n-th match to pick (trailing <c>@n</c>), or null for all matches.</summary>
    public int? Index { get; }

    public string Source { get; }

    public bool MatchesStep(int step, UiElementInfo e) => _steps[step].All(t => t.Matches(e));

    public static UiSelector Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Empty selector.");
        var src = text.Trim();
        int? index = null;
        var at = Regex.Match(src, @"@(\d+)$");
        if (at.Success)
        {
            index = int.Parse(at.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            src = src[..at.Index].TrimEnd();
        }

        var steps = new List<IReadOnlyList<Term>>();
        foreach (var part in src.Split(" >> "))
        {
            var terms = new List<Term>();
            foreach (var raw in part.Split(';').Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                if (raw.StartsWith('#')) { terms.Add(new Term("id", raw[1..])); continue; }
                var m = Regex.Match(raw, @"^(id|name~|name|role|type|text~|state):(.*)$", RegexOptions.Singleline);
                terms.Add(m.Success ? new Term(m.Groups[1].Value, m.Groups[2].Value) : new Term("name", raw));
            }
            if (terms.Count == 0) throw new FormatException($"Empty selector step in '{text}'.");
            steps.Add(terms);
        }
        return new UiSelector(steps, index, text);
    }

    private sealed record Term(string Key, string Value)
    {
        private readonly Regex? _regex = Key.EndsWith('~') ? new Regex(Value, RegexOptions.CultureInvariant) : null;

        public bool Matches(UiElementInfo e) => Key switch
        {
            "id" => e.Id == Value,
            "name" => e.Name == Value,
            "name~" => _regex!.IsMatch(e.Name),
            "role" => string.Equals(e.Role, Value, StringComparison.OrdinalIgnoreCase),
            "type" => string.Equals(e.Type, Value, StringComparison.OrdinalIgnoreCase),
            "text~" => _regex!.IsMatch(e.Text ?? ""),
            "state" => e.States.Contains(Value, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
