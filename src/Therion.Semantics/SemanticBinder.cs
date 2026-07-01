// Implementation Plan �5.2 � bind + resolve passes.
// Walks a TherionFile AST, collecting surveys, stations, shots and equates.
// Builds qualified names by prefixing the current survey scope.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Stateless binder � call <see cref="Bind"/> to produce a <see cref="SemanticModel"/>.</summary>
public sealed class SemanticBinder
{
    public SemanticModel Bind(TherionFile file)
    {
        var ctx = new BindContext();
        WalkChildren(file.Children, ctx, scope: ImmutableArray<string>.Empty,
            dataFields: null);

        // Resolve all equate references; emit TH_SEM_001 for unresolved.
        // Equate groups are resolved as a post-pass so all stations declared
        // anywhere in the file are visible regardless of source order.
        foreach (var group in ctx.EquateGroups)
        {
            QualifiedName? firstResolved = null;
            foreach (var (raw, span, scope) in group)
            {
                var resolved = TryResolveRef(raw, scope, ctx.Stations.Keys);
                if (resolved is null)
                {
                    // This file can't see cross-file / @-qualified targets, so don't warn here —
                    // record the reference for the workspace-level validator, which has cross-file
                    // and @ visibility. (Falls back to a per-file warning when there's no workspace.)
                    ctx.UnresolvedEquateRefs.Add(new EquateRef(raw, span, NearestHint(raw, ctx.Stations.Keys)));
                    continue;
                }
                var st = ctx.Stations[resolved.Value];
                ctx.Stations[resolved.Value] = st with { References = st.References.Add(span) };
                ctx.Equates.Add(resolved.Value);
                if (firstResolved is null) firstResolved = resolved;
                else ctx.Equates.Union(firstResolved.Value, resolved.Value);
            }
        }

        var stations = ctx.Stations.ToFrozenDictionary();
        var surveys = ctx.Surveys.ToFrozenDictionary();
        var scraps = ctx.Scraps.ToFrozenDictionary(System.StringComparer.Ordinal);
        var maps = ctx.Maps.ToFrozenDictionary(System.StringComparer.Ordinal);
        return new SemanticModel(
            stations,
            surveys,
            ctx.Shots.ToImmutable(),
            ctx.Equates,
            ctx.Diagnostics.ToImmutable())
        {
            Scraps = scraps,
            Maps = maps,
            InputCoordinateSystem = ctx.InputCs,
            Declination = ctx.Declination,
            EquateRecords = ctx.EquateRecords.ToImmutable(),
            UnresolvedEquateRefs = ctx.UnresolvedEquateRefs.ToImmutable(),
        };
    }

    /// <summary>
    /// A single-value <c>declination</c> command's value in degrees (east positive), or null for the
    /// reset / dated-list / value-less forms. Grad/mil/minute units are converted to degrees.
    /// </summary>
    private static double? DeclinationToDegrees(DeclinationCommand decl)
    {
        if (decl.IsReset || decl.SingleValue is not { } v) return null;
        var unit = string.IsNullOrWhiteSpace(decl.Unit)
            ? AngleUnit.Degree
            : MeasurementUnits.TryAngle(decl.Unit) ?? AngleUnit.Degree;
        return unit switch
        {
            AngleUnit.Grad => v * 0.9,                 // 400 grad = 360°
            AngleUnit.Mil => v * 360.0 / 6400.0,       // 6400 mil = 360°
            AngleUnit.Minute => v / 60.0,
            _ => v,                                     // Degree (and any non-azimuth unit) → degrees
        };
    }

    // ---- walk ------------------------------------------------------------

