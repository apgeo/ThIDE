# Opening files with TherionProc (UX-09)

TherionProc accepts file paths in three ways:

1. **Drag-and-drop** — drop one or more `.th` / `.th2` / `.thconfig` files onto the main
   window and they open as tabs. (Implemented in `MainWindow` `OnDrop`.)
2. **Command line** — `TherionProc path/to/cave.thconfig path/to/survey.th` opens those files
   after the window appears. Paths that don't exist or that start with `-`/`/` are ignored.
   (Implemented in `App.ParseFileArgs` → `MainWindow.OpenStartupFileArgs`.)
3. **OS file association ("Open with TherionProc")** — once associated, double-clicking a
   `.th`/`.thconfig` (or right-click → *Open with*) launches the app with the file path as an
   argument, which path 2 then opens.

The in-app handling (paths 1 & 2) ships in the binary. Registering the association (path 3) is a
packaging/installer concern and is OS-specific:

## Windows

Associate per-user via the registry (replace the exe path):

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\.thconfig]
@="TherionProc.thconfig"

[HKEY_CURRENT_USER\Software\Classes\TherionProc.thconfig]
@="Therion project configuration"

[HKEY_CURRENT_USER\Software\Classes\TherionProc.thconfig\shell\open\command]
@="\"C:\\Path\\To\\TherionProc.exe\" \"%1\""
```

Repeat for `.th` / `.th2`. An MSI/MSIX installer should write these (and a `ProgID` with an icon)
during install and remove them on uninstall.

## Linux

Ship a `.desktop` file with `MimeType=` and register a MIME type:

```ini
[Desktop Entry]
Type=Application
Name=TherionProc
Exec=therionproc %F
MimeType=application/x-therion-config;application/x-therion;
```

Define the MIME globs (e.g. `*.thconfig`, `*.th`, `*.th2`) in a
`~/.local/share/mime/packages/therionproc.xml` and run `update-mime-database` +
`update-desktop-database`.

## macOS

Declare `CFBundleDocumentTypes` (and a `UTExportedTypeDeclarations` UTI for `.thconfig`/`.th`) in
the app bundle's `Info.plist`. macOS then passes opened files to the app; Avalonia surfaces them
on the command line / open-file events.
