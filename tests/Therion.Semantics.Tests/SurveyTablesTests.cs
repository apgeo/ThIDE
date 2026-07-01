using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

// station / shot tables projected from the workspace model.
public class SurveyTablesTests
{
    private static WorkspaceSemanticModel Build(string src, string path = "cave.th")
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, src) };
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    private const string Src = """
        survey cave
          centreline
            data normal from to length compass clino
              1 2 10.5 90 0
              2 3 5 100 -3
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Shots_table_has_rows_with_measurements()
    {
        var (headers, rows) = SurveyTables.ShotsTable(Build(Src));
        Assert.Equal(new[] { "From", "To", "Length", "Compass", "Clino", "Flags", "File", "Line" }, headers);
        Assert.Equal(2, rows.Count);
        // First shot's length/compass are formatted invariantly.
        Assert.Contains(rows, r => r[2] == "10.5" && r[3] == "90");
    }

    [Fact]
    public void Stations_table_lists_stations_sorted()
    {
        var (headers, rows) = SurveyTables.StationsTable(Build(Src));
        Assert.Equal("Station", headers[0]);
        Assert.NotEmpty(rows);
        var names = rows.Select(r => r[0]).ToList();
        Assert.Equal(names.OrderBy(n => n, System.StringComparer.Ordinal), names);   // sorted
    }
}
