namespace ThIDE.Views.Docking;

/// <summary>
/// Compile-time visibility switches for Overview-panel tabs whose backing features aren't
/// finished yet. Flip a flag to <c>true</c> to re-enable the tab — the tab's XAML and its
/// view-models are kept intact behind the guard, they are just not shown.
/// </summary>
public static class OverviewFeatures
{
    /// <summary>Project metadata editor tab — hidden until it round-trips to the .th source.</summary>
    public const bool ShowMetadataTab = false;

    /// <summary>Media / background-scan manager tab — hidden until the asset workflow is complete.</summary>
    public const bool ShowMediaTab = false;
}