    private void WalkChildren(
        ImmutableArray<TherionNode> children,
        BindContext ctx,
        ImmutableArray<string> scope,
        DataCommand? dataFields)
    {
        var currentFields = dataFields;
        // Flags are stateful within a centreline body, and a comment line directly
        // above a data row binds to that row. Both are tracked across this child list.
        var activeFlags = ShotFlags.None;
        // Active angle units for compass/clino (value validation). A `units` command
        // earlier in this body switches degrees↔grads↔… for the range checks that follow.
        var compassUnit = AngleUnit.Degree;
        var clinoUnit = AngleUnit.Degree;
        TrivialComment? pendingComment = null;
        // The survey these direct children belong to (team/date attach here).
        QualifiedName? currentSurvey = scope.IsEmpty ? null : new QualifiedName(scope);
        foreach (var node in children)
        {
            switch (node)
            {
                case TeamCommand team when currentSurvey is { } tsv && !string.IsNullOrWhiteSpace(team.Name):
                    AppendSurveyTeam(ctx, tsv, team.Name);
                    break;
                case DateCommand date when currentSurvey is { } dsv && !string.IsNullOrWhiteSpace(date.Value):
                    AppendSurveyDate(ctx, dsv, date.Value);
                    break;
                case TrivialComment tc:
                    pendingComment = tc;
                    continue; // keep the comment pending; don't reset it below.
                case SurveyCommand sv:
                    BindSurvey(sv, ctx, scope);
                    break;
                case CentrelineCommand cl:
                    // centreline body inherits the enclosing survey scope.
                    BindCentreline(cl, ctx, scope);
                    break;
                case GroupCommand grp:
                    // `group` is transparent: process its children in the current scope,
                    // inheriting the active data-field definition (it may sit in a centreline).
                    WalkChildren(grp.Children, ctx, scope, currentFields);
                    break;
                case StationFix fix:
                    BindFix(fix, ctx, scope);
                    break;
                case EquateCommand eq:
                    BindEquate(eq, ctx, scope);
                    break;
                case FlagsCommand flags:
                    activeFlags = ApplyFlags(activeFlags, flags.Tokens);
                    break;
                case UnitsCommand units:
                    ApplyAngleUnits(units, ref compassUnit, ref clinoUnit);
                    break;
                case DataCommand d:
                    currentFields = d;
                    break;
                case DataRow row when currentFields is not null:
                    ValidateRowArity(row, currentFields, ctx);
                    ValidateRowValues(row, currentFields, ctx, compassUnit, clinoUnit);
                    var leading = AdjacentLeadingComment(pendingComment, row);
                    BindShot(row, currentFields, ctx, scope, activeFlags,
                        CombineComments(leading, row.TrailingComment));
                    break;
                case ScrapBlock scrap:
                    BindScrap(scrap, ctx, scope);
                    break;
                case MapCommand map:
                    BindMap(map, ctx);
                    break;
                case StationCommand st:
                    BindStationCommand(st, ctx, scope);
                    break;
                case MarkCommand mk:
                    BindMark(mk, ctx, scope);
                    break;
                case CsCommand cs:
                    ctx.InputCs ??= cs.System;
                    ctx.CurrentCs = cs.System;
                    break;
                case DeclinationCommand decl:
                    ctx.Declination ??= DeclinationToDegrees(decl);
                    break;
            }
            pendingComment = null;
        }
    }

    /// <summary>
    /// Binds a <c>station &lt;name&gt; "comment" [flags]</c> command: attaches the comment + flags
    /// to the (possibly implicitly declared) station. Does not create a shot.
    /// </summary>
    private static void BindStationCommand(StationCommand st, BindContext ctx, ImmutableArray<string> scope)
    {
        if (string.IsNullOrEmpty(st.Station)) return;
        var qn = QualifyLocal(st.Station, scope);
        var existing = ctx.Stations.TryGetValue(qn, out var s)
            ? s
            : new StationSymbol(qn, st.Span, StationDeclarationKind.Shot, ImmutableArray<SourceSpan>.Empty);
        ctx.Stations[qn] = existing with
        {
            Comment = st.Comment ?? existing.Comment,
            Flags = st.Flags.IsDefaultOrEmpty ? existing.Flags : existing.Flags.AddRange(st.Flags),
        };
        ctx.Equates.Add(qn);
    }

    /// <summary>Appends a team member to the enclosing survey.</summary>
    private static void AppendSurveyTeam(BindContext ctx, QualifiedName survey, string name)
    {
        if (ctx.Surveys.TryGetValue(survey, out var sv))
            ctx.Surveys[survey] = sv with { Team = sv.Team.Add(name) };
    }

    /// <summary>Appends a survey date to the enclosing survey.</summary>
    private static void AppendSurveyDate(BindContext ctx, QualifiedName survey, string date)
    {
        if (ctx.Surveys.TryGetValue(survey, out var sv))
            ctx.Surveys[survey] = sv with { Dates = sv.Dates.Add(date) };
    }

