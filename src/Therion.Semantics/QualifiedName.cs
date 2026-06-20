// Implementation Plan §5.1 — qualified station/survey names.
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

    public override string ToString() => string.Join('.', Parts);

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
