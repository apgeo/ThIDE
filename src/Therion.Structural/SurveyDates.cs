// The survey date a batch was measured at, as a decimal year: what the WMM-auto declination is
// evaluated at. A batch belongs to the survey of its from-station; a survey without a `date` takes the
// nearest enclosing survey's.

using System;
using System.Globalization;
using Therion.Semantics;

namespace Therion.Structural;

public static class SurveyDates
{
    /// <summary>
    /// The decimal year of the first parseable date of the survey <paramref name="station"/> lies in,
    /// else of the nearest enclosing survey that has one; null when none of them is dated.
    /// </summary>
    public static double? DecimalYearOf(SemanticModel model, QualifiedName station)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (station.Parts.IsDefaultOrEmpty) return null;
        for (var survey = station; survey.HasParent;)
        {
            survey = survey.Parent();
            if (model.Surveys.TryGetValue(survey, out var symbol))
                foreach (var date in symbol.Dates)
                    if (TryParseDecimalYear(date, out var year)) return year;
        }
        return null;
    }

    /// <summary>The decimal year of the first parseable date of any survey in <paramref name="model"/>; null when none.</summary>
    public static double? FirstDecimalYear(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (var s in model.Surveys.Values)
            foreach (var d in s.Dates)
                if (TryParseDecimalYear(d, out var y)) return y;
        return null;
    }

    /// <summary>
    /// Reads the start of a Therion <c>date</c> value (<c>1998.07.12</c>, <c>1998.07</c>, <c>1998</c>, a
    /// range <c>1998.07.12 - 1998.07.14</c>) as a year plus the month's fraction.
    /// </summary>
    public static bool TryParseDecimalYear(string? raw, out double year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var first = raw.Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length == 0) return false;
        var parts = first[0].Split('.');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var yr) || yr < 1000 || yr > 3000)
            return false;
        double frac = 0;
        if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mo) && mo is >= 1 and <= 12)
            frac = (mo - 1) / 12.0;
        year = yr + frac;
        return true;
    }
}
