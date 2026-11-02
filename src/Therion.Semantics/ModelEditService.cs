// Implementation Plan §7.3 / §7.5 — model edit service.
// Validates inline edits and applies them via ITherionWriter (round-trip).
// Backs the Object Browser DataGrid + Properties panel.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Outcome of an inline edit attempt.</summary>
public sealed record EditResult(
    bool Success,
    string? UpdatedText,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Applies single-node edits to an in-memory <see cref="TherionFile"/> and
/// re-serializes the file via <see cref="ITherionWriter"/>. The result is the
/// new file text; persistence is the caller's responsibility (workspace layer).
/// </summary>
public interface IModelEditService
{
    /// <summary>Replace <paramref name="original"/> with <paramref name="replacement"/> in <paramref name="file"/>.</summary>
    EditResult ReplaceNode(TherionFile file, TherionNode original, TherionNode replacement);

    /// <summary>
    /// Sets value <paramref name="valueIndex"/> of shot row <paramref name="row"/> to <paramref name="value"/>
    /// by replacing only that token in <paramref name="text"/>, the text <paramref name="file"/> was parsed
    /// from; the rest of the file is kept byte for byte. Missing trailing values are filled with <c>-</c>.
    /// </summary>
    EditResult ReplaceRowValue(TherionFile file, string text, DataRow row, int valueIndex, string value);
}

public sealed class ModelEditService : IModelEditService
{
    private readonly ITherionWriter _writer;

    public ModelEditService(ITherionWriter? writer = null)
    {
        _writer = writer ?? new TherionWriter();
    }

    public EditResult ReplaceNode(TherionFile file, TherionNode original, TherionNode replacement)
    {
        if (original.GetType() != replacement.GetType())
        {
            return new EditResult(false, null, ImmutableArray.Create(
                Diagnostic.Create("TH_EDIT_001", DiagnosticSeverity.Error,
                    $"Cannot replace node of type {original.GetType().Name} with {replacement.GetType().Name}.",
                    original.Span)));
        }

        var (updatedChildren, found) = ReplaceIn(file.Children, original, replacement);
        if (!found)
        {
            return new EditResult(false, null, ImmutableArray.Create(
                Diagnostic.Create("TH_EDIT_002", DiagnosticSeverity.Error,
                    "Target node not found in the file.", original.Span)));
        }

        var updatedFile = file with { Children = updatedChildren };
        var text = _writer.Write(updatedFile);
        return new EditResult(true, text, ImmutableArray<Diagnostic>.Empty);
    }

    public EditResult ReplaceRowValue(TherionFile file, string text, DataRow row, int valueIndex, string value)
    {
        // The row may come from another parse of this file (the workspace session's): locate it here.
        var current = Find(file.Children, row) as DataRow;
        if (current is null || valueIndex < 0 || current.ValueSpans.Length != current.Values.Length ||
            current.ValueSpans.Length == 0 || !SpansMatch(text, current))
        {
            // Not found, or the text no longer is what was parsed (an edit since the last parse).
            return new EditResult(false, null, ImmutableArray.Create(
                Diagnostic.Create("TH_EDIT_002", DiagnosticSeverity.Error,
                    "Target node not found in the file.", row.Span)));
        }

        if (valueIndex < current.Values.Length)
        {
            var span = current.ValueSpans[valueIndex];
            return new EditResult(true,
                text[..span.StartOffset] + value + text[(span.StartOffset + span.Length)..],
                ImmutableArray<Diagnostic>.Empty);
        }

        // Past the last value: append the missing columns after it.
        var last = current.ValueSpans[^1];
        int at = last.StartOffset + last.Length;
        var tail = new System.Text.StringBuilder();
        for (int i = current.Values.Length; i < valueIndex; i++) tail.Append(" -");
        tail.Append(' ').Append(value);
        return new EditResult(true, text[..at] + tail + text[at..], ImmutableArray<Diagnostic>.Empty);
    }

    /// <summary>True when every value of <paramref name="row"/> still sits at its recorded offset in <paramref name="text"/>.</summary>
    private static bool SpansMatch(string text, DataRow row)
    {
        for (int i = 0; i < row.Values.Length; i++)
        {
            var span = row.ValueSpans[i];
            if (span.StartOffset < 0 || span.Length != row.Values[i].Length || span.StartOffset + span.Length > text.Length ||
                string.CompareOrdinal(text, span.StartOffset, row.Values[i], 0, span.Length) != 0)
                return false;
        }
        return true;
    }

    private static TherionNode? Find(ImmutableArray<TherionNode> children, TherionNode original)
    {
        foreach (var child in children)
        {
            if (IsSameNode(child, original)) return child;
            if (child is BlockCommand block && Find(block.Children, original) is { } inner) return inner;
        }
        return null;
    }

    private static (ImmutableArray<TherionNode> Children, bool Found) ReplaceIn(
        ImmutableArray<TherionNode> children, TherionNode original, TherionNode replacement)
    {
        var builder = ImmutableArray.CreateBuilder<TherionNode>(children.Length);
        bool found = false;
        foreach (var child in children)
        {
            if (IsSameNode(child, original))
            {
                builder.Add(replacement);
                found = true;
            }
            else if (child is BlockCommand block)
            {
                var (innerChildren, innerFound) = ReplaceIn(block.Children, original, replacement);
                if (innerFound)
                {
                    builder.Add(block switch
                    {
                        SurveyCommand s     => s with { Children = innerChildren },
                        CentrelineCommand c => c with { Children = innerChildren },
                        ScrapBlock sc       => sc with { Children = innerChildren },
                        _ => block,
                    });
                    found = true;
                }
                else builder.Add(child);
            }
            else builder.Add(child);
        }
        return (builder.ToImmutable(), found);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="original"/>, or the node at the same
    /// place in another parse of the same text: the Object Browser's rows come from the workspace
    /// session's parse while the edit is applied to the editor's own AST of that file. Same node type
    /// and source range, and for a shot row the same values, so a row whose text has since changed
    /// is not mistaken for whatever now sits at its old offset.
    /// </summary>
    private static bool IsSameNode(TherionNode candidate, TherionNode original)
    {
        if (ReferenceEquals(candidate, original)) return true;
        if (candidate.GetType() != original.GetType() || original.Span.IsEmpty) return false;
        if (candidate.Span.StartOffset != original.Span.StartOffset || candidate.Span.Length != original.Span.Length)
            return false;
        return candidate is not DataRow row || row.Values.SequenceEqual(((DataRow)original).Values);
    }
}
