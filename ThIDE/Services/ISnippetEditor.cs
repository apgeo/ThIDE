namespace ThIDE.Services;

/// <summary>What happened when the assistant tried to drop a code snippet into the editor (CAP-03).</summary>
public enum SnippetOutcome
{
    /// <summary>The snippet was inserted/replaced in the active editor.</summary>
    Applied,
    /// <summary>No document is open, so there is nowhere to put it.</summary>
    NoEditor,
    /// <summary>A replace was asked for but nothing is selected.</summary>
    NoSelection,
}

/// <summary>
/// The narrow slice the Assistant pane needs to place a code snippet into the active editor with the
/// normal editor pipeline behind it (native undo, dirty tracking, re-lint) — because it <em>is</em> a
/// user edit. <see cref="IDocumentService"/>'s implementation provides it; a two-method fake covers the
/// pane's note behaviour in tests without stubbing the whole document surface.
/// </summary>
public interface ISnippetEditor
{
    /// <summary>Inserts <paramref name="text"/> at the active editor's caret, or reports no editor.</summary>
    SnippetOutcome InsertAtActiveCaret(string text);

    /// <summary>Replaces the active editor's selection with <paramref name="text"/>, or reports no editor / no selection.</summary>
    SnippetOutcome ReplaceActiveSelection(string text);
}
