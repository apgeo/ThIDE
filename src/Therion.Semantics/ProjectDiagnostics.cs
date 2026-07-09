// project-wide correctness diagnostics computed from a bound
// WorkspaceSemanticModel. These are "preview-quality" checks from our own model (not a full
// Therion/Survex adjustment): loop misclosure, blunder/outlier shots, foresight/backsight
// consistency, cross-file naming collisions, and dangling include targets. Angles are assumed to
// be in degrees (the overwhelmingly common case); grad-unit projects may see softer loop figures.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>Tunable thresholds for <see cref="ProjectDiagnostics"/>.</summary>
public sealed class ProjectDiagnosticOptions
{
    /// <summary>Loop misclosure beyond this fraction of the loop length is flagged.</summary>
    public double LoopMisclosureWarnFraction { get; init; } = 0.04;
    /// <summary>Loops whose misclosure is below this many metres are never flagged (noise floor).</summary>
    public double LoopMisclosureFloorMetres { get; init; } = 0.5;
    /// <summary>Legs longer than this (metres) are flagged as suspicious (possible transcription error).</summary>
    public double MaxPlausibleLegMetres { get; init; } = 300;
    /// <summary>Allowed foresight↔backsight compass disagreement (degrees) before flagging.</summary>
    public double ForeBackCompassToleranceDeg { get; init; } = 2.5;
    /// <summary>Allowed foresight↔backsight clino disagreement (degrees) before flagging.</summary>
    public double ForeBackClinoToleranceDeg { get; init; } = 2.5;
    /// <summary>Cap on the number of loop-misclosure diagnostics emitted (worst first).</summary>
    public int MaxLoopsReported { get; init; } = 100;
    /// <summary>Cap on the number of disconnected-piece diagnostics emitted (largest first).</summary>
    public int MaxDisconnectedReported { get; init; } = 100;

    public bool EnableLoopClosure { get; init; } = true;
    public bool EnableBlunders { get; init; } = true;
    public bool EnableForeBack { get; init; } = true;
    public bool EnableDuplicates { get; init; } = true;
    public bool EnableDangling { get; init; } = true;
    /// <summary>Flag disconnected, ungrounded survey pieces (floating mainlines).</summary>
    public bool EnableDisconnection { get; init; } = true;
    /// <summary>
    /// When true, a bare <c>fix</c> (one made without a <c>cs</c>) also grounds a piece and so
    /// suppresses its disconnection warning. Off by default: a local <c>fix 0 0 0</c> is only a
    /// placeholder origin, not an absolute anchor, so by default it does not exempt the piece.
    /// </summary>
    public bool LocalFixGrounds { get; init; }

    public static ProjectDiagnosticOptions Default { get; } = new();
}

