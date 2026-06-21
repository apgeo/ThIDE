// Parser for Therion's `@` cross-reference notation, shared by the editor (to
// decide what the user clicked) and the workspace resolver (to find the target).
//
// The rule (thbook §"survey structure"): `point@inner.outer` is read bottom-up
// after the `@`, and is equivalent to the top-down dotted form `outer.inner.point`
// that QualifiedName / SemanticBinder already use. So we reverse the components
// after the `@` to get a top-down survey path.

using System;
using System.Collections.Immutable;

namespace Therion.Semantics;

/// <summary>Which part of a <c>point@survey</c> token a caret/click landed on.</summary>
public enum ReferencePart
{
    /// <summary>The id before the <c>@</c> (the station / map / scrap-object).</summary>
    Point,
    /// <summary>The survey path after the <c>@</c>.</summary>
    Survey,
    /// <summary>No <c>@</c> present — the whole token is a single id.</summary>
    Whole,
}

/// <summary>
/// A parsed Therion reference of the form <c>point@inner.outer</c> (or a bare
/// <c>point</c>). <see cref="SurveyPathTopDown"/> is the survey path in top-down
/// (outer→inner) order, ready to compose with <see cref="QualifiedName"/>.
/// </summary>
public readonly record struct StationRef(string Point, ImmutableArray<string> SurveyPathTopDown)
{
    /// <summary>True when the reference carried an <c>@survey</c> part.</summary>
    public bool HasSurvey => !SurveyPathTopDown.IsDefaultOrEmpty && SurveyPathTopDown.Length > 0;

    /// <summary>Last (innermost) survey component, or <c>null</c> when bare.</summary>
    public string? SurveyLastName => HasSurvey ? SurveyPathTopDown[^1] : null;

    /// <summary>Top-down dotted survey path (<c>outer.inner</c>), or <c>null</c> when bare.</summary>
    public string? SurveyQuery => HasSurvey ? string.Join('.', SurveyPathTopDown) : null;

    /// <summary>Top-down dotted station path (<c>outer.inner.point</c>), or just the point when bare.</summary>
    public string StationQuery =>
        HasSurvey ? string.Concat(string.Join('.', SurveyPathTopDown), ".", Point) : Point;

    /// <summary>Parses <c>point@inner.outer</c> into point + reversed (top-down) survey path.</summary>
    public static StationRef Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new StationRef(string.Empty, ImmutableArray<string>.Empty);

        int at = raw.IndexOf('@');
        if (at < 0)
            return new StationRef(raw, ImmutableArray<string>.Empty);

        var point = raw[..at];
        var after = raw[(at + 1)..];
        if (after.Length == 0)
            return new StationRef(point, ImmutableArray<string>.Empty);

        // Bottom-up after '@' → reverse to top-down so it matches QualifiedName order.
        var parts = after.Split('.');
        Array.Reverse(parts);
        return new StationRef(point, ImmutableArray.Create(parts));
    }

    /// <summary>
    /// Given a raw token and a caret offset within it, returns which part was hit.
    /// Offsets on/left of the <c>@</c> count as the point; offsets to its right as
    /// the survey.
    /// </summary>
    public static (ReferencePart Part, StationRef Ref) ClassifyClick(string token, int offsetInToken)
    {
        var parsed = Parse(token);
        int at = token.IndexOf('@');
        if (at < 0) return (ReferencePart.Whole, parsed);
        return offsetInToken <= at ? (ReferencePart.Point, parsed) : (ReferencePart.Survey, parsed);
    }

    /// <summary>The point id with any trailing <c>:mark</c> (used by <c>join</c>) removed.</summary>
    public string PointWithoutMark
    {
        get
        {
            int colon = Point.IndexOf(':');
            return colon < 0 ? Point : Point[..colon];
        }
    }
}
