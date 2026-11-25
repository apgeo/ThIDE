using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ThIDE.Resources;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class BlenderAnimationToolView : UserControl
{
    public BlenderAnimationToolView()
    {
        InitializeComponent();
        // The file dialog hangs off the window, so the view lends it to the view model, which
        // decides what a chosen file means.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BlenderAnimationToolViewModel { Blender: { } blender }) blender.PickFile = PickFileAsync;
        };
    }

    private async Task<string?> PickFileAsync(BlenderFileKind kind)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return null;
        var (title, type, patterns) = kind switch
        {
            BlenderFileKind.HdriImage => ("Blender_UI_BrowseHdri", "Blender_Pick_HdriImages", new[] { "*.hdr", "*.exr" }),
            BlenderFileKind.RevealDates => ("Blender_UI_BrowseRevealDates", "Blender_Pick_Models", new[] { "*.3d", "*.lox" }),
            BlenderFileKind.Annotations => ("Blender_UI_BrowseAnnotations", "Blender_Pick_Annotations", new[] { "*.json" }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.Get(title),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Tr.Get(type)) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
