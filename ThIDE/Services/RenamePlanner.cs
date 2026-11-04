// Rename Symbol's planning half, kept out of the main window so it runs without one: which occurrences
// the rename rewrites, the opt-in extras the occurrences preview offers (namesakes in other surveys,
// equate-linked stations, mentions in comments), and whether the rename can be applied straight from the
// name prompt or needs the preview. The window asks for the name and applies (RenameApplier).

using System;
using System.Collections.Generic;
using System.Linq;
using ThIDE.Views;

namespace ThIDE.Services;

/// <summary>What renaming one symbol touches: <see cref="Changes"/> is the rename itself, the rest are opt-in.</summary>
internal sealed record RenamePlan(
    string OldName,
    List<RenameFileChanges> Changes,
    List<RenameFileChanges> CommentChanges,
    List<RenameFileChanges> EquateLinkedChanges,
    List<RenameFileChanges> SameNameChanges,
    bool CrossFile)
{
    /// <summary>
    /// "Complex" = the rename reaches beyond the active file, or there are other-survey namesakes / equate
    /// links to weigh. Complex renames always open the occurrences preview (with its own name field); a
    /// simple, single-file rename is applied straight from the name prompt unless
    /// <paramref name="alwaysPreview"/> (the setting) asks for the preview.
    /// </summary>
    public bool NeedsPreview(bool alwaysPreview) =>
        CrossFile || EquateLinkedChanges.Count > 0 || SameNameChanges.Count > 0 || alwaysPreview;
}

internal static class RenamePlanner
{
    /// <summary>The name token a rename replaces: a survey's last name, or a station's name without its mark. Null or empty: nothing to rename.</summary>
    public static string? OldName(string raw, Therion.Processing.Abstractions.ReferenceKind kind)
    {
        var stRef = Therion.Semantics.StationRef.Parse(raw);
        return kind == Therion.Processing.Abstractions.ReferenceKind.Survey
            ? (stRef.SurveyLastName ?? stRef.Point)
            : stRef.PointWithoutMark;
    }

    /// <summary>A character Therion does not allow in a name, or null when <paramref name="name"/> is valid.</summary>
    public static char? InvalidName(string name) => Therion.Syntax.TherionIdentifiers.FirstIllegalChar(name);

    /// <summary>
    /// Plans renaming the symbol declared at <paramref name="targetSpan"/> (the token <paramref name="raw"/>,
    /// whose name is <paramref name="oldName"/>). <see cref="RenamePlan.Changes"/> is empty when nothing
    /// refers to it; the opt-in extras are only looked for when it is not.
    /// </summary>
    public static RenamePlan Plan(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        string raw,
        Therion.Processing.Abstractions.ReferenceKind kind,
        string oldName,
        Therion.Core.SourceSpan targetSpan)
    {
        // Find every occurrence up-front — both to apply and to choose the workflow. Stations & surveys
        // rename via the true occurrence index (scope-correct, @-aware, cross-file); scrap/map use the
        // text-scan; a standalone file falls back to the active document's own index.
        var changes = CollectSymbolRenameChanges(workspace, active, kind, targetSpan)
            ?? CollectActiveFileRenameChanges(active, kind, targetSpan)
            ?? CollectRenameChanges(workspace, active, nav, raw, kind, oldName, targetSpan);
        if (changes.Count == 0) return new RenamePlan(oldName, changes, new(), new(), new(), CrossFile: false);

        // Opt-in expansions surfaced only in the occurrences preview.
        var commentChanges = CollectCommentRenameChanges(workspace, active, oldName, changes);
        var equateLinkedChanges = CollectEquateLinkedChanges(workspace, active, kind, oldName, targetSpan, changes);
        var sameNameChanges = CollectSameNameChanges(workspace, active, kind, oldName, changes);

        var activePath = active?.FilePath;
        bool crossFile = changes.Any(c => activePath is null ||
            !Therion.Workspace.Editing.FilePaths.Comparer.Equals(c.FilePath, activePath));
        return new RenamePlan(oldName, changes, commentChanges, equateLinkedChanges, sameNameChanges, crossFile);
    }

