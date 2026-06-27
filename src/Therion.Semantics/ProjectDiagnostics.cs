// DIAG-02/03/04/05/06 — project-wide correctness diagnostics computed from a bound
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

    public bool EnableLoopClosure { get; init; } = true;
    public bool EnableBlunders { get; init; } = true;
    public bool EnableForeBack { get; init; } = true;
    public bool EnableDuplicates { get; init; } = true;
    public bool EnableDangling { get; init; } = true;

    public static ProjectDiagnosticOptions Default { get; } = new();
}

/// <summary>Workspace-level correctness analysis (DIAG-02..06).</summary>
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
        if (o.EnableDangling && fileExists is not null) DanglingIncludes(workspace, fileExists, diags);

        return diags.ToImmutable();
    }

    // ---- DIAG-05: cross-file naming collisions (surveys / maps) -------------------------------
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

    // ---- DIAG-03 / DIAG-04: per-shot blunder + foresight/backsight checks --------------------

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

    // ---- DIAG-02: loop-closure misclosure ----------------------------------------------------

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

    // ---- DIAG-06: dangling include targets ---------------------------------------------------

    private static void DanglingIncludes(WorkspaceSemanticModel ws, Func<string, bool> fileExists,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in ws.FileGraphEdges)
        {
            if (string.IsNullOrEmpty(to) || !seen.Add(to)) continue;
            if (fileExists(to)) continue;
            // Point the diagnostic at the top of the including file (navigable in the panel).
            var loc = new SourceLocation(1, 1);
            diags.Add(Diagnostic.Create(
                SemanticDiagnosticCodes.DanglingReference,
                DiagnosticSeverity.Warning,
                $"Dangling include: '{to}' (referenced by {System.IO.Path.GetFileName(from)}) was not found.",
                new SourceSpan(from, loc, loc, 0, 0)));
        }
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
