// Mutation-test harness (syntax-coverage effort, batch A1 — PLAN §8).
// A mutation is a deterministic, typed edit of a known-good source file that should
// produce INVALID syntax. Each declares the diagnostic code it must provoke plus an
// oracle tier that governs how strictly the outcome is asserted:
//
//   Tier1 — invalid BY CONSTRUCTION (charset injection, terminator break, non-numeric
//           in a numeric slot): assert the exact expected code appears.
//   Tier2 — PROBABLY invalid (enum misspellings could collide with another valid word;
//           deletions could form a shorter valid form): the mutation re-checks its own
//           output and self-skips when accidentally valid; asserts diagnostics did not
//           DECREASE vs. the seed, rather than an exact code.
//
// The harness is data-driven and schema-agnostic so new file types / commands / mutations
// plug in without new test code (D1 grows the catalog per C batch).

using System;

namespace Therion.Syntax.Tests.Mutation;

public enum MutationTier
{
    /// <summary>Invalid by construction — assert the exact diagnostic code.</summary>
    Tier1,
    /// <summary>Probably invalid — assert diagnostics did not decrease; may self-skip.</summary>
    Tier2,
}

/// <summary>One typed source-code mutation.</summary>
public sealed record SyntaxMutation(
    string Id,
    string Description,
    MutationTier Tier,
    // Diagnostic code the mutant must contain (required for Tier1; optional for Tier2).
    string? ExpectedCode,
    // Applies the edit; returns the mutated text, or null when not applicable to this
    // seed / accidentally still valid (the case is then skipped, not failed).
    Func<string, Random, string?> Apply);
