# Licensing

ThIDE is **dual-licensed** so the reusable parts can be embedded widely
while the assembled application stays copyleft.

| Component | Location | License |
|-----------|----------|---------|
| Reusable libraries (parser, semantics, workspace, build, LSP, CLI, …) | `src/**` | **MPL-2.0** |
| Application shell | `ThIDE/**` | **GPL-3.0-or-later** |
| Test projects (not distributed) | `tests/**` | not shipped; follow the project they exercise |

Each library directory carries its own `LICENSE` (MPL-2.0). The repository-root
`LICENSE` is the application's GPL-3.0 text.

## Why this split

- **Libraries — MPL-2.0 (file-level copyleft).** Anyone, including closed-source
  or differently-licensed tools, may embed them; only changes to the
  MPL-covered files must be shared back. This maximizes reuse while keeping the
  core open.
- **Application — GPL-3.0-or-later.** The assembled app is end-user software in a
  copyleft ecosystem (Therion, Survex, Aven are all GPL), so the whole app and
  its forks stay open.

## Compatibility

MPL-2.0 is compatible with the GNU licenses (MPL-2.0 §3.3, "Secondary
Licenses"). The library sources are **not** marked "Incompatible With Secondary
Licenses" (MPL Exhibit B), so they may be combined into **GPL-3.0** *and*
**AGPL-3.0** projects. GPL-3.0 and AGPL-3.0 are themselves cross-compatible
(§13 of each), so the GPL-3.0 application may likewise be combined with
AGPL-3.0 work.

## Relationship to Therion (no GPL propagation)

ThIDE does **not** link or embed Therion's source:

- The parsers under `src/Therion.Syntax` (and the other `src/` libraries) are an
  independent C# implementation of the Therion *file formats* — formats are not
  copyrightable.
- The `src/Therion.Blender` module's `.lox` (Therion loch) and `.3d` (Survex)
  readers are likewise **original C# written from the format specifications**, not
  ports: the `.3d` reader follows Survex's official `doc/3dformat.htm` specs and the
  `.lox` reader the record/chunk layout facts in `lxFile.*`, with CaveView.js (MIT)
  and Survex `img.c` consulted only as normative cross-checks — **no GPL code was
  copied or transliterated** (keeps GPL out of the MPL-2.0 library; see the module's
  reuse ledger and per-file attribution headers). Blender itself is **not** linked or
  bundled — it is located and run as a separate process.
- Therion, Mapiah, `loch`, `aven` and Blender are invoked as **separate processes**
  (arms-length `fork`/`exec` over command-line arguments and files), which does
  not create a derivative work under the GPL.

Third-party NuGet dependencies (Avalonia, Dock.Avalonia, CommunityToolkit.Mvvm,
Svg.Skia, Docnet/PDFium, WebView, …) and the vendored CaveView.js are all
permissive (MIT/BSD).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). By contributing you agree your
contributions are licensed under the project's licenses (MPL-2.0 for the
libraries, GPL-3.0-or-later for the app). No CLA or DCO sign-off is required.
