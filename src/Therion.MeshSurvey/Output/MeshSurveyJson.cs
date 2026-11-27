// JSON for the settings, the guide and the report: camelCase, enums as words, infinities allowed (a
// clearance with nothing near is +∞). Settings read from JSON start from the defaults, so a file need
// only name what it changes.

using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.MeshSurvey.Plan;

namespace Therion.MeshSurvey.Output;

public static class MeshSurveyJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static MeshSurveyOptions ReadOptions(string json) =>
        JsonSerializer.Deserialize<MeshSurveyOptions>(json, Options) ?? new MeshSurveyOptions();

    public static SurveyGuide ReadGuide(string json) =>
        JsonSerializer.Deserialize<SurveyGuide>(json, Options) ?? SurveyGuide.None;
}
