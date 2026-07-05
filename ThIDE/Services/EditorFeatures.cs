// Editor-feature gating. Every feature is toggleable two ways:
//   1. a compile-time master constant in EditorFeatureFlags (set false to compile it out), and
//   2. a per-feature runtime switch in AppSettings.EditorFeatures (the Preferences "Editor
//      Features" section).
// The effective state is the AND of the two, exposed through EditorFeatureFlags.IsEnabled().
// EditorFeatureCatalog supplies the human-readable title/description used by the UI.

using System;
using System.Collections.Generic;

namespace ThIDE.Services;

/// <summary>The editor features that can be individually enabled/disabled.</summary>
public enum EditorFeature
{
    RichCompletion,
    SignatureHelp,
    Snippets,
    FormatDocument,
    Minimap,
    StickyScroll,
    Outline,
    PeekDefinition,
    SplitView,
    ColorAdornments,
    WhitespaceGuides,
    TrimTrailingOnSave,
    MatchTerminator,
    SmartEnter,
    ReadingOrderNav,
}

/// <summary>
/// Compile-time master switches for the editor features. Flip one to <c>false</c> to remove the
/// feature from the build entirely; the matching runtime setting then has no effect. The
/// effective state of a feature is <c>const &amp;&amp; AppSettings.EditorFeatures.X</c> — see
/// <see cref="IsEnabled(EditorFeature, AppSettings?)"/>.
/// </summary>
public static class EditorFeatureFlags
{
    public const bool RichCompletion     = true;
    public const bool SignatureHelp      = true;
    public const bool Snippets           = true;
    public const bool FormatDocument     = true;
    public const bool Minimap            = true;
    public const bool StickyScroll       = true;
    public const bool Outline            = true;
    public const bool PeekDefinition     = true;
    public const bool SplitView          = true;
    public const bool ColorAdornments    = true;
    public const bool WhitespaceGuides   = true;
    public const bool TrimTrailingOnSave = true;
    public const bool MatchTerminator    = true;
    public const bool SmartEnter         = true;
    public const bool ReadingOrderNav    = true;

    /// <summary>The compile-time master switch for <paramref name="feature"/>.</summary>
    public static bool Compiled(EditorFeature feature) => feature switch
    {
        EditorFeature.RichCompletion     => RichCompletion,
        EditorFeature.SignatureHelp      => SignatureHelp,
        EditorFeature.Snippets           => Snippets,
        EditorFeature.FormatDocument     => FormatDocument,
        EditorFeature.Minimap            => Minimap,
        EditorFeature.StickyScroll       => StickyScroll,
        EditorFeature.Outline            => Outline,
        EditorFeature.PeekDefinition     => PeekDefinition,
        EditorFeature.SplitView          => SplitView,
        EditorFeature.ColorAdornments    => ColorAdornments,
        EditorFeature.WhitespaceGuides   => WhitespaceGuides,
        EditorFeature.TrimTrailingOnSave => TrimTrailingOnSave,
        EditorFeature.MatchTerminator    => MatchTerminator,
        EditorFeature.SmartEnter         => SmartEnter,
        EditorFeature.ReadingOrderNav    => ReadingOrderNav,
        _ => false,
    };

    /// <summary>
    /// The effective state of <paramref name="feature"/>: enabled only when both the compile-time
    /// constant and the user's runtime setting allow it. A null <paramref name="settings"/> falls
    /// back to defaults (all on except SignatureHelp).
    /// </summary>
    public static bool IsEnabled(EditorFeature feature, AppSettings? settings) =>
        Compiled(feature) && (settings ?? AppSettings.Default).EditorFeatures.IsEnabled(feature);
}

/// <summary>Per-feature runtime toggles, persisted inside <see cref="AppSettings"/> (default: all on except SignatureHelp).</summary>
public sealed record EditorFeatureSettings
{
    public bool RichCompletion     { get; init; } = true;
    // Off by default — the inline signature/parameter hint is opt-in (Preferences ▸ Editor).
    public bool SignatureHelp      { get; init; } = false;
    public bool Snippets           { get; init; } = true;
    public bool FormatDocument     { get; init; } = true;
    public bool Minimap            { get; init; } = true;
    public bool StickyScroll       { get; init; } = true;
    public bool Outline            { get; init; } = true;
    public bool PeekDefinition     { get; init; } = true;
    public bool SplitView          { get; init; } = true;
    public bool ColorAdornments    { get; init; } = true;
    public bool WhitespaceGuides   { get; init; } = true;
    public bool TrimTrailingOnSave { get; init; } = true;
    public bool MatchTerminator    { get; init; } = true;
    public bool SmartEnter         { get; init; } = true;
    public bool ReadingOrderNav    { get; init; } = true;

