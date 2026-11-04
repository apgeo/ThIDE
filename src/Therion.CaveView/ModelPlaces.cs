// Places a model page can offer, read from the model itself: its entrances and the stations the
// surveyors commented. Each is named by the path CaveView gives the station, so the page finds
// every one of them in the model it shows.

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.CaveView;

public static class ModelPlaces
{
    /// <summary>
    /// Entrances first, then commented stations, each once, in file order; at most
    /// <paramref name="max"/>. A commented station is labelled by its comment, an entrance by its
    /// comment or its name, with "entrance" as the note.
    /// </summary>
    public static IReadOnlyList<ModelPagePlace> FromModel(CaveModel model, int max = 100)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paths = new StationPaths(model);
        var places = new List<ModelPagePlace>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(CaveStation s, string? note)
        {
            if (places.Count >= max || (s.Flags & CaveStationFlags.Anonymous) != 0 || string.IsNullOrEmpty(s.Name)) return;
            var parts = paths.Of(s);
            var path = string.Join('.', parts);
            if (!seen.Add(path)) return;
            var comment = string.IsNullOrWhiteSpace(s.Comment) ? null : s.Comment.Trim();
            places.Add(new ModelPagePlace(comment ?? parts[^1], path, note) { StationParts = parts });
        }

        foreach (var s in model.Stations.Where(s => s.IsEntrance)) Add(s, "entrance");
        foreach (var s in model.Stations.Where(s => !string.IsNullOrWhiteSpace(s.Comment))) Add(s, null);
        return places;
    }

    /// <summary>The places of a .lox or .3d file (empty for a format with no reader here, such as .plt).</summary>
    public static IReadOnlyList<ModelPagePlace> FromFile(string path, int max = 100)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".lox" => FromModel(LoxReader.ReadFile(path), max),
            ".3d" => FromModel(Survex3dReader.ReadFile(path), max),
            _ => Array.Empty<ModelPagePlace>(),
        };
    }

    /// <summary>
    /// The dotted path CaveView gives a station. A .3d station is named by its full label already.
    /// A .lox station is named within its survey, and the path runs down CaveView's survey tree to
    /// it. That tree is built as the file's surveys are read, in file order: the file's own top
    /// survey (the one that is its own parent) is left out, and a survey whose parent has not been
    /// read yet is hung from the top - so the path is worked out the same way, not from the
    /// parents alone.
    /// </summary>
    internal sealed class StationPaths
    {
        private readonly CaveModel _model;
        private readonly Dictionary<uint, string[]> _surveyPaths = new();

        public StationPaths(CaveModel model)
        {
            _model = model;
            foreach (var s in model.Surveys)
            {
                if (s.ParentId == s.Id || _surveyPaths.ContainsKey(s.Id)) continue;
                _surveyPaths[s.Id] = _surveyPaths.TryGetValue(s.ParentId, out var parent) ? [.. parent, s.Name] : [s.Name];
            }
        }

        /// <summary>The names of the station's path, top first.</summary>
        public string[] Of(CaveStation station)
        {
            if (_model.SourceFormat != CaveSourceFormat.Lox || station.SurveyId is not { } id)
                return station.Name.Split(_model.SeparatorChar);
            return _surveyPaths.TryGetValue(id, out var survey) ? [.. survey, station.Name] : [station.Name];
        }
    }
}
