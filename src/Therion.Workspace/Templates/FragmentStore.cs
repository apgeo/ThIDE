// Fragments are thconfig-scoped snippets: reusable layout/export blocks a club keeps in the
// project's snippets.json. Saving one adds (or replaces, by trigger) an entry in that file while
// leaving everything else in it as it was.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Workspace.Templates;

public static class FragmentStore
{
    /// <summary>
    /// The snippet file's text with the fragment added, or replacing the entry with the same trigger.
    /// Null (or blank) means the file does not exist yet. A new trigger is inserted as text before
    /// the closing bracket, so comments and layout survive; replacing an entry rewrites the file (and
    /// drops its comments). Returns null with an error when the file is not a JSON array — it is left
    /// alone rather than overwritten.
    /// </summary>
    public static (string? Json, string? Error) Append(string? existingJson, string trigger, string description, string template)
    {
        JsonArray array;
        if (string.IsNullOrWhiteSpace(existingJson)) array = new JsonArray();
        else
        {
            try
            {
                if (JsonNode.Parse(existingJson, documentOptions: ReadOptions)
                    is not JsonArray parsed)
                    return (null, "the snippet file is not a list");
                array = parsed;
            }
            catch (JsonException ex) { return (null, ex.Message); }
        }

        bool replacing = false;
        for (int i = array.Count - 1; i >= 0; i--)
            if (TriggerOf(array[i]) is { } t && string.Equals(t, trigger, StringComparison.OrdinalIgnoreCase))
            {
                array.RemoveAt(i);
                replacing = true;
            }
        var entry = new JsonObject
        {
            ["trigger"] = trigger,
            ["description"] = description,
            ["template"] = template,
            ["scope"] = "thconfig",
        };
        if (!replacing && !string.IsNullOrWhiteSpace(existingJson) && existingJson.LastIndexOf(']') is var close and > 0)
        {
            var head = existingJson[..close].TrimEnd();
            var separator = head.EndsWith('[') || head.EndsWith(',') ? "" : ",";
            var text = entry.ToJsonString(Options).Replace("\n", "\n  ");
            var spliced = head + separator + "\n  " + text + "\n" + existingJson[close..];
            // The last ']' or the text before it may sit inside a comment; keep the splice only when
            // it reads back as the same list plus the new entry, else rewrite the list.
            if (ParsesAsAppended(spliced, array.Count, trigger)) return (spliced, null);
        }
        array.Add(entry);
        return (array.ToJsonString(Options) + "\n", null);
    }

    private static bool ParsesAsAppended(string json, int countBefore, string trigger)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: ReadOptions) is JsonArray a
                && a.Count == countBefore + 1
                && TriggerOf(a[^1]) == trigger;
        }
        catch (JsonException) { return false; }
    }

    private static readonly JsonDocumentOptions ReadOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Property names are matched like the loader does (any case); a non-string trigger is no match.
    private static string? TriggerOf(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        foreach (var (key, value) in o)
            if (string.Equals(key, "trigger", StringComparison.OrdinalIgnoreCase)
                && value is JsonValue v && v.TryGetValue<string>(out var s))
                return s;
        return null;
    }

    /// <summary>Thconfig text made into a template that inserts it verbatim ('$' and '\' escaped).</summary>
    public static string TemplateFromText(string text) => text.Replace("\\", "\\\\").Replace("$", "\\$");
}
