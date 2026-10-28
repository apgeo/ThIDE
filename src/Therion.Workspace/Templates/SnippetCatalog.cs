// Snippets come in three layers, later ones overriding earlier ones by trigger: the built-ins, the
// user's own file (app data) and the project's (`.thide/snippets.json`, shared with the cave).

using System.Text.Json;

namespace Therion.Workspace.Templates;

/// <summary>Where a snippet applies: the file kind it is offered in.</summary>
public enum SnippetScope { Any, Th, Th2, Thconfig }

/// <param name="DescriptionKey">For built-ins: a resource key the app translates; null for user snippets.</param>
public sealed record SnippetDefinition(
    string Trigger, string Description, string Template, SnippetScope Scope = SnippetScope.Any, string? DescriptionKey = null)
{
    /// <summary>Whether the snippet is offered in a file with this name.</summary>
    public bool AppliesTo(string? filePath) => Scope switch
    {
        SnippetScope.Th => filePath?.EndsWith(".th", StringComparison.OrdinalIgnoreCase) ?? true,
        SnippetScope.Th2 => filePath?.EndsWith(".th2", StringComparison.OrdinalIgnoreCase) ?? false,
        SnippetScope.Thconfig => filePath is not null && SnippetCatalog.IsThconfig(filePath),
        _ => true,
    };
}

/// <summary>A snippet file layer: a name for messages and its JSON text (null when the file is absent).</summary>
public sealed record SnippetLayer(string Name, string? Json);

public static class SnippetCatalog
{
    private sealed class Dto
    {
        public string? Trigger { get; set; }
        public string? Description { get; set; }
        public string? Template { get; set; }
        public string? Scope { get; set; }
    }

    /// <summary>
    /// Merges the layers over the built-ins. A file that does not parse, or an entry whose template
    /// does not, is reported and skipped; the rest of the layer still applies.
    /// </summary>
    public static (IReadOnlyList<SnippetDefinition> Snippets, IReadOnlyList<TemplateError> Errors) Load(
        IEnumerable<SnippetDefinition> builtIns, params SnippetLayer[] layers)
    {
        var map = new Dictionary<string, SnippetDefinition>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<TemplateError>();
        foreach (var s in builtIns) map[s.Trigger] = s;

        foreach (var layer in layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Json)) continue;
            List<Dto>? dtos;
            try { dtos = JsonSerializer.Deserialize<List<Dto>>(layer.Json, ProjectLocalConfig.JsonOptions); }
            catch (JsonException ex)
            {
                errors.Add(new TemplateError((int)(ex.LineNumber ?? 0) + 1, ex.Message, layer.Name));
                continue;
            }
            foreach (var d in dtos ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.Trigger) || d.Template is null)
                {
                    errors.Add(new TemplateError(0, "an entry needs a trigger and a template", layer.Name));
                    continue;
                }
                var (_, error) = SnippetTemplate.Parse(d.Template);
                if (error is not null)
                {
                    errors.Add(error with { Source = $"{layer.Name} ({d.Trigger})" });
                    continue;
                }
                map[d.Trigger] = new SnippetDefinition(d.Trigger, d.Description ?? d.Trigger, d.Template, ParseScope(d.Scope));
            }
        }
        return (map.Values.OrderBy(s => s.Trigger, StringComparer.OrdinalIgnoreCase).ToList(), errors);
    }

    private static SnippetScope ParseScope(string? scope) => scope?.ToLowerInvariant() switch
    {
        "th" => SnippetScope.Th,
        "th2" => SnippetScope.Th2,
        "thconfig" => SnippetScope.Thconfig,
        _ => SnippetScope.Any,
    };

    internal static bool IsThconfig(string path) =>
        path.EndsWith(".thconfig", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".thc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(path), "thconfig", StringComparison.OrdinalIgnoreCase);

    /// <summary>The snippets ThIDE ships. Descriptions are resource keys the app translates.</summary>
    public static IReadOnlyList<SnippetDefinition> BuiltIns { get; } =
    [
        new("survb", "survey … endsurvey block",
            "survey ${1:name} -title \"$2\"\n  centreline\n    date $DATE\n    team \"$3\"\n    data normal from to length compass clino\n    $0\n  endcentreline\nendsurvey",
            SnippetScope.Th, "Snippet_survb"),
        new("clt", "centreline + data normal",
            "centreline\n  date $DATE\n  ${TEAM_RECENT:team \"\"}\n  ${LAST_DATA:data normal from to length compass clino}\n  $0\nendcentreline",
            SnippetScope.Th, "Snippet_clt"),
        new("datan", "data normal reading order",
            "data normal from to length compass clino$0", SnippetScope.Th, "Snippet_datan"),
        new("leg", "next leg from the last station",
            "${LAST_STATION:1} ${NEXT_STATION:2} ${1:length} ${2:compass} ${3:clino}$0", SnippetScope.Th, "Snippet_leg"),
        new("eq", "equate two stations",
            "equate ${1:station} ${2:station}$0", SnippetScope.Th, "Snippet_eq"),
        new("inp", "input another file",
            "input ${1:file.th}$0", SnippetScope.Any, "Snippet_inp"),
        new("scrapb", "scrap … endscrap block",
            "scrap ${1:s1} -projection ${2|plan,elevation,extended|} -scale [0 0 1 0 0 0 1 0 m]\n  $0\nendscrap",
            SnippetScope.Any, "Snippet_scrapb"),
        new("mapb", "map … endmap block",
            "map ${1:m1}\n  $0\nendmap", SnippetScope.Any, "Snippet_mapb"),
        new("thconf", "thconfig skeleton",
            "encoding  utf-8\nsource ${1:main.th}\n\nlayout ${2:layout1}\n  $0\nendlayout\n\nselect ${3:cave}\nexport map -layout $2 -output ${4:map.pdf}",
            SnippetScope.Thconfig, "Snippet_thconf"),
        // Fragments: thconfig blocks.
        new("layout-plan-500", "plan layout at 1:500",
            "layout ${1:plan500}\n  scale 1 500\n  grid bottom\n  north grid\n  $0\nendlayout", SnippetScope.Thconfig, "Snippet_layoutPlan500"),
        new("layout-copy", "layout based on another",
            "layout ${1:mine}\n  copy ${2:base}\n  $0\nendlayout", SnippetScope.Thconfig, "Snippet_layoutCopy"),
        new("export-plan-pdf", "export a plan map to PDF",
            "export map -projection plan -layout ${1:layout1} -output ${2:plan.pdf}$0", SnippetScope.Thconfig, "Snippet_exportPlanPdf"),
        new("export-model-lox", "export the 3D model",
            "export model -output ${1:model.lox}$0", SnippetScope.Thconfig, "Snippet_exportModelLox"),
    ];
}
