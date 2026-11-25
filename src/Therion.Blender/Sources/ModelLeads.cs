// Leads without a project. The application takes its leads from the workspace's leads register;
// whoever has only a model file (the command line) has two sources that are honest about what
// they know: the stations the model itself flags as continuations, which a .lox file carries, and
// a small JSON file that lists stations.

using System.Text.Json;
using Therion.Cave3D;

namespace Therion.Blender.Sources;

/// <summary>Leads for a render that has a model file and no project.</summary>
public static class ModelLeads
{
    /// <summary>The kind of a lead a model's own flag gives.</summary>
    public const string ContinuationKind = "continuation";

    /// <summary>
    /// The stations <paramref name="model"/> flags as continuations, as leads. A <c>.lox</c>
    /// file carries the flag (Therion's <c>station ... continuation</c>); a <c>.3d</c> file has
    /// none, and gives no leads. Each is named with its surveys before it, top down
    /// ("cave.branch.7"), which is how <see cref="LeadsEnricher"/> tells two stations "7" of two
    /// surveys apart; its note is the station's comment.
    /// </summary>
    public static IReadOnlyList<SourceLead> FromModel(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var surveys = new Dictionary<uint, CaveSurvey>();
        foreach (var survey in model.Surveys) surveys.TryAdd(survey.Id, survey);
        var leads = new List<SourceLead>();
        foreach (var station in model.Stations)
        {
            if ((station.Flags & CaveStationFlags.Continuation) == 0 || string.IsNullOrEmpty(station.Name)) continue;
            string path = station.SurveyId is { } id ? SurveyPath(id, surveys) : "";
            leads.Add(new SourceLead(
                path.Length == 0 ? station.Name : path + "." + station.Name, ContinuationKind,
                string.IsNullOrWhiteSpace(station.Comment) ? null : station.Comment.Trim()));
        }
        return leads;
    }

    private static string SurveyPath(uint id, Dictionary<uint, CaveSurvey> surveys)
    {
        var names = new List<string>();
        var seen = new HashSet<uint>();
        // The root survey is its own parent; the guard also stops a malformed cycle.
        while (surveys.TryGetValue(id, out var survey) && seen.Add(id))
        {
            if (survey.Name.Length > 0) names.Add(survey.Name);
            if (survey.ParentId == id) break;
            id = survey.ParentId;
        }
        names.Reverse();
        return string.Join('.', names);
    }

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record LeadJson(string? Station, string? Kind, string? Note);

    /// <summary>
    /// Reads a leads file: a JSON array (or an object with a <c>leads</c> array) whose entries
    /// are a station's name, or an object with <c>station</c> and, when wanted, <c>kind</c> and
    /// <c>note</c>. The note is what a lead's caption shows.
    /// </summary>
    /// <exception cref="FormatException">The text is not such a file. The message names the
    /// entry; it quotes nothing of the file.</exception>
    public static IReadOnlyList<SourceLead> Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new FormatException("not valid JSON: " + JsonFault.NotJson(ex));
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("leads", out var inner)) root = inner;
            if (root.ValueKind != JsonValueKind.Array)
                throw new FormatException("a leads file is a JSON array of leads, or an object with a \"leads\" array.");
            var leads = new List<SourceLead>();
            int index = 0;
            foreach (var entry in root.EnumerateArray())
            {
                string where = $"leads[{index++}]";
                LeadJson? lead;
                if (entry.ValueKind == JsonValueKind.String) lead = new LeadJson(entry.GetString(), null, null);
                else if (entry.ValueKind == JsonValueKind.Object)
                {
                    try { lead = entry.Deserialize<LeadJson>(FileOptions); }
                    catch (JsonException) { throw new FormatException($"{where}: \"station\", \"kind\" and \"note\" are texts."); }
                }
                else throw new FormatException($"{where}: a lead is a station's name, or an object with \"station\".");
                if (string.IsNullOrWhiteSpace(lead?.Station))
                    throw new FormatException($"{where}.station: a lead names its station.");
                leads.Add(new SourceLead(
                    lead.Station.Trim(), string.IsNullOrWhiteSpace(lead.Kind) ? ContinuationKind : lead.Kind.Trim(),
                    string.IsNullOrWhiteSpace(lead.Note) ? null : lead.Note.Trim()));
            }
            return leads;
        }
    }

    /// <summary><see cref="Read"/> of a file.</summary>
    public static IReadOnlyList<SourceLead> ReadFile(string path) => Read(File.ReadAllText(path));
}