/// <summary>Workspace-level correctness analysis.</summary>
public static class ProjectDiagnostics
{
    private readonly record struct Vec3(double E, double N, double Z)
    {
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.E + b.E, a.N + b.N, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.E - b.E, a.N - b.N, a.Z - b.Z);
        public double Magnitude => Math.Sqrt(E * E + N * N + Z * Z);
    }

    public static ImmutableArray<Diagnostic> Analyze(
        WorkspaceSemanticModel workspace,
        ProjectDiagnosticOptions? options = null,
        Func<string, bool>? fileExists = null)
    {
        var o = options ?? ProjectDiagnosticOptions.Default;
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        if (o.EnableDuplicates) DuplicateDeclarations(workspace, diags);
        if (o.EnableBlunders || o.EnableForeBack) ShotChecks(workspace, o, diags);
        if (o.EnableLoopClosure) LoopClosure(workspace, o, diags);
        if (o.EnableDisconnection) Disconnection(workspace, o, diags);
        if (o.EnableDangling && fileExists is not null) DanglingIncludes(workspace, fileExists, diags);

        return diags.ToImmutable();
    }

    // ---- : cross-file naming collisions (surveys / maps) -------------------------------
    // Per the spec note, station names are unique only *per survey*, so reusing a station name in
    // different surveys is fine and is never flagged. Surveys and maps, however, should be unique.

    private static void DuplicateDeclarations(WorkspaceSemanticModel ws, ImmutableArray<Diagnostic>.Builder diags)
    {
        CollisionsOf(ws.PerFile.Values.SelectMany(m => m.Surveys.Values.Select(s => (s.Name.ToString(), s.DeclarationSpan))),
            "survey", diags);
        CollisionsOf(ws.PerFile.Values.SelectMany(m => m.Maps.Values.Select(s => (s.Id, s.DeclarationSpan))),
            "map", diags);
    }

    private static void CollisionsOf(IEnumerable<(string Name, SourceSpan Span)> decls, string kind,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        foreach (var group in decls.Where(d => !string.IsNullOrEmpty(d.Name))
                                   .GroupBy(d => d.Name, StringComparer.Ordinal))
        {
            var spans = group.Select(g => g.Span).ToList();
            // A genuine collision spans more than one distinct file.
            int files = spans.Select(s => s.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (files < 2) continue;
            foreach (var span in spans)
                diags.Add(Diagnostic.Create(
                    SemanticDiagnosticCodes.DuplicateDeclaration,
                    DiagnosticSeverity.Info,
                    $"Naming collision: {kind} '{group.Key}' is declared in {files} files; {kind} names should be unique across the project.",
                    span));
        }
    }

    // ---- / : per-shot blunder + foresight/backsight checks --------------------

    private static void ShotChecks(WorkspaceSemanticModel ws, ProjectDiagnosticOptions o,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        foreach (var model in ws.PerFile.Values)
            foreach (var shot in model.Shots)
            {
                bool splay = (shot.Flags & ShotFlags.Splay) != 0;

                if (o.EnableBlunders && !splay)
                {
                    if (shot.From.Equals(shot.To))
                        diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.ShotOutlier, DiagnosticSeverity.Warning,
                            $"Shot's from and to are the same station '{shot.From}'.", shot.Span));
                    else if (shot.Length is { } len)
                    {
                        if (len <= 0.0001)
                            diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.ShotOutlier, DiagnosticSeverity.Warning,
                                $"Zero-length leg between distinct stations {shot.From} → {shot.To}.", shot.Span));
                        else if (len > o.MaxPlausibleLegMetres)
                            diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.ShotOutlier, DiagnosticSeverity.Info,
                                $"Unusually long leg ({len.ToString("0.#", CultureInfo.InvariantCulture)} m) {shot.From} → {shot.To}; check for a transcription error.",
                                shot.Span));
                    }
                }

                if (o.EnableForeBack && !splay) ForeBack(shot, o, diags);
            }
    }

    private static void ForeBack(ShotSymbol shot, ProjectDiagnosticOptions o, ImmutableArray<Diagnostic>.Builder diags)
    {
        var fore = Reading(shot, "compass", "bearing");
        var back = Reading(shot, "backcompass", "backbearing");
        if (fore is { } fc && back is { } bc)
        {
            double diff = Math.Abs(NormalizeDeg(bc - (fc + 180)));
            if (diff > o.ForeBackCompassToleranceDeg)
                diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.ForeBackMismatch, DiagnosticSeverity.Warning,
                    $"Foresight/backsight compass disagree by {diff.ToString("0.#", CultureInfo.InvariantCulture)}° " +
                    $"(fore {Fmt(fc)}, back {Fmt(bc)}, expected ~{Fmt(NormalizeDeg(fc + 180))}).", shot.Span));
        }

        var foreI = Reading(shot, "clino", "gradient");
        var backI = Reading(shot, "backclino", "backgradient");
        if (foreI is { } fi && backI is { } bi)
        {
            double diff = Math.Abs(bi - (-fi));
            if (diff > o.ForeBackClinoToleranceDeg)
                diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.ForeBackMismatch, DiagnosticSeverity.Warning,
                    $"Foresight/backsight clino disagree by {diff.ToString("0.#", CultureInfo.InvariantCulture)}° " +
                    $"(fore {Fmt(fi)}, back {Fmt(bi)}, expected ~{Fmt(-fi)}).", shot.Span));
        }
    }

    // ---- : loop-closure misclosure ----------------------------------------------------

    private static void LoopClosure(WorkspaceSemanticModel ws, ProjectDiagnosticOptions o,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // Merge equate classes across files so loops that close via @-equates are seen.
        var equates = new EquateGraph();
        foreach (var model in ws.PerFile.Values)
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++) equates.Union(group[0], group[i]);

        // Edge list (a→b with its 3-D vector) over equate-merged nodes; skip splays + incomplete shots.
        var edges = new List<(QualifiedName A, QualifiedName B, Vec3 V, double Len, SourceSpan Span)>();
        var adj = new Dictionary<QualifiedName, List<int>>();
        void Link(QualifiedName n, int e) => (adj.TryGetValue(n, out var l) ? l : adj[n] = new()).Add(e);

        foreach (var model in ws.PerFile.Values)
            foreach (var shot in model.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                if (shot.Length is not { } len || shot.Compass is not { } c || shot.Clino is not { } cl) continue;
                var a = equates.Find(shot.From);
                var b = equates.Find(shot.To);
                if (a.Equals(b)) continue;
                int idx = edges.Count;
                edges.Add((a, b, Vector(len, c, cl), len, shot.Span));
                Link(a, idx); Link(b, idx);
            }
        if (edges.Count == 0) return;

        var pos = new Dictionary<QualifiedName, Vec3>();
        var depth = new Dictionary<QualifiedName, int>();
        var parent = new Dictionary<QualifiedName, QualifiedName>();
        var parentLen = new Dictionary<QualifiedName, double>();
        var visited = new HashSet<QualifiedName>();
        var treeEdge = new HashSet<int>();

        // BFS spanning forest, assigning each node a position vector from its component root.
        foreach (var root in adj.Keys)
        {
            if (!visited.Add(root)) continue;
            pos[root] = default; depth[root] = 0;
            var queue = new Queue<QualifiedName>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var e in adj[u])
                {
                    var (ea, eb, v, len, _) = edges[e];
                    var w = ea.Equals(u) ? eb : ea;
                    if (visited.Contains(w)) continue;
                    visited.Add(w);
                    treeEdge.Add(e);
                    // Vector u→w (flip the stored a→b vector if u is the 'b' end).
                    pos[w] = pos[u] + (ea.Equals(u) ? v : Negate(v));
                    depth[w] = depth[u] + 1;
                    parent[w] = u;
                    parentLen[w] = len;
                    queue.Enqueue(w);
                }
            }
        }

        // Each non-tree edge closes exactly one fundamental loop; its misclosure is the residual.
        var loops = new List<(double Err, double Len, double Frac, SourceSpan Span)>();
        for (int e = 0; e < edges.Count; e++)
        {
            if (treeEdge.Contains(e)) continue;
            var (a, b, v, len, span) = edges[e];
            if (!pos.ContainsKey(a) || !pos.ContainsKey(b)) continue;
            var residual = (pos[a] + v) - pos[b];
            double err = residual.Magnitude;
            double loopLen = len + TreePathLength(a, b, parent, parentLen, depth);
            if (loopLen <= 0) continue;
            double frac = err / loopLen;
            if (err >= o.LoopMisclosureFloorMetres && frac >= o.LoopMisclosureWarnFraction)
                loops.Add((err, loopLen, frac, span));
        }

        foreach (var (err, len, frac, span) in loops.OrderByDescending(l => l.Frac).Take(o.MaxLoopsReported))
            diags.Add(Diagnostic.Create(
                SemanticDiagnosticCodes.LoopMisclosure,
                DiagnosticSeverity.Warning,
                $"Loop misclosure {err.ToString("0.##", CultureInfo.InvariantCulture)} m over a " +
                $"{len.ToString("0.#", CultureInfo.InvariantCulture)} m loop " +
                $"({(frac * 100).ToString("0.#", CultureInfo.InvariantCulture)}% error) — check this loop for a blunder.",
                span));
    }

    private static double TreePathLength(QualifiedName a, QualifiedName b,
        Dictionary<QualifiedName, QualifiedName> parent, Dictionary<QualifiedName, double> parentLen,
        Dictionary<QualifiedName, int> depth)
    {
        double total = 0;
        int guard = 0;
        // Raise the deeper node until both depths match.
        while (depth.TryGetValue(a, out var da) && depth.TryGetValue(b, out var db) && da != db && guard++ < 100000)
        {
            if (da > db) { total += parentLen[a]; a = parent[a]; }
            else { total += parentLen[b]; b = parent[b]; }
        }
        // Then climb both together until they meet at the LCA.
        while (!a.Equals(b) && parent.ContainsKey(a) && parent.ContainsKey(b) && guard++ < 100000)
        {
            total += parentLen[a] + parentLen[b];
            a = parent[a]; b = parent[b];
        }
        return total;
    }

    // ---- : disconnected (ungrounded) survey pieces ------------------------------------
    // The whole project should form ONE connected network, or several pieces each independently
    // georeferenced by a `fix` under a coordinate system. A piece that is neither joined to the main
    // network (by a shared or `equate`d station) nor anchored to absolute coordinates has no defined
    // position relative to the rest of the cave — it "floats". We report each such piece with the
    // files it spans and its two farthest-apart stations. The largest piece is treated as the main
    // network / reference frame and is never reported; a piece holding a georeferenced `fix` is exempt
    // (a bare `fix 0 0 0` with no `cs` is only a local placeholder and does NOT ground the piece).

    private static void Disconnection(WorkspaceSemanticModel ws, ProjectDiagnosticOptions o,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // Equate union-find over station names, merging BOTH the per-file equate classes AND cross-file
        // `@`-equates (resolved through the workspace), so a cave stitched together across files is seen
        // as a single piece rather than wrongly reported as many disconnected ones.
        var equates = new EquateGraph();
        foreach (var model in ws.PerFile.Values)
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++) equates.Union(group[0], group[i]);
        foreach (var model in ws.PerFile.Values)
            foreach (var rec in model.EquateRecords)
            {
                QualifiedName? first = null;
                foreach (var raw in rec.Stations)
                {
                    if (ResolveEquateMember(ws, model, raw) is not { } qn) continue;
                    if (first is null) first = qn;
                    else equates.Union(first.Value, qn);
                }
            }

        // Nodes = equate-merged stations; index each rep's declaring files + grounded state, and keep a
        // station-name → symbol map for the navigable diagnostic anchor.
        var adjacency = new Dictionary<QualifiedName, HashSet<QualifiedName>>();
        var symbolByName = new Dictionary<QualifiedName, StationSymbol>();
        var filesByRep = new Dictionary<QualifiedName, HashSet<string>>();
        var groundedReps = new HashSet<QualifiedName>();

        HashSet<QualifiedName> Adj(QualifiedName n) =>
            adjacency.TryGetValue(n, out var s) ? s : adjacency[n] = new HashSet<QualifiedName>();
        HashSet<string> Files(QualifiedName rep) =>
            filesByRep.TryGetValue(rep, out var s) ? s : filesByRep[rep] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in ws.PerFile.Values)
            foreach (var st in model.Stations.Values)
            {
                var rep = equates.Find(st.Name);
                Adj(rep);
                symbolByName.TryAdd(st.Name, st);
                if (!string.IsNullOrEmpty(st.DeclarationSpan.FilePath))
                    Files(rep).Add(st.DeclarationSpan.FilePath);
                // A georeferenced fix (one made under a `cs`) grounds the piece to absolute coordinates;
                // a bare `fix` with no coordinate system is only a local placeholder — it grounds the
                // piece only when the caller opts in via LocalFixGrounds.
                if (st.Kind == StationDeclarationKind.Fix &&
                    (o.LocalFixGrounds || !string.IsNullOrWhiteSpace(st.Cs)))
                    groundedReps.Add(rep);
            }

        // Edges from non-splay legs (splays reach wall points, not the survey skeleton).
        foreach (var model in ws.PerFile.Values)
            foreach (var shot in model.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                var a = equates.Find(shot.From);
                var b = equates.Find(shot.To);
                if (a.Equals(b)) continue;
                Adj(a).Add(b);
                Adj(b).Add(a);
            }

        // Connected components over the merged graph.
        var seen = new HashSet<QualifiedName>();
        var components = new List<List<QualifiedName>>();
        foreach (var startNode in adjacency.Keys)
        {
            if (!seen.Add(startNode)) continue;
            var members = new List<QualifiedName> { startNode };
            var queue = new Queue<QualifiedName>();
            queue.Enqueue(startNode);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var next in adjacency[cur])
                    if (seen.Add(next)) { members.Add(next); queue.Enqueue(next); }
            }
            components.Add(members);
        }
        if (components.Count <= 1) return;   // fully connected — nothing floats.

        // Largest first (ties: smallest member name). The largest piece is the reference frame.
        components.Sort(static (x, y) =>
        {
            int bySize = y.Count.CompareTo(x.Count);
            return bySize != 0 ? bySize : string.CompareOrdinal(MinName(x), MinName(y));
        });

        // Report every non-main piece that is a real mainline (≥1 leg ⇒ ≥2 merged nodes) and is not
        // grounded by a georeferenced fix. Largest first, capped.
        int reported = 0;
        for (int ci = 1; ci < components.Count && reported < o.MaxDisconnectedReported; ci++)
        {
            var comp = components[ci];
            if (comp.Count < 2) continue;                                // lone station — not a mainline
            bool grounded = false;
            foreach (var n in comp) if (groundedReps.Contains(n)) { grounded = true; break; }
            if (grounded) continue;

            reported++;
            EmitDisconnected(comp, adjacency, symbolByName, filesByRep, diags);
        }
    }

    private static string MinName(List<QualifiedName> c)
    {
        string min = c[0].ToString();
        for (int i = 1; i < c.Count; i++)
        {
            var s = c[i].ToString();
            if (string.CompareOrdinal(s, min) < 0) min = s;
        }
        return min;
    }

    private static void EmitDisconnected(
        List<QualifiedName> comp,
        Dictionary<QualifiedName, HashSet<QualifiedName>> adjacency,
        Dictionary<QualifiedName, StationSymbol> symbolByName,
        Dictionary<QualifiedName, HashSet<string>> filesByRep,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var memberSet = new HashSet<QualifiedName>(comp);

        // Two farthest-apart stations (graph diameter via double BFS) = the piece's natural ends.
        // Seed from the smallest name so the endpoints are deterministic.
        var seed = comp[0];
        foreach (var n in comp)
            if (string.CompareOrdinal(n.ToString(), seed.ToString()) < 0) seed = n;
        var p = FarthestNode(seed, memberSet, adjacency);
        var q = FarthestNode(p, memberSet, adjacency);
        var (startRep, endRep) = string.CompareOrdinal(p.ToString(), q.ToString()) <= 0 ? (p, q) : (q, p);

        // Files the piece spans (basenames, sorted).
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var n in comp)
            if (filesByRep.TryGetValue(n, out var fs))
                foreach (var f in fs) files.Add(System.IO.Path.GetFileName(f));

        // Legs inside the piece (each undirected edge counted once).
        int legs = 0;
        foreach (var n in comp)
            if (adjacency.TryGetValue(n, out var nb)) legs += nb.Count;
        legs /= 2;

        var span = SpanFor(startRep, comp, symbolByName);

        diags.Add(Diagnostic.Create(
            SemanticDiagnosticCodes.DisconnectedSurvey,
            DiagnosticSeverity.Warning,
            $"Disconnected survey: this piece ({comp.Count} stations, {legs} leg{(legs == 1 ? "" : "s")}; " +
            $"{FormatFiles(files)}) is not connected to the rest of the survey and is not georeferenced by a " +
            $"fix. It runs from station '{startRep}' to '{endRep}'. Join it with an 'equate' to an adjacent " +
            $"station, or anchor it with a 'fix' under a 'cs'.",
            span));
    }

    /// <summary>The farthest node from <paramref name="src"/> within <paramref name="allowed"/> (BFS by hops).</summary>
    private static QualifiedName FarthestNode(QualifiedName src, HashSet<QualifiedName> allowed,
        Dictionary<QualifiedName, HashSet<QualifiedName>> adjacency)
    {
        var dist = new Dictionary<QualifiedName, int> { [src] = 0 };
        var queue = new Queue<QualifiedName>();
        queue.Enqueue(src);
        var best = src; int bestD = 0;
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var v in adjacency[u])
            {
                if (!allowed.Contains(v) || dist.ContainsKey(v)) continue;
                int dv = dist[u] + 1;
                dist[v] = dv;
                // Break distance ties by smallest name so the chosen end is deterministic.
                if (dv > bestD || (dv == bestD && string.CompareOrdinal(v.ToString(), best.ToString()) < 0))
                { bestD = dv; best = v; }
                queue.Enqueue(v);
            }
        }
        return best;
    }

    /// <summary>Declaration span of <paramref name="preferred"/> (else any member) for a navigable anchor.</summary>
    private static SourceSpan SpanFor(QualifiedName preferred, List<QualifiedName> comp,
        Dictionary<QualifiedName, StationSymbol> symbolByName)
    {
        if (symbolByName.TryGetValue(preferred, out var s) && !s.DeclarationSpan.IsEmpty)
            return s.DeclarationSpan;
        foreach (var n in comp)
            if (symbolByName.TryGetValue(n, out var s2) && !s2.DeclarationSpan.IsEmpty)
                return s2.DeclarationSpan;
        return SourceSpan.None;
    }

    private static string FormatFiles(SortedSet<string> files)
    {
        if (files.Count == 0) return "unknown file";
        const int max = 4;
        return files.Count <= max
            ? string.Join(", ", files)
            : string.Join(", ", files.Take(max)) + $", +{files.Count - max} more";
    }

    /// <summary>
    /// Resolves an <c>equate</c> member (as written in source) to the canonical station name it refers
    /// to, spanning files: cross-file / <c>@</c>-qualified / full-dotted names via the workspace resolver,
    /// then a bare or relative name against its own file (exact, else a unique last-name match).
    /// </summary>
    private static QualifiedName? ResolveEquateMember(WorkspaceSemanticModel ws, SemanticModel model, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (ws.ResolveStationSymbol(raw) is { } sym) return sym.Name;

        var r = StationRef.Parse(raw);
        if (!r.HasSurvey)
        {
            var direct = QualifiedName.Of(r.Point);
            if (model.Stations.ContainsKey(direct)) return direct;
        }
        // A unique last-name match inside this one file (station names are effectively unique per
        // survey; requiring uniqueness avoids guessing across surveys that reuse a name).
        QualifiedName? unique = null;
        foreach (var name in model.Stations.Keys)
            if (string.Equals(name.Last, r.Point, StringComparison.Ordinal))
            {
                if (unique is not null) return null;
                unique = name;
            }
        return unique;
    }

    // ---- : dangling include targets ---------------------------------------------------

    private static void DanglingIncludes(WorkspaceSemanticModel ws, Func<string, bool> fileExists,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // Deduplicate per (including file, target): the same missing file referenced from two places
        // is two problems, and each `source`/`input` line deserves its own squiggle.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to, site) in ws.FileGraphEdges)
        {
            if (string.IsNullOrEmpty(to) || !seen.Add(from + "\0" + to)) continue;
            if (fileExists(to)) continue;
            diags.Add(Diagnostic.Create(
                SemanticDiagnosticCodes.DanglingReference,
                DiagnosticSeverity.Warning,
                $"File not found: '{to}', referenced by {System.IO.Path.GetFileName(from)}.",
                SiteOrTopOf(from, site)));
        }
    }

    /// <summary>
    /// The span of the <c>source</c>/<c>input</c> command, or a one-character span at the top of the
    /// including file for an edge that carries none. Never returns an empty span: consumers treat
    /// <see cref="SourceSpan.IsEmpty"/> as "not navigable" and would refuse to jump to it.
    /// </summary>
    private static SourceSpan SiteOrTopOf(string from, SourceSpan site)
    {
        if (!site.IsEmpty) return site;
        return new SourceSpan(from, new SourceLocation(1, 1), new SourceLocation(1, 2), 0, 1);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static Vec3 Vector(double length, double compassDeg, double clinoDeg)
    {
        double cl = clinoDeg * Math.PI / 180.0;
        double cd = compassDeg * Math.PI / 180.0;
        double horiz = length * Math.Cos(cl);
        return new Vec3(horiz * Math.Sin(cd), horiz * Math.Cos(cd), length * Math.Sin(cl));
    }

    private static Vec3 Negate(Vec3 v) => new(-v.E, -v.N, -v.Z);

    /// <summary>Reads a numeric reading from a shot's source row by field name(s), if present.</summary>
    private static double? Reading(ShotSymbol shot, params string[] names)
    {
        if (shot.SourceRow is not { } row || shot.FieldDefinition is not { } data) return null;
        for (int i = 0; i < data.Fields.Length && i < row.Values.Length; i++)
        {
            foreach (var name in names)
                if (string.Equals(data.Fields[i], name, StringComparison.OrdinalIgnoreCase))
                    return double.TryParse(row.Values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
        return null;
    }

    private static double NormalizeDeg(double deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg < -180) deg += 360;
        return deg;
    }

    private static string Fmt(double deg) => NormalizeAngle(deg).ToString("0.#", CultureInfo.InvariantCulture) + "°";
    private static double NormalizeAngle(double deg) { deg %= 360; if (deg < 0) deg += 360; return deg; }
}