    /// <summary>Binds a <c>mark [&lt;stations&gt;] &lt;type&gt;</c> command, tagging the listed stations.</summary>
    private static void BindMark(MarkCommand mk, BindContext ctx, ImmutableArray<string> scope)
    {
        if (string.IsNullOrEmpty(mk.MarkType)) return;
        foreach (var raw in mk.Stations)
        {
            var qn = QualifyLocal(raw, scope);
            if (!ctx.Stations.TryGetValue(qn, out var s))
                s = new StationSymbol(qn, mk.Span, StationDeclarationKind.Shot, ImmutableArray<SourceSpan>.Empty);
            ctx.Stations[qn] = s with { MarkType = mk.MarkType };
            ctx.Equates.Add(qn);
        }
    }

    /// <summary>Folds a <c>flags [not] name...</c> token list into the active flag set.</summary>
    private static ShotFlags ApplyFlags(ShotFlags active, ImmutableArray<string> tokens)
    {
        bool negate = false;
        foreach (var raw in tokens)
        {
            var t = raw.ToLowerInvariant();
            if (t is "not")
            {
                negate = true;
                continue;
            }
            var flag = t switch
            {
                "surface"                   => ShotFlags.Surface,
                "duplicate"                 => ShotFlags.Duplicate,
                "splay"                     => ShotFlags.Splay,
                "approximate" or "approx"   => ShotFlags.Approximate,
                _                           => ShotFlags.None,
            };
            if (flag != ShotFlags.None)
                active = negate ? active & ~flag : active | flag;
            negate = false;
        }
        return active;
    }

    /// <summary>Returns the comment text only when it sits on the line directly above the row.</summary>
    private static string? AdjacentLeadingComment(TrivialComment? comment, DataRow row)
    {
        if (comment is null) return null;
        if (comment.Span.End.Line != row.Span.Start.Line - 1) return null;
        return CleanComment(comment.Text);
    }

