// Implementation Plan �5.1 � qualified station/survey names.
// Therion uses top-down dotted paths (e.g., "cave.upper.u1") where the leftmost
// component is the outermost survey and the rightmost component is the station.

using System;
using System.Collections.Immutable;

namespace Therion.Semantics;

/// <summary>
/// A fully-qualified name consisting of one or more dot-separated components.
/// Comparison is case-sensitive (Therion names are).
/// </summary>
public readonly record struct QualifiedName
{
    public ImmutableArray<string> Parts { get; }

    public QualifiedName(ImmutableArray<string> parts)
    {
        if (parts.IsDefault || parts.Length == 0)
            throw new ArgumentException("QualifiedName requires at least one component.", nameof(parts));
        Parts = parts;
    }

    public string Last => Parts[^1];

    public QualifiedName Append(string name) =>
        new(Parts.Add(name));

    public QualifiedName Parent() =>
        Parts.Length <= 1
            ? throw new InvalidOperationException("Root has no parent.")
            : new QualifiedName(ImmutableArray.CreateRange(Parts, 0, Parts.Length - 1, x => x));

    public bool HasParent => Parts.Length > 1;

    public static QualifiedName Of(params string[] parts) =>
        new(ImmutableArray.Create(parts));

    public static QualifiedName Parse(string dotted)
    {
        if (string.IsNullOrEmpty(dotted))
            throw new ArgumentException("Name cannot be empty.", nameof(dotted));
        return new QualifiedName(ImmutableArray.Create(dotted.Split('.')));
    }

    // Builds "p0.p1.….pn" directly. `string.Join('.', Parts)` boxes the ImmutableArray into
    // IEnumerable<string> and allocates an enumerator on every call; ToString is on the hot path
    // for building the workspace reference indexes (one per station) and graph sort keys, so we
    // compose the string in one allocation via string.Create instead.
    public override string ToString()
    {
        var parts = Parts;
        if (parts.Length == 1) return parts[0];   // no separator, no new allocation

        int length = parts.Length - 1;            // the '.' separators
        for (int i = 0; i < parts.Length; i++) length += parts[i].Length;

        return string.Create(length, parts, static (span, p) =>
        {
            int pos = 0;
            for (int i = 0; i < p.Length; i++)
            {
                if (i > 0) span[pos++] = '.';
                p[i].AsSpan().CopyTo(span[pos..]);
                pos += p[i].Length;
            }
        });
    }

    public bool Equals(QualifiedName other)
    {
        if (Parts.Length != other.Parts.Length) return false;
        for (int i = 0; i < Parts.Length; i++)
            if (!string.Equals(Parts[i], other.Parts[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var p in Parts) hc.Add(p, StringComparer.Ordinal);
        return hc.ToHashCode();
    }
}
