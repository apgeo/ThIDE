// Phase 2 — finds structural-measurement shots in the object graph and groups them into
// candidate planes. Multi-signal (name keyword / comment marker / station flag), configurable grouping,
// splay policy, and the synthetic origin row (decisions 1 & 5). Pure: consumes a SemanticModel + a
// CenterlineSolution (for world positions); produces batches the facade then fits.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;

namespace Therion.Structural;

public static class GeoStructureDetector
{
    /// <summary>
    /// Detects and groups structural measurements. <paramref name="solution"/> supplies world positions
    /// (so planes co-locate with the cave line); pass null to get local-frame batches only.
    /// </summary>
    public static ImmutableArray<StructuralBatch> Detect(
        SemanticModel model, DetectionOptions options, CenterlineSolution? solution = null)
    {
        var equates = model.Equates;

        // Pass 1: classify each shot; keep candidates with full geometry.
        var candidates = new List<(ShotSymbol Shot, DetectionSignal Sig, string Key, bool IsSplay)>();
        foreach (var shot in model.Shots)
        {
            var sig = Classify(shot, options, model);
            if (sig == DetectionSignal.None) continue;
            if (shot.Length is null || shot.Compass is null || shot.Clino is null) continue; // can't place a point

            bool isSplay = (shot.Flags & ShotFlags.Splay) != 0;
            if (options.Splays == SplayPolicy.OnlySplays && !isSplay) continue;

            candidates.Add((shot, sig, GroupKey(shot, options, equates), isSplay));
        }

        // Pass 2: group. ByFromStation → consecutive runs of equal key; tag modes → global by key.
        var batches = ImmutableArray.CreateBuilder<StructuralBatch>();
        if (options.Grouping == GroupingMode.ByFromStation)
        {
            int i = 0;
            while (i < candidates.Count)
            {
                int j = i + 1;
                while (j < candidates.Count && candidates[j].Key == candidates[i].Key) j++;
                batches.Add(BuildBatch(candidates.GetRange(i, j - i), options, equates, solution));
                i = j;
            }
        }
        else
        {
            var order = new List<string>();
            var groups = new Dictionary<string, List<(ShotSymbol, DetectionSignal, string, bool)>>();
            foreach (var c in candidates)
            {
                if (!groups.TryGetValue(c.Key, out var list))
                {
                    groups[c.Key] = list = new();
                    order.Add(c.Key);
                }
                list.Add(c);
            }
            foreach (var key in order)
                batches.Add(BuildBatch(groups[key], options, equates, solution));
        }

        return batches.ToImmutable();
    }

    // ---- classification ------------------------------------------------------------------------

    private static DetectionSignal Classify(ShotSymbol shot, DetectionOptions o, SemanticModel model)
    {
        var sig = DetectionSignal.None;

        if (!o.NameKeywords.IsDefaultOrEmpty && NameMatches(shot.From, o.NameKeywords))
            sig |= DetectionSignal.NameKeyword;

        if (o.MatchComment && !o.CommentMarkers.IsDefaultOrEmpty && CommentMatches(shot.Comment, o.CommentMarkers))
            sig |= DetectionSignal.CommentMarker;

        if (o.MatchStationFlag && !o.StationFlags.IsDefaultOrEmpty &&
            (StationHasFlag(model, shot.From, o.StationFlags) || StationHasFlag(model, shot.To, o.StationFlags)))
            sig |= DetectionSignal.StationFlag;

        return sig;
    }

    private static bool NameMatches(QualifiedName from, ImmutableArray<string> keywords)
    {
        var name = from.ToString();
        foreach (var k in keywords)
            if (!string.IsNullOrEmpty(k) && name.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool CommentMatches(string? comment, ImmutableArray<string> markers)
    {
        if (string.IsNullOrEmpty(comment)) return false;
        foreach (var m in markers)
            if (!string.IsNullOrEmpty(m) && comment.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool StationHasFlag(SemanticModel model, QualifiedName station, ImmutableArray<string> flags)
    {
        if (!model.Stations.TryGetValue(station, out var s) || s.Flags.IsDefaultOrEmpty) return false;
        foreach (var f in s.Flags)
            foreach (var want in flags)
                if (string.Equals(f, want, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ---- grouping key --------------------------------------------------------------------------

    private static string GroupKey(ShotSymbol shot, DetectionOptions o, EquateGraph equates) => o.Grouping switch
    {
        GroupingMode.ByCommentParameter => CommentParameter(shot.Comment, o.CommentMarkers) ?? equates.Find(shot.From).ToString(),
        GroupingMode.ByFlagParameter => equates.Find(shot.From).ToString(), // flags carry no value → fall back to station
        _ => equates.Find(shot.From).ToString(),
    };

    /// <summary>The text following a comment marker, e.g. <c># plane fault-A</c> with marker "plane" → "fault-A".</summary>
    private static string? CommentParameter(string? comment, ImmutableArray<string> markers)
    {
        if (string.IsNullOrEmpty(comment)) return null;
        foreach (var m in markers)
        {
            if (string.IsNullOrEmpty(m)) continue;
            int idx = comment.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var rest = comment[(idx + m.Length)..].Trim();
            return rest.Length == 0 ? m : rest;
        }
        return null;
    }

    // ---- batch assembly ------------------------------------------------------------------------

    private static StructuralBatch BuildBatch(
        List<(ShotSymbol Shot, DetectionSignal Sig, string Key, bool IsSplay)> items,
        DetectionOptions o, EquateGraph equates, CenterlineSolution? solution)
    {
        var measurements = ImmutableArray.CreateBuilder<StructuralMeasurement>(items.Count + 1);
        var distinctFroms = new HashSet<QualifiedName>();

        foreach (var (shot, sig, _, isSplay) in items)
        {
            var local = CenterlineGeometry.ShotVector(shot.Length!.Value, shot.Compass!.Value, shot.Clino!.Value);
            var worldFrom = solution?.PositionOf(shot.From);
            distinctFroms.Add(shot.From);

            measurements.Add(new StructuralMeasurement
            {
                Shot = shot,
                From = shot.From,
                To = shot.To,
                Length = shot.Length,
                Compass = shot.Compass,
                Clino = shot.Clino,
                Local = local,
                World = worldFrom is { } wf ? wf + local : null,
                IsSplay = isSplay,
                IncludedByDefault = !isSplay || o.Splays != SplayPolicy.Exclude,
                MatchedBy = sig,
                Comment = shot.Comment,
                SourceFile = shot.Span.FilePath,
                Line = shot.Span.Start.Line,
                Span = shot.Span,
            });
        }

        // Synthetic origin row — only meaningful when the whole batch radiates from one station.
        if (distinctFroms.Count == 1)
        {
            var from = items[0].Shot.From;
            measurements.Add(new StructuralMeasurement
            {
                From = from,
                To = from,
                Local = Vec3.Zero,
                World = solution?.PositionOf(from),
                IsOrigin = true,
                IncludedByDefault = o.IncludeOriginPoint,
                MatchedBy = DetectionSignal.None,
                SourceFile = items[0].Shot.Span.FilePath,
                Line = items[0].Shot.Span.Start.Line,
            });
        }

        string key = items[0].Key;
        string name = o.Grouping == GroupingMode.ByFromStation ? items[0].Shot.From.ToString() : key;
        return new StructuralBatch
        {
            Key = key,
            Name = name,
            SourceFile = items[0].Shot.Span.FilePath,
            Grouping = o.Grouping,
            Measurements = measurements.ToImmutable(),
        };
    }
}
