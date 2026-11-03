using System.Text.RegularExpressions;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented data. Compass stores passage dimensions in feet like the tape. In Therion the `length`
// quantity covers the tape only; LRUD readings follow the `dimensions` quantity (or `left`, `right`,
// `up`, `down`), which stays in metres unless declared.
public class CompassLrudUnitsTests
{
    [Fact]
    public void Passage_dimensions_are_declared_in_feet()
    {
        var dat = "Invented Cave\r\n" +
                  "SURVEY NAME: A\r\n" +
                  "SURVEY DATE: 7 1 2024\r\n" +
                  "SURVEY TEAM:\r\n" +
                  "Nobody\r\n" +
                  "DECLINATION: 0.00  FORMAT: DDDDLUDRADLN  CORRECTIONS: 0 0 0\r\n" +
                  "\r\n" +
                  "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n" +
                  "\r\n" +
                  "1 2 10.00 0.0 0.0 3.0 6.0 6.0 3.0\r\n";

        var th = CompassImporter.Import(dat);

        var unitsLines = Regex.Matches(th, @"^\s*units\s+(?<q>.*?)\s+feet\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups["q"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();
        bool lrudInFeet = unitsLines.Any(q => q.Contains("dimensions")
            || (q.Contains("left") && q.Contains("right") && q.Contains("up") && q.Contains("down")));

        Assert.True(lrudInFeet, "no `units … feet` line covers the passage dimensions (dimensions, or left/right/up/down); Therion reads the LRUD columns as metres:\n" + th);
    }
}
