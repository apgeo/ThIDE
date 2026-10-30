using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Fixes;

namespace ThIDE.ViewModels;

/// <summary>Tools ▸ Normalise project: which house-keeping steps to preview.</summary>
public sealed partial class NormalizeProjectViewModel : ObservableObject
{
    [ObservableProperty] private bool _reindent = true;
    /// <summary>0 = keep each file's, 1 = LF, 2 = CRLF.</summary>
    [ObservableProperty] private int _lineEndings = 1;
    [ObservableProperty] private bool _toUtf8 = true;
    [ObservableProperty] private bool _sortInputs;

    public NormalizeOptions Options(string indentUnit) => new(
        Reindent ? indentUnit : null,
        LineEndings switch { 1 => Therion.Workspace.Fixes.LineEndings.Lf, 2 => Therion.Workspace.Fixes.LineEndings.CrLf, _ => Therion.Workspace.Fixes.LineEndings.Keep },
        ToUtf8,
        SortInputs);
}
