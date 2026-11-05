// Implementation Plan §4.2 (Diagnostics-first design), §10 (UX priorities).

using System.Collections.Immutable;

namespace Therion.Core;

/// <summary>Severity of a <see cref="Diagnostic"/>.</summary>
public enum DiagnosticSeverity
{
    Hint,
    Info,
    Warning,
    Error,
}

/// <summary>
/// A culture-invariant diagnostic code (e.g., <c>TH0007</c>, <c>TH_XVI_001</c>).
/// Codes never change once shipped. <see cref="Diagnostic.Message"/> and <see cref="Diagnostic.Hint"/>
/// are English; a UI that shows them in another language keys its own translations by code.
/// See <c>docs/diagnostics.md</c> for the catalog.
/// </summary>
public readonly record struct DiagnosticCode(string Value)
{
    public override string ToString() => Value;
    public static implicit operator string(DiagnosticCode c) => c.Value;
}

/// <summary>
/// A structured diagnostic produced by lexer / parser / semantic passes
/// or by the external Therion compiler output parser.
/// </summary>
public sealed record Diagnostic(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan Span,
    ImmutableArray<SourceSpan> RelatedSpans,
    string? Hint = null,
    string? HelpUri = null,
    TherionSyntaxVersion? Version = null)
{
    /// <summary>
    /// The suggestion in <see cref="Hint"/> is a guess a tool should not apply unseen: a quick fix may
    /// offer it for this one diagnostic, but "fix all" leaves it out.
    /// </summary>
    public bool HintNeedsReview { get; init; }

    public static Diagnostic Create(
        string code,
        DiagnosticSeverity severity,
        string message,
        SourceSpan span,
        string? hint = null,
        string? helpUri = null,
        TherionSyntaxVersion? version = null) =>
        new(new DiagnosticCode(code), severity, message, span,
            ImmutableArray<SourceSpan>.Empty, hint, helpUri, version);

    public override string ToString() =>
        $"{Severity.ToString().ToLowerInvariant()} {Code}: {Message}{(Span.IsEmpty ? "" : $" --> {Span}")}";
}
