// Fragments are thconfig-scoped snippets: reusable layout/export blocks a club keeps in the
// project's snippets.json. Saving one adds (or replaces, by trigger) an entry in that file while
// leaving everything else in it as it was.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Workspace.Templates;

public static class FragmentStore
{
    /// <summary>
    /// The snippet file's text with <paramref name="fragment"/> added, or replacing the entry with the
    /// same trigger. Null (or blank) means the file does not exist yet. Returns null with an error
    /// when the existing file is not a JSON array — it is left alone rather than overwritten.
    /// </summary>
    public static (string? Json, string? Error) Append(string? existingJson, string trigger, string description, string template)
    {
        JsonArray array;
        if (string.IsNullOrWhiteSpace(existingJson)) array = new JsonArray();
        else
        {
            try
            {
                if (JsonNode.Parse(existingJson, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                    is not JsonArray parsed)
                    return (null, "the snippet file is not a list");
                array = parsed;
            }
            catch (JsonException ex) { return (null, ex.Message); }
        }

        for (int i = array.Count - 1; i >= 0; i--)
            if (array[i] is JsonObject o && string.Equals((string?)o["trigger"], trigger, StringComparison.OrdinalIgnoreCase))
                array.RemoveAt(i);
        array.Add(new JsonObject
        {
            ["trigger"] = trigger,
            ["description"] = description,
            ["template"] = template,
            ["scope"] = "thconfig",
        });
        return (array.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", null);
    }

    /// <summary>Thconfig text made into a template that inserts it verbatim ('$' and '\' escaped).</summary>
    public static string TemplateFromText(string text) => text.Replace("\\", "\\\\").Replace("$", "\\$");
}
