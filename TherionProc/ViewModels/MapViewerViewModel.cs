// in-app map viewer (PNG/SVG/PDF). Renders via IMapRenderService into an Avalonia bitmap
// shown in a zoomable/scrollable view; PDF gets page navigation. reuses it: ShowLatest()
// loads the newest renderable build artifact after a compile.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed partial class MapViewerViewModel : ObservableObject
{
    // Render at a slightly higher base resolution than 1:1 so PDF/SVG stay crisp; the view's zoom
    // transform scales the bitmap on top of this.
    private const double RenderScale = 1.6;

    private readonly IMapRenderService? _render;
    private readonly IShellOpener? _shell;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _status = "Open a PNG, SVG or PDF — or build a map.";
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private double _zoom = 1.0;

    public bool HasImage => Image is not null;
    public bool HasMultiplePages => PageCount > 1;
    public bool HasFile => !string.IsNullOrEmpty(CurrentPath);
    public string PageLabel => $"{PageIndex + 1} / {PageCount}";

    public MapViewerViewModel() { } // design-time
    public MapViewerViewModel(IMapRenderService render, IShellOpener shell)
    {
        _render = render;
        _shell = shell;
    }

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(HasMultiplePages));
    partial void OnCurrentPathChanged(string? value) => OnPropertyChanged(nameof(HasFile));

    /// <summary>Loads and renders the first page of <paramref name="path"/>.</summary>
    public void Load(string path)
    {
        CurrentPath = path;
        PageIndex = 0;
        Zoom = 1.0;
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        if (_render is null || string.IsNullOrEmpty(CurrentPath)) return;
        if (!_render.CanRender(CurrentPath))
        {
            Image = null;
            Status = $"Can't preview {Path.GetExtension(CurrentPath)} in-app — use “Open externally”.";
            return;
        }

        var result = _render.Render(CurrentPath, PageIndex, RenderScale);
        Image = result.Image;
        PageCount = Math.Max(1, result.PageCount);
        Status = result.Ok ? $"{Path.GetFileName(CurrentPath)}  ·  {PageLabel}" : (result.Error ?? "Render failed.");
        OnPropertyChanged(nameof(PageLabel));
    }

    /// <summary>load the most recently written renderable map artifact from a build.</summary>
    public void ShowLatest(IEnumerable<string> artifactPaths)
    {
        if (_render is null) return;
        var best = artifactPaths
            .Where(p => _render.CanRender(p))
            .OrderByDescending(SafeWrite)
            .FirstOrDefault();
        if (best is not null) Load(best);
    }

    private static DateTime SafeWrite(string p)
    {
        try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; }
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(8.0, Math.Round(Zoom * 1.25, 3));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.1, Math.Round(Zoom / 1.25, 3));
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;
    [RelayCommand] private void Refresh() => RenderCurrent();
    [RelayCommand] private void NextPage() { if (PageIndex < PageCount - 1) { PageIndex++; RenderCurrent(); } }
    [RelayCommand] private void PrevPage() { if (PageIndex > 0) { PageIndex--; RenderCurrent(); } }
    [RelayCommand] private void OpenExternally() { if (HasFile) _shell?.Open(CurrentPath!); }
}
