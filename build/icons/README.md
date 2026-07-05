# Application icon

The ThIDE / **ThIDE** application icon: a surveyor's compass over a glowing cave arch,
with a bold two-tone "ThIDE" wordmark. Design candidates and the chosen master live in
[`candidates/`](candidates/); this directory holds the rendered, ready-to-ship assets.

## Files

| File | Used by |
|------|---------|
| `../../ThIDE/Assets/thide.ico` | **canonical app icon** — embedded in the Windows exe (`ApplicationIcon` in `ThIDE.csproj`) and set as the runtime `Window.Icon` (Linux/macOS). Frames: 16/24/32/48/64/128 (BMP) + 256 (PNG). |
| `thide.ico` | copy of the above (installer convenience). |
| `thide.icns` | macOS bundle icon (icp4/5/6 + ic07–ic14, 16→1024 incl. @2x). For a future `.app`/`.dmg`. |
| `png/thide-<n>.png` | Linux hicolor PNGs (16–512). The `.deb`/AppImage scripts currently extract 256 from the `.ico`; these are the loss-free source. |

The Windows Inno Setup script and the Linux `build-deb.sh` / `build-appimage.sh` all reference
`ThIDE/Assets/thide.ico`.

## Regenerating

The icons are rendered from code (SkiaSharp) so they stay pixel-consistent and font-independent
— the wordmark is rasterized from Segoe UI Bold, not left as live text. To rebuild after editing
the design in [`tool/Program.cs`](tool/Program.cs):

```powershell
dotnet run --project build/icons/tool -c Release
```

The tool locates the repo root by walking up to `ThIDE.sln` (or pass the root as arg 0)
and overwrites the assets listed above. The geometry mirrors
[`candidates/icon-5-compass.svg`](candidates/icon-5-compass.svg) in a 512-unit space; keep the two
in sync if you change the design.
