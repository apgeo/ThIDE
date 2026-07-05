// Backs the Preferences ▸ File Associations tab (Task 5). Lists the syntax-supported Therion file
// types with their current association status and lets the user associate / remove them on demand
// (never automatically). Delegates to the platform IFileAssociationService.

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>One file-type row: extension + description + live status, with per-row associate/remove.</summary>
public sealed partial class FileAssociationRow : ObservableObject
{
    private readonly IFileAssociationService _svc;

    public string Extension { get; }
    public string Description { get; }

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isAssociated;
    /// <summary>False when the OS can't manage associations, so the row buttons are disabled.</summary>
    public bool CanManage { get; }

    public FileAssociationRow(IFileAssociationService svc, string ext)
    {
        _svc = svc;
        Extension = ext;
        Description = FileAssociationCatalog.DescriptionFor(ext);
        CanManage = svc.IsSupported;
        Refresh();
    }

    [RelayCommand] private void Associate() { _svc.Associate(Extension); Refresh(); }
    [RelayCommand] private void Unassociate() { _svc.Unassociate(Extension); Refresh(); }

    public void Refresh()
    {
        var info = _svc.GetInfo(Extension);
        IsAssociated = info.State == FileAssociationState.Associated;
        StatusText = info.State switch
        {
            FileAssociationState.Associated => Tr.Get("Assoc_StateAssociated"),
            FileAssociationState.AssociatedWithOther => string.Format(Tr.Get("Assoc_StateOtherFmt"), info.CurrentHandler),
            FileAssociationState.NotAssociated => Tr.Get("Assoc_StateNone"),
            _ => Tr.Get("Assoc_StateUnknown"),
        };
    }
}

public sealed partial class FileAssociationsViewModel : ObservableObject
{
    private readonly IFileAssociationService _svc;

    public bool IsSupported => _svc.IsSupported;
    /// <summary>Localized note describing how associations behave on this OS.</summary>
    public string PlatformNote { get; }
    public ObservableCollection<FileAssociationRow> Rows { get; } = new();

    [ObservableProperty] private string _status = string.Empty;

    public FileAssociationsViewModel(IFileAssociationService svc)
    {
        _svc = svc;
        PlatformNote = !svc.IsSupported ? Tr.Get("Assoc_NoteUnsupported")
            : OperatingSystem.IsWindows() ? Tr.Get("Assoc_NoteWindows")
            : OperatingSystem.IsLinux() ? Tr.Get("Assoc_NoteLinux")
            : Tr.Get("Assoc_NoteUnsupported");
        foreach (var ext in svc.SupportedExtensions) Rows.Add(new FileAssociationRow(svc, ext));
    }

    [RelayCommand]
    private void Refresh()
    {
        foreach (var r in Rows) r.Refresh();
        Status = string.Empty;
    }

    [RelayCommand]
    private void AssociateAll()
    {
        int ok = 0;
        foreach (var r in Rows) if (_svc.Associate(r.Extension)) ok++;
        foreach (var r in Rows) r.Refresh();
        Status = string.Format(Tr.Get("Assoc_MsgBulkFmt"), ok, Rows.Count);
    }

    [RelayCommand]
    private void UnassociateAll()
    {
        int ok = 0;
        foreach (var r in Rows) if (_svc.Unassociate(r.Extension)) ok++;
        foreach (var r in Rows) r.Refresh();
        Status = string.Format(Tr.Get("Assoc_MsgBulkFmt"), ok, Rows.Count);
    }
}
