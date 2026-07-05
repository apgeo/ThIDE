# Installers (`/build`)

Native application installers for ThIDE. Both wrap the same **self-contained, single-file**
publish, so the target machine needs no system .NET runtime. They are produced automatically for
`v*` tags by [`.github/workflows/release.yml`](../.github/workflows/release.yml) and can also be
built locally with the scripts here. See [docs/release.md](../docs/release.md) for the release flow.

| Platform | Format | Script | Tooling |
|----------|--------|--------|---------|
| Windows  | `ThIDE-Setup-<ver>.exe` | [`windows/build-installer.ps1`](windows/build-installer.ps1) → [`windows/ThIDE.iss`](windows/ThIDE.iss) | [Inno Setup 6](https://jrsoftware.org/isdl.php) (`ISCC.exe`) |
| Linux    | `thide_<ver>_amd64.deb` | [`linux/build-deb.sh`](linux/build-deb.sh) | `dpkg-deb` (+ `icoutils`/ImageMagick for the icon) |
| Linux    | `ThIDE-<ver>-x86_64.AppImage` | [`linux/build-appimage.sh`](linux/build-appimage.sh) | `appimagetool` (auto-downloaded; + `icoutils`/ImageMagick) |

They create a Start-Menu / application-menu entry and register the Therion file types
(`.th .th2 .thconfig .thc .thl .xvi`, mirroring `FileAssociationCatalog`). The installers support
clean uninstall (Add/Remove Programs on Windows, `apt remove thide` on Linux); the **AppImage**
is portable — a single self-contained executable that runs on most distros with no install (run it
directly, or with `--appimage-extract-and-run` if the host lacks FUSE). The file-type list is kept in
sync with the app by `InstallerAssociationConsistencyTests`.

## Build locally

### Windows

```powershell
# Needs Inno Setup once:  choco install innosetup -y
pwsh build/windows/build-installer.ps1 -Version 0.3.0
# -> build/windows/Output/ThIDE-Setup-0.3.0.exe
```

### Linux

```bash
dotnet publish ThIDE/ThIDE.csproj -m:1 -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
build/linux/build-deb.sh      publish/linux-x64 0.3.0   # -> ./thide_0.3.0_amd64.deb
build/linux/build-appimage.sh publish/linux-x64 0.3.0   # -> ./ThIDE-0.3.0-x86_64.AppImage
```

macOS is still shipped as a portable `.tar.gz`; a signed/notarized `.dmg` is a future step
(see [docs/release.md](../docs/release.md)).