    /// <summary>Strips the leading <c>#</c> and surrounding whitespace from a comment.</summary>
    private static string? CleanComment(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var s = raw.TrimStart();
        if (s.StartsWith('#')) s = s[1..];
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static string? CombineComments(string? leading, string? trailing)
    {
        if (string.IsNullOrEmpty(leading)) return string.IsNullOrEmpty(trailing) ? null : trailing;
        if (string.IsNullOrEmpty(trailing)) return leading;
        return $"{leading} | {trailing}";
    }

    private void BindScrap(ScrapBlock scrap, BindContext ctx, ImmutableArray<string> scope)
    {
        if (!string.IsNullOrEmpty(scrap.Id) && !ctx.Scraps.ContainsKey(scrap.Id))
            ctx.Scraps[scrap.Id] = new ScrapSymbol(scrap.Id, scrap.Span);
        WalkChildren(scrap.Children, ctx, scope, dataFields: null);
    }

    /// <summary>Records a <c>map &lt;id&gt;</c> declaration (first one wins per id), with members.</summary>
    private static void BindMap(MapCommand map, BindContext ctx)
    {
        if (string.IsNullOrEmpty(map.Id) || ctx.Maps.ContainsKey(map.Id)) return;
        var memberIds = map.Members.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : map.Members.Select(m => m.Id).ToImmutableArray();
        ctx.Maps[map.Id] = new MapSymbol(map.Id, map.Span)
        {
            Title = ExtractTitle(map.OptionsRaw),
            Projection = map.Projection,
            Members = memberIds,
        };
    }

    /// <summary>Extracts the value of a <c>-title "..."</c> option (quote-aware) from a raw option string.</summary>
    private static string? ExtractTitle(string optionsRaw)
    {
        if (string.IsNullOrEmpty(optionsRaw)) return null;
        bool wantValue = false;
        foreach (var token in TokenizeOptions(optionsRaw))
        {
            if (wantValue) return token;
            if (string.Equals(token, "-title", StringComparison.OrdinalIgnoreCase)) wantValue = true;
        }
        return null;
    }

    /// <summary>Splits an option string into whitespace- or quote-delimited tokens (quotes stripped).</summary>
    private static IEnumerable<string> TokenizeOptions(string raw)
    {
        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) yield break;
            if (raw[i] == '"')
            {
                int end = raw.IndexOf('"', ++i);
                if (end < 0) { yield return raw[i..]; yield break; }
                yield return raw[i..end];
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                yield return raw[start..i];
            }
        }
    }

    private void BindSurvey(SurveyCommand sv, BindContext ctx, ImmutableArray<string> scope)
    {
        if (string.IsNullOrEmpty(sv.Name)) return;
        var newScope = scope.Add(sv.Name);
        var qn = new QualifiedName(newScope);
        var parent = scope.IsEmpty ? (QualifiedName?)null : new QualifiedName(scope);

        ctx.Surveys[qn] = new SurveySymbol(qn, sv.Span, parent,
            ImmutableArray<QualifiedName>.Empty)
        {
            Title = ExtractTitle(sv.OptionsRaw),
        };

        // attach as child of parent
        if (parent is { } p && ctx.Surveys.TryGetValue(p, out var ps))
            ctx.Surveys[p] = ps with { Children = ps.Children.Add(qn) };

        WalkChildren(sv.Children, ctx, newScope, dataFields: null);
    }

    private void BindCentreline(CentrelineCommand cl, BindContext ctx, ImmutableArray<string> scope)
    {
        WalkChildren(cl.Children, ctx, scope, dataFields: null);
    }

    private void BindFix(StationFix fix, BindContext ctx, ImmutableArray<string> scope)
    {
        var qn = QualifyLocal(fix.Station, scope);
        if (ctx.Stations.TryGetValue(qn, out var existing))
        {
            if (existing.Kind == StationDeclarationKind.Fix)
            {
                ctx.Diagnostics.Add(Diagnostic.Create(
                    SemanticDiagnosticCodes.DuplicateFix,
                    DiagnosticSeverity.Warning,
                    $"Station '{qn}' is fixed more than once.",
                    fix.Span));
            }
            ctx.Stations[qn] = existing with
            {
                Kind = StationDeclarationKind.Fix, DeclarationSpan = fix.Span,
                FixX = fix.X, FixY = fix.Y, FixZ = fix.Z, Cs = ctx.CurrentCs,
            };
        }
        else
        {
            ctx.Stations[qn] = new StationSymbol(qn, fix.Span,
                StationDeclarationKind.Fix, ImmutableArray<SourceSpan>.Empty)
            {
                FixX = fix.X, FixY = fix.Y, FixZ = fix.Z, Cs = ctx.CurrentCs,
            };
        }
        ctx.Equates.Add(qn);
    }

    private void BindEquate(EquateCommand eq, BindContext ctx, ImmutableArray<string> scope)
    {
        var group = new List<(string Raw, SourceSpan Span, ImmutableArray<string> Scope)>(eq.Stations.Length);
        foreach (var raw in eq.Stations)
            group.Add((raw, eq.Span, scope));
        if (group.Count > 0)
        {
            ctx.EquateGroups.Add(group);
            ctx.EquateRecords.Add(new EquateRecord(eq.Stations, eq.Span));
        }
    }

    /// <summary>
    /// warns when a data row supplies the wrong number of columns for its declared
    /// reading order. Skipped for interleaved / <c>ignoreall</c> styles (expected == -1).
    /// </summary>
    private static void ValidateRowArity(DataRow row, DataCommand data, BindContext ctx)
    {
        int expected = DataStyles.ExpectedColumnCount(data.Fields);
        if (expected < 0) return; // interleaved / ignoreall → arity is open
        if (row.Values.Length == expected) return;
        ctx.Diagnostics.Add(Diagnostic.Create(
            SemanticDiagnosticCodes.DataRowArity,
            DiagnosticSeverity.Warning,
            $"Data row has {row.Values.Length} value(s) but the '{data.Style}' reading order " +
            $"expects {expected}.",
            row.Span));
    }

    /// <summary>
    /// validates each value of a data row against the reading declared for its column —
    /// that a number column actually holds a number, and that compass/clino/length values fall in
    /// range. Only runs on fully-determined fixed-arity rows (not interleaved / <c>ignoreall</c>),
    /// so the value↔reading mapping is unambiguous.
    /// </summary>
    private static void ValidateRowValues(DataRow row, DataCommand data, BindContext ctx,
        AngleUnit compassUnit, AngleUnit clinoUnit)
    {
        int expected = DataStyles.ExpectedColumnCount(data.Fields);
        if (expected < 0 || row.Values.Length != expected || data.Fields.Length != row.Values.Length)
            return;

        for (int i = 0; i < row.Values.Length; i++)
        {
            if (DataReadingValidation.CheckValue(data.Fields[i], row.Values[i], compassUnit, clinoUnit)
                is not { } problem)
                continue;

            var span = !row.ValueSpans.IsDefaultOrEmpty && i < row.ValueSpans.Length
                ? row.ValueSpans[i]
                : row.Span;
            ctx.Diagnostics.Add(Diagnostic.Create(
                problem.IsError ? SemanticDiagnosticCodes.DataValueInvalid : SemanticDiagnosticCodes.DataValueRange,
                problem.IsError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                problem.Message,
                span));
        }
    }

    /// <summary>Folds an in-body <c>units</c> command into the active compass/clino angle units.</summary>
    private static void ApplyAngleUnits(UnitsCommand units, ref AngleUnit compassUnit, ref AngleUnit clinoUnit)
    {
        if (MeasurementUnits.TryAngle(units.Unit) is not { } angle) return;
        foreach (var q in units.Quantities)
        {
            switch (q.ToLowerInvariant())
            {
                case "compass" or "bearing" or "backcompass" or "backbearing":
                    compassUnit = angle; break;
                case "clino" or "gradient" or "backclino" or "backgradient":
                    clinoUnit = angle; break;
            }
        }
    }

    private void BindShot(DataRow row, DataCommand data, BindContext ctx, ImmutableArray<string> scope,
        ShotFlags flags, string? comment)
    {
        var (fromIdx, toIdx) = DataStyles.FindFromTo(data.Fields);
        if (fromIdx < 0 || toIdx < 0) return;
        if (row.Values.Length <= Math.Max(fromIdx, toIdx)) return;

        // A '.' or '-' endpoint is not a station: the shot is a splay (one real station + a
        // ray to a feature/wall). Don't create a phantom station for the marker, and tag the
        // shot as a splay so connectivity skips it (it doesn't extend the survey skeleton).
        var fromRaw = row.Values[fromIdx];
        var toRaw   = row.Values[toIdx];
        bool fromSplay = IsSplayMarker(fromRaw);
        bool toSplay   = IsSplayMarker(toRaw);

        var from = fromSplay ? QualifiedName.Of(fromRaw) : QualifyLocal(fromRaw, scope);
        var to   = toSplay   ? QualifiedName.Of(toRaw)   : QualifyLocal(toRaw,   scope);
        if (!fromSplay) DeclareImplicit(ctx, from, row.Span);
        if (!toSplay)   DeclareImplicit(ctx, to,   row.Span);

        var effectiveFlags = fromSplay || toSplay ? flags | ShotFlags.Splay : flags;

        double? length = TryGet(row, data, "length");
        double? compass = TryGet(row, data, "compass");
        double? clino = TryGet(row, data, "clino");
        ctx.Shots.Add(new ShotSymbol(from, to, length, compass, clino, row.Span)
        {
            SourceRow = row,
            FieldDefinition = data,
            Flags = effectiveFlags,
            Comment = comment,
        });
    }

    /// <summary>A '.' (feature) or '-' (wall) endpoint marks the shot as a splay, not a station.</summary>
    private static bool IsSplayMarker(string token) => token is "." or "-";

    private static void DeclareImplicit(BindContext ctx, QualifiedName qn, SourceSpan span)
    {
        if (!ctx.Stations.ContainsKey(qn))
        {
            ctx.Stations[qn] = new StationSymbol(qn, span,
                StationDeclarationKind.Shot, ImmutableArray<SourceSpan>.Empty);
        }
        ctx.Equates.Add(qn);
    }

    private static int IndexOf(ImmutableArray<string> fields, string name)
    {
        for (int i = 0; i < fields.Length; i++)
            if (string.Equals(fields[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static double? TryGet(DataRow row, DataCommand data, string field)
    {
        int i = IndexOf(data.Fields, field);
        if (i < 0 || i >= row.Values.Length) return null;
        return double.TryParse(row.Values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }

    // ---- resolution ------------------------------------------------------

    /// <summary>
    /// Builds a qualified name for a locally-declared station <paramref name="token"/> relative to
    /// the current survey <paramref name="scope"/>. The token is a station name and is kept whole —
    /// a '.' inside it is a literal character (e.g. <c>N32.11</c>), not a survey separator. Therion
    /// expresses cross-survey references with the <c>@</c> notation (handled by <see cref="StationRef"/>),
    /// not by dotted station tokens.
    /// </summary>
    private static QualifiedName QualifyLocal(string token, ImmutableArray<string> scope)
        => QualifiedName.OfStation(scope, token);

    /// <summary>
    /// Therion-style ancestor lookup: try the reference in the local scope first, then walk outward.
    /// Station names may contain '.', so the <b>whole</b> token is tried first (so <c>N32.11</c> is
    /// not split); only if that resolves nowhere do we fall back to the legacy top-down dotted-path
    /// interpretation (<c>survey.survey.station</c>), which keeps any such reference working.
    /// </summary>
    private static QualifiedName? TryResolveRef(
        string raw, ImmutableArray<string> scope,
        IReadOnlyCollection<QualifiedName> known)
    {
        // Primary: treat `raw` as one whole station name (dots are literal).
        if (TryResolveWith(new[] { raw }, scope, known) is { } whole) return whole;
        // Fallback: the raw carried '.' and did not resolve whole — try the dotted-path split.
        return raw.Contains('.') ? TryResolveWith(raw.Split('.'), scope, known) : null;
    }

    /// <summary>Walks the scope from innermost to root, testing <paramref name="parts"/> appended at each depth.</summary>
    private static QualifiedName? TryResolveWith(
        string[] parts, ImmutableArray<string> scope, IReadOnlyCollection<QualifiedName> known)
    {
        for (int depth = scope.Length; depth >= 0; depth--)
        {
            var candidate = ImmutableArray.CreateRange(scope, 0, depth, x => x).AddRange(parts);
            var qn = new QualifiedName(candidate);
            if (known.Contains(qn)) return qn;
        }
        return null;
    }

    private static string? NearestHint(string raw, IReadOnlyCollection<QualifiedName> known)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var k in known)
        {
            int d = Levenshtein(raw, k.ToString());
            if (d < bestDist) { bestDist = d; best = k.ToString(); }
        }
        return (best is not null && bestDist <= Math.Max(2, raw.Length / 3))
            ? $"did you mean '{best}'?"
            : null;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur  = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    // ---- mutable context -------------------------------------------------

    private sealed class BindContext
    {
        public Dictionary<QualifiedName, StationSymbol> Stations { get; } = new();
        public Dictionary<QualifiedName, SurveySymbol> Surveys { get; } = new();
        public Dictionary<string, ScrapSymbol> Scraps { get; } = new(System.StringComparer.Ordinal);
        public Dictionary<string, MapSymbol> Maps { get; } = new(System.StringComparer.Ordinal);
        public ImmutableArray<ShotSymbol>.Builder Shots { get; } = ImmutableArray.CreateBuilder<ShotSymbol>();
        public EquateGraph Equates { get; } = new();
        public ImmutableArray<Diagnostic>.Builder Diagnostics { get; } = ImmutableArray.CreateBuilder<Diagnostic>();
        public List<List<(string Raw, SourceSpan Span, ImmutableArray<string> Scope)>> EquateGroups { get; } = new();
        public ImmutableArray<EquateRecord>.Builder EquateRecords { get; } = ImmutableArray.CreateBuilder<EquateRecord>();
        /// <summary>Equate references unresolved in this file (re-checked at the workspace level).</summary>
        public ImmutableArray<EquateRef>.Builder UnresolvedEquateRefs { get; } = ImmutableArray.CreateBuilder<EquateRef>();
        /// <summary>First <c>cs</c> declared in the file (input coordinate system), if any.</summary>
        public string? InputCs { get; set; }
        /// <summary>The <c>cs</c> in force at the current point of the walk (for fix coords).</summary>
        public string? CurrentCs { get; set; }
        /// <summary>First single-value <c>declination</c> in the file, in degrees east-positive.</summary>
        public double? Declination { get; set; }
    }
}