    /// <summary>
    /// True rename for a station: replaces exactly the token-level occurrences the semantic
    /// occurrence index attributes to the clicked station (scope-correct, <c>@</c>-aware, comment-free,
    /// cross-file). Returns null when the target isn't a resolvable station (caller falls back).
    /// </summary>
    private static List<RenameFileChanges>? CollectSymbolRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, Therion.Core.SourceSpan targetSpan)
    {
        // Resolve the clicked declaration to a symbol identity + its current name token; shared with
        // Find All References, which needs exactly the same resolution.
        if (Therion.Semantics.SymbolReferences.ResolveDeclaration(workspace, kind, targetSpan)
            is not { } resolved)
            return null;   // scrap/map (or unresolved): caller falls back to the text-scan
        var (symbol, name) = resolved;

        // The active document's spans must come from its live (possibly-unsaved) buffer — the apply
        // step writes FileText back and updates the open buffer in place, so disk text here would
        // clobber unsaved edits (and the stale-drift guard would silently drop the file's spans).
        var edits = Therion.Semantics.SymbolRenamePlan.Compute(workspace, symbol, name,
            path => active?.FilePath is { Length: > 0 } ap &&
                    Therion.Workspace.Editing.FilePaths.Comparer.Equals(path, ap) &&
                    active.DocumentText is { Length: > 0 } at
                ? at
                : SafeReadAllText(path));
        if (edits.Count == 0) return null;
        return edits
            .Select(e => new RenameFileChanges(e.FilePath, e.FileText, e.Spans.ToList()))
            .ToList();
    }

    /// <summary>
    /// Single-file rename via the <b>active document's own</b> occurrence index — the fallback for when
    /// the edited file isn't in the loaded workspace model (a standalone <c>.th</c>, or one opened
    /// without its project, so <see cref="Therion.Semantics.WorkspaceSemanticModel.PerFile"/> doesn't
    /// cover it). Uses the live editor text so spans stay aligned even when the buffer is unsaved.
    /// Returns null when the active file has no matching station/survey declared at the target.
    /// </summary>
    private static List<RenameFileChanges>? CollectActiveFileRenameChanges(
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, Therion.Core.SourceSpan targetSpan)
    {
        if (active?.Semantics is not { } model) return null;
        var path = active.FilePath;
        var text = active.DocumentText;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) return null;
        // The occurrence index only covers this file, so the declaration must live here.
        if (!Therion.Workspace.Editing.FilePaths.Comparer.Equals(targetSpan.FilePath, path)) return null;

        // Resolve the clicked declaration to a symbol identity within this file (mirrors the workspace
        // path). Any ⇒ try station then survey (a plain token arrives as Any — see CollectSymbolRenameChanges).
        bool Match(Therion.Core.SourceSpan d) =>
            d.StartOffset == targetSpan.StartOffset &&
            Therion.Workspace.Editing.FilePaths.Comparer.Equals(d.FilePath, path);

        Therion.Semantics.SymbolId symbol;
        string name;
        if (WantsStation(kind) && model.Stations.Values.FirstOrDefault(s => Match(s.DeclarationSpan)) is { } st)
        {
            symbol = new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, st.Name);
            name = st.Name.Last;
        }
        else if (WantsSurvey(kind) && model.Surveys.Values.FirstOrDefault(s => Match(s.DeclarationSpan)) is { } sv)
        {
            symbol = new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, sv.Name);
            name = sv.Name.Last;
        }
        else return null;   // scrap/map (or unresolved): caller falls back to the text-scan

        if (Therion.Semantics.SymbolRenamePlan.ComputeForFile(model.Occurrences, symbol, name, path, text)
            is not { } edit) return null;
        return new List<RenameFileChanges> { new(edit.FilePath, edit.FileText, edit.Spans.ToList()) };
    }

    // A plain station/survey token reaches rename as ReferenceKind.Any; both must be tried for it.
    private static bool WantsStation(Therion.Processing.Abstractions.ReferenceKind k) =>
        k is Therion.Processing.Abstractions.ReferenceKind.Station or Therion.Processing.Abstractions.ReferenceKind.Any;
    private static bool WantsSurvey(Therion.Processing.Abstractions.ReferenceKind k) =>
        k is Therion.Processing.Abstractions.ReferenceKind.Survey or Therion.Processing.Abstractions.ReferenceKind.Any;

    /// <summary>
    /// The blunt opt-in pass: rename <b>every other</b> station/survey that shares this name, regardless of
    /// which survey or file it lives in (≈ "replace all string occurrences"). Built from the occurrence
    /// index — for each same-last-named symbol it computes that symbol's own scope-correct edits, then
    /// merges them, dropping any span already covered by the true rename. Covers workspace files and the
    /// active document (which may be outside the workspace). Empty when no other same-named symbol exists.
    /// </summary>
    private static List<RenameFileChanges> CollectSameNameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, string oldName, List<RenameFileChanges> already)
    {
        // Every station (and survey, when applicable) sharing the last name — workspace-wide plus the
        // active document's own (it may be outside the workspace). The target's own occurrences drop out
        // via the covered-span filter in BuildSymbolChanges.
        var symbols = new HashSet<Therion.Semantics.SymbolId>();
        if (WantsStation(kind) && workspace.StationsByLastName.TryGetValue(oldName, out var sts))
            foreach (var s in sts) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, s.Name));
        if (WantsSurvey(kind) && workspace.SurveysByLastName.TryGetValue(oldName, out var svs))
            foreach (var s in svs) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, s.Name));
        if (active?.Semantics is { } m)
        {
            if (WantsStation(kind))
                foreach (var s in m.Stations.Values)
                    if (s.Name.Last == oldName) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, s.Name));
            if (WantsSurvey(kind))
                foreach (var s in m.Surveys.Values)
                    if (s.Name.Last == oldName) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, s.Name));
        }
        return BuildSymbolChanges(workspace, active, symbols, oldName, already);
    }

    /// <summary>
    /// Opt-in pass: rename the stations that an <c>equate</c> declares to be the same physical point as the
    /// target and that share its name in a different survey (e.g. <c>b.1</c> for <c>a.1</c> given
    /// <c>equate 1@a 1@b</c>) — following equate links across surveys/files so the connection isn't broken.
    /// Narrower than <see cref="CollectSameNameChanges"/> (only equate-linked stations, not every namesake).
    /// Works no matter where the rename was launched (equate line or an ordinary station occurrence).
    /// </summary>
    private static List<RenameFileChanges> CollectEquateLinkedChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, string oldName,
        Therion.Core.SourceSpan targetSpan, List<RenameFileChanges> already)
    {
        if (!WantsStation(kind)) return new();

        // The target station's qualified name (workspace first, then the active file's own model).
        Therion.Semantics.QualifiedName? targetQn = workspace.FindStationByDeclaration(targetSpan)?.Name;
        if (targetQn is null && active?.Semantics is { } m)
            foreach (var s in m.Stations.Values)
                if (s.DeclarationSpan.StartOffset == targetSpan.StartOffset &&
                    Therion.Workspace.Editing.FilePaths.Comparer.Equals(s.DeclarationSpan.FilePath, targetSpan.FilePath))
                { targetQn = s.Name; break; }
        if (targetQn is null) return new();

        var linked = workspace.EquatedSameNameStations(targetQn.Value);
        if (linked.IsEmpty) return new();

        var symbols = new HashSet<Therion.Semantics.SymbolId>();
        foreach (var qn in linked) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, qn));
        return BuildSymbolChanges(workspace, active, symbols, oldName, already);
    }

    /// <summary>
    /// Computes the merged, per-file rename edits for an explicit set of <paramref name="symbols"/> from the
    /// occurrence index (workspace + active document), dropping any span already covered by
    /// <paramref name="already"/>. The active document is read from its live buffer so spans stay aligned
    /// even when it is unsaved / not part of the workspace.
    /// </summary>
    private static List<RenameFileChanges> BuildSymbolChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        IReadOnlyCollection<Therion.Semantics.SymbolId> symbols, string oldName, List<RenameFileChanges> already)
    {
        if (symbols.Count == 0) return new();

        var covered = already.ToDictionary(c => c.FilePath,
            c => new HashSet<int>(c.Hits.Select(h => h.Start)), Therion.Workspace.Editing.FilePaths.Comparer);
        var byPath = new Dictionary<string, RenameFileChanges>(Therion.Workspace.Editing.FilePaths.Comparer);

        string? Read(string p) =>
            active?.FilePath is { Length: > 0 } ap &&
            Therion.Workspace.Editing.FilePaths.Comparer.Equals(p, ap) &&
            active.DocumentText is { Length: > 0 } at
                ? at
                : (System.IO.File.Exists(p) ? SafeReadAllText(p) : null);

        void AddEdit(string filePath, string fileText, IEnumerable<(int Start, int Length)> spans)
        {
            covered.TryGetValue(filePath, out var cov);
            var add = spans.Where(s => cov is null || !cov.Contains(s.Start)).ToList();
            if (add.Count == 0) return;
            byPath[filePath] = byPath.TryGetValue(filePath, out var ex)
                ? ex with { Hits = ex.Hits.Concat(add).Distinct().OrderBy(h => h.Start).ToList() }
                : new RenameFileChanges(filePath, fileText, add.Distinct().OrderBy(h => h.Start).ToList());
        }

        foreach (var sym in symbols)
        {
            foreach (var e in Therion.Semantics.SymbolRenamePlan.Compute(workspace, sym, oldName, Read))
                AddEdit(e.FilePath, e.FileText, e.Spans);
            // Active document (may be outside the workspace occurrence index).
            if (active?.Semantics is { } m && active.FilePath is { Length: > 0 } ap && Read(ap) is { } t &&
                Therion.Semantics.SymbolRenamePlan.ComputeForFile(m.Occurrences, sym, oldName, ap, t) is { } le)
                AddEdit(le.FilePath, le.FileText, le.Spans);
        }
        return byPath.Values.ToList();
    }

    private static string? SafeReadAllText(string path)
    {
        try { return Therion.Syntax.EncodingResolver.ReadAllText(path); } catch { return null; }
    }

    /// <summary>
    /// Whole-word occurrences of <paramref name="oldName"/> that live inside <c>#</c> comments (which
    /// the symbol rename skips), across the workspace's .th files — for the opt-in "rename in comments
    /// too" pass. Excludes any span already covered by the symbol rename.
    /// </summary>
    internal static List<RenameFileChanges> CollectCommentRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        string oldName, List<RenameFileChanges> symbolChanges)
    {
        var known = symbolChanges.ToDictionary(c => c.FilePath,
            c => new HashSet<int>(c.Hits.Select(h => h.Start)), Therion.Workspace.Editing.FilePaths.Comparer);
        // A file the symbol pass already plans is scanned in that very text, so both passes' offsets
        // agree when they are merged and applied to it.
        var plannedText = symbolChanges.ToDictionary(c => c.FilePath, c => c.FileText, Therion.Workspace.Editing.FilePaths.Comparer);
        var result = new List<RenameFileChanges>();
        var seenPaths = new HashSet<string>(Therion.Workspace.Editing.FilePaths.Comparer);

        void Scan(string path, string text)
        {
            if (!seenPaths.Add(path)) return;
            known.TryGetValue(path, out var seen);
            var hits = Therion.Semantics.CommentOccurrences.Find(text, oldName, seen).ToList();
            if (hits.Count > 0) result.Add(new RenameFileChanges(path, text, hits));
        }

        // Active document first, from its live buffer (may be standalone / unsaved).
        if (active?.FilePath is { Length: > 0 } activePath && active.DocumentText is { Length: > 0 } activeText)
            Scan(activePath, activeText);

        foreach (var path in workspace.PerFile.Keys)
        {
            if (seenPaths.Contains(path)) continue;
            // Decoded the way Therion reads the file (its `encoding` line), as the other rename passes do:
            // the hits are applied to this text and it is saved back in that encoding.
            var text = plannedText.TryGetValue(path, out var planned) ? planned : SafeReadAllText(path);
            if (text is null) continue;
            Scan(path, text);
        }
        return result;
    }

    /// <summary>
    /// Merges two change sets by file (dedup + order hits), so both passes apply cleanly. Hits are
    /// offsets into their own <see cref="RenameFileChanges.FileText"/>, so a file whose two plans were
    /// computed against different texts keeps only the first plan's hits rather than misplacing the second's.
    /// </summary>
    internal static List<RenameFileChanges> MergeRenameChanges(
        List<RenameFileChanges> a, IReadOnlyList<RenameFileChanges> b)
    {
        var byPath = a.ToDictionary(c => c.FilePath, Therion.Workspace.Editing.FilePaths.Comparer);
        foreach (var c in b)
        {
            if (byPath.TryGetValue(c.FilePath, out var existing))
            {
                if (!string.Equals(existing.FileText, c.FileText, StringComparison.Ordinal)) continue;
                byPath[c.FilePath] = existing with
                {
                    Hits = existing.Hits.Concat(c.Hits).Distinct().OrderBy(h => h.Start).ToList(),
                };
            }
            else byPath[c.FilePath] = c;
        }
        return byPath.Values.ToList();
    }

    /// <summary>
    /// Scans every workspace file (plus the active document, so a standalone file not in the workspace
    /// is still covered) for ref tokens resolving to the same declaration span. The last-resort path for
    /// scrap/map renames, which don't yet have an occurrence index.
    /// </summary>
    private static List<RenameFileChanges> CollectRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        string raw,
        Therion.Processing.Abstractions.ReferenceKind kind,
        string oldName,
        Therion.Core.SourceSpan targetSpan)
    {
        var result = new List<RenameFileChanges>();
        var seenPaths = new HashSet<string>(Therion.Workspace.Editing.FilePaths.Comparer);

        // The active document may not be part of the loaded workspace; scan its live (possibly-unsaved)
        // text first so its spans align with what the user sees.
        if (active?.FilePath is { Length: > 0 } activePath && active.DocumentText is { Length: > 0 } activeText)
        {
            var activeHits = FindRenameHitsInText(activeText, oldName, nav, kind, targetSpan);
            if (activeHits.Count > 0) result.Add(new RenameFileChanges(activePath, activeText, activeHits));
            seenPaths.Add(activePath);
        }

        foreach (var filePath in workspace.PerFile.Keys)
        {
            if (!seenPaths.Add(filePath)) continue;   // already handled as the active doc
            string text;
            try { text = Therion.Syntax.EncodingResolver.ReadAllText(filePath); }
            catch { continue; }

            var hits = FindRenameHitsInText(text, oldName, nav, kind, targetSpan);
            if (hits.Count > 0) result.Add(new RenameFileChanges(filePath, text, hits));
        }
        return result;
    }

    private static List<(int Start, int Length)> FindRenameHitsInText(
        string text, string oldName,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        Therion.Processing.Abstractions.ReferenceKind kind,
        Therion.Core.SourceSpan targetSpan)
    {
        var hits = new List<(int, int)>();
        int idx = 0;
        while ((idx = text.IndexOf(oldName, idx, System.StringComparison.Ordinal)) >= 0)
        {
            bool beforeOk = idx == 0 || !IsRefChar(text[idx - 1]);
            bool afterOk  = idx + oldName.Length >= text.Length || !IsRefChar(text[idx + oldName.Length]);
            if (beforeOk && afterOk)
            {
                int mid = idx + oldName.Length / 2;
                var tok = ThIDE.Editor.TherionTextEditor.ExtractRefTokenStatic(text, mid);
                if (tok is { } t)
                {
                    var resolved = nav.GoToDefinition(t.Raw, kind);
                    if (resolved is { } rs &&
                        Therion.Workspace.Editing.FilePaths.Comparer.Equals(rs.FilePath, targetSpan.FilePath) &&
                        rs.StartOffset == targetSpan.StartOffset)
                    {
                        hits.Add((idx, oldName.Length));
                    }
                }
            }
            idx += oldName.Length;
        }
        return hits;
    }

    private static bool IsRefChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@' or ':';
}