    /// <summary>The runtime toggle for <paramref name="feature"/>.</summary>
    public bool IsEnabled(EditorFeature feature) => feature switch
    {
        EditorFeature.RichCompletion     => RichCompletion,
        EditorFeature.SignatureHelp      => SignatureHelp,
        EditorFeature.Snippets           => Snippets,
        EditorFeature.FormatDocument     => FormatDocument,
        EditorFeature.Minimap            => Minimap,
        EditorFeature.StickyScroll       => StickyScroll,
        EditorFeature.Outline            => Outline,
        EditorFeature.PeekDefinition     => PeekDefinition,
        EditorFeature.SplitView          => SplitView,
        EditorFeature.ColorAdornments    => ColorAdornments,
        EditorFeature.WhitespaceGuides   => WhitespaceGuides,
        EditorFeature.TrimTrailingOnSave => TrimTrailingOnSave,
        EditorFeature.MatchTerminator    => MatchTerminator,
        EditorFeature.SmartEnter         => SmartEnter,
        EditorFeature.ReadingOrderNav    => ReadingOrderNav,
        _ => false,
    };

    /// <summary>Returns a copy with <paramref name="feature"/> set to <paramref name="value"/>.</summary>
    public EditorFeatureSettings With(EditorFeature feature, bool value) => feature switch
    {
        EditorFeature.RichCompletion     => this with { RichCompletion = value },
        EditorFeature.SignatureHelp      => this with { SignatureHelp = value },
        EditorFeature.Snippets           => this with { Snippets = value },
        EditorFeature.FormatDocument     => this with { FormatDocument = value },
        EditorFeature.Minimap            => this with { Minimap = value },
        EditorFeature.StickyScroll       => this with { StickyScroll = value },
        EditorFeature.Outline            => this with { Outline = value },
        EditorFeature.PeekDefinition     => this with { PeekDefinition = value },
        EditorFeature.SplitView          => this with { SplitView = value },
        EditorFeature.ColorAdornments    => this with { ColorAdornments = value },
        EditorFeature.WhitespaceGuides   => this with { WhitespaceGuides = value },
        EditorFeature.TrimTrailingOnSave => this with { TrimTrailingOnSave = value },
        EditorFeature.MatchTerminator    => this with { MatchTerminator = value },
        EditorFeature.SmartEnter         => this with { SmartEnter = value },
        EditorFeature.ReadingOrderNav    => this with { ReadingOrderNav = value },
        _ => this,
    };
}

/// <summary>Human-readable metadata for each editor feature (backs the Preferences UI).</summary>
public readonly record struct EditorFeatureInfo(
    EditorFeature Feature, string Title, string Description);

public static class EditorFeatureCatalog
{
    public static readonly IReadOnlyList<EditorFeatureInfo> All = new[]
    {
        new EditorFeatureInfo(EditorFeature.RichCompletion, "Rich completion items",
            "Per-kind icons, documentation detail, and signature/snippet insertion in autocomplete."),
        new EditorFeatureInfo(EditorFeature.SignatureHelp, "Parameter / signature help",
            "Shows the expected next field as the caret moves across a command's arguments."),
        new EditorFeatureInfo(EditorFeature.Snippets, "Code snippets & templates",
            "Tab-expandable templates (survey block, centreline, scrap, thconfig skeleton, map block)."),
        new EditorFeatureInfo(EditorFeature.FormatDocument, "Format document",
            "Normalises block indentation, aligns data columns and flag spelling via the Therion writer."),
        new EditorFeatureInfo(EditorFeature.Minimap, "Minimap / code overview",
            "A condensed overview of the whole document beside the scrollbar."),
        new EditorFeatureInfo(EditorFeature.StickyScroll, "Sticky scroll & breadcrumbs",
            "Pins the enclosing survey/centreline/scrap header and shows a breadcrumb trail."),
        new EditorFeatureInfo(EditorFeature.Outline, "Document outline",
            "A live symbol tree (surveys, centrelines, scraps, objects) with click-to-navigate."),
        new EditorFeatureInfo(EditorFeature.PeekDefinition, "Peek definition",
            "Shows a definition inline in a popup without leaving the current file."),
        new EditorFeatureInfo(EditorFeature.SplitView, "Split / side-by-side editor",
            "Open a file in a second pane (Split Right / Split Down)."),
        new EditorFeatureInfo(EditorFeature.ColorAdornments, "Inline colour & unit hints",
            "A colour swatch next to colour values and a unit hint next to numerics."),
        new EditorFeatureInfo(EditorFeature.WhitespaceGuides, "Whitespace & indent guides",
            "Render spaces/tabs/end-of-line markers and indentation guide lines."),
        new EditorFeatureInfo(EditorFeature.TrimTrailingOnSave, "Trim whitespace on save",
            "Remove trailing whitespace and ensure a final newline when saving."),
        new EditorFeatureInfo(EditorFeature.MatchTerminator, "Match block terminator",
            "Jump between survey/endsurvey and highlight the matching pair."),
        new EditorFeatureInfo(EditorFeature.SmartEnter, "Smart Enter",
            "Auto-indent on Enter and auto-insert the matching endX for block openers."),
        new EditorFeatureInfo(EditorFeature.ReadingOrderNav, "Reading-order navigation",
            "Step into an included file and back, with a navigation backstack."),
    };

    public static EditorFeatureInfo Get(EditorFeature feature)
    {
        foreach (var info in All) if (info.Feature == feature) return info;
        throw new ArgumentOutOfRangeException(nameof(feature));
    }
}
