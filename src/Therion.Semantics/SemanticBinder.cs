// Implementation Plan �5.2 � bind + resolve passes.
// Walks a TherionFile AST, collecting surveys, stations, shots and equates.
// Builds qualified names by prefixing the current survey scope.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
                    ctx.Diagnostics.Add(Diagnostic.Create(
                        SemanticDiagnosticCodes.UnresolvedStation,
                        DiagnosticSeverity.Warning,
                        $"Unresolved station reference '{raw}'.",
                        span,
                        hint: NearestHint(raw, ctx.Stations.Keys)));
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
        TrivialComment? pendingComment = null;
        foreach (var node in children)
        {
            switch (node)
            {
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
                case DataCommand d:
                    currentFields = d;
                    break;
                case DataRow row when currentFields is not null:
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
            }
            pendingComment = null;
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

    /// <summary>Records a <c>map &lt;id&gt;</c> declaration (first one wins per id).</summary>
    private static void BindMap(MapCommand map, BindContext ctx)
    {
        if (!string.IsNullOrEmpty(map.Id) && !ctx.Maps.ContainsKey(map.Id))
            ctx.Maps[map.Id] = new MapSymbol(map.Id, map.Span) { Title = ExtractTitle(map.OptionsRaw) };
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
            ctx.Stations[qn] = existing with { Kind = StationDeclarationKind.Fix, DeclarationSpan = fix.Span };
        }
        else
        {
            ctx.Stations[qn] = new StationSymbol(qn, fix.Span,
                StationDeclarationKind.Fix, ImmutableArray<SourceSpan>.Empty);
        }
        ctx.Equates.Add(qn);
    }

    private void BindEquate(EquateCommand eq, BindContext ctx, ImmutableArray<string> scope)
    {
        var group = new List<(string Raw, SourceSpan Span, ImmutableArray<string> Scope)>(eq.Stations.Length);
        foreach (var raw in eq.Stations)
            group.Add((raw, eq.Span, scope));
        if (group.Count > 0) ctx.EquateGroups.Add(group);
    }

    private void BindShot(DataRow row, DataCommand data, BindContext ctx, ImmutableArray<string> scope,
        ShotFlags flags, string? comment)
    {
        int fromIdx = IndexOf(data.Fields, "from");
        int toIdx   = IndexOf(data.Fields, "to");
        if (fromIdx < 0 || toIdx < 0) return;
        if (row.Values.Length <= Math.Max(fromIdx, toIdx)) return;

        var from = QualifyLocal(row.Values[fromIdx], scope);
        var to   = QualifyLocal(row.Values[toIdx],   scope);
        DeclareImplicit(ctx, from, row.Span);
        DeclareImplicit(ctx, to,   row.Span);

        double? length = TryGet(row, data, "length");
        double? compass = TryGet(row, data, "compass");
        double? clino = TryGet(row, data, "clino");
        ctx.Shots.Add(new ShotSymbol(from, to, length, compass, clino, row.Span)
        {
            SourceRow = row,
            FieldDefinition = data,
            Flags = flags,
            Comment = comment,
        });
    }

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
    /// Builds a qualified name for a token relative to the current survey scope.
    /// If the token contains '.', it's treated as an absolute (top-down) reference.
    /// </summary>
    private static QualifiedName QualifyLocal(string token, ImmutableArray<string> scope)
    {
        if (token.Contains('.'))
            return QualifiedName.Parse(token);
        if (scope.IsEmpty)
            return QualifiedName.Of(token);
        return new QualifiedName(scope.Add(token));
    }

    /// <summary>
    /// Therion-style ancestor lookup: try local scope first, then walk outward.
    /// </summary>
    private static QualifiedName? TryResolveRef(
        string raw, ImmutableArray<string> scope,
        IReadOnlyCollection<QualifiedName> known)
    {
        var parts = raw.Split('.');
        // 1. local scope candidate
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
    }
}
