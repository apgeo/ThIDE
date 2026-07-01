// string interning for big projects.
//
// Large surveys repeat the same strings thousands of times: survey-name prefixes, file paths,
// symbol kinds, flags. Holding a separate string instance for each wastes managed heap. This is
// a small, thread-safe, *bounded* intern pool: callers route long-lived, high-duplication
// strings through Intern(...) so equal values share one instance. It stops growing past a cap
// (returning the original string) so it can never become an unbounded leak itself.
//
// Distinct from String.Intern (the CLR's pool, never collected and process-global): this pool is
// app-scoped and Clear()-able, so it can be reset between projects.

using System;
using System.Collections.Concurrent;

namespace Therion.Core;

public sealed class StringInterner
{
    private readonly ConcurrentDictionary<string, string> _pool = new(StringComparer.Ordinal);
    private readonly int _maxSize;

    public StringInterner(int maxSize = 250_000) => _maxSize = maxSize > 0 ? maxSize : int.MaxValue;

    /// <summary>Returns the canonical shared instance for <paramref name="s"/> (the original once the cap is hit).</summary>
    public string Intern(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        if (_pool.TryGetValue(s, out var existing)) return existing;
        if (_pool.Count >= _maxSize) return s;   // cap reached — don't grow the pool further
        return _pool.GetOrAdd(s, s);
    }

    /// <summary>Number of distinct strings currently pooled.</summary>
    public int Count => _pool.Count;

    /// <summary>Drops every pooled string (e.g. when switching projects).</summary>
    public void Clear() => _pool.Clear();

    /// <summary>A process-wide shared pool for app-level callers that don't own an instance.</summary>
    public static StringInterner Shared { get; } = new();
}
