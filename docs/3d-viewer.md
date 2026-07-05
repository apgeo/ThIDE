# Embedded 3D Model Viewer (VIS-01)

ThIDE can render a compiled cave model (`.lox` / `.3d`) in a dockable **3D Viewer** pane —
rotate / pan / zoom, color-by overlays, and **station picking that jumps back to the `.th` source**.

The renderer is [CaveView.js](https://github.com/aardgoose/CaveView.js) (MIT), vendored under
`ThIDE/Assets/caveview/` and hosted inside Avalonia 12's **`NativeWebView`**. The web view uses
each OS's *native* engine (WebView2 on Windows, WKWebView on macOS, WebKitGTK/WPE on Linux) — there is
**no bundled Chromium**.

## Enabling it

The feature is **off by default**. Turn it on in **Preferences → Build / Visualization**:

- *Embedded 3D model viewer (CaveView.js in a native web view) — VIS-01*
- *Auto-show the 3D model (.lox / .3d) after a build — VIS-01*

Once enabled, open it from **View → 3D Viewer** (or the command palette: *Toggle 3D Viewer*). It
appears in the right tool rail.

## Using it

- **Model dropdown** (top-left) lists every model the active thconfig produces — each `export model`
  target whose output is a `.lox`/`.3d`, plus any `.lox`/`.3d` detected next to the thconfig. Pick one
  to switch the displayed model. Targets that haven't been built yet are shown as *“— not built”*.
- **Auto-open:** switching to the 3D Viewer pane opens the default model (the first `export model`
  output that exists) even if it is old. When the file isn't from today the bottom bar shows
  *“Generated &lt;date&gt;. Recompile for a fresh model.”* A model older than its sources is also flagged
  **stale** (rebuild to refresh — BUILD-03).
- **Open…** loads any `.lox`/`.3d` file directly; after a build the newest model auto-loads
  (`.lox` preferred over `.3d`) when auto-show is on.
- **Control bar** (mirrors the public CaveView galleries): **orientation** (Plan / profile N·E·S·W),
  **camera** (Perspective / Orthographic), and feature toggles — **Walls** (on by default), **Splays**,
  **Surface**, **Stations**, **Labels**, **Names**, **Box** (bounding box), **Auto-rotate**, **Light**
  (directional), **HUD** (CaveView's heads-up display), and **Menu** (CaveView's own sidebar).
- **Color by:** Height · Survey · Length · Inclination · Plain (the last choice is remembered).
- **Full screen** puts the whole 3D panel onto a borderless full-screen window — press **Esc** (even
  when the 3D canvas has focus) or the **Exit full screen** button to return it to the dock.
  **Reset view** re-frames the model; **↺ Defaults** returns every switch to its default; **Refresh**
  reloads. **DevTools** opens the web engine's inspector where supported (else use F12 / right-click ▸
  Inspect; JS console output is also mirrored into the **Log** panel).
- Switching workspace/thconfig unloads the current model, then auto-opens the new project's default.
- **Click a station** in the 3D view to navigate the editor to where that station is declared in the
  `.th` source.
- **Show in 3D View** — right-click a station (or a shot's *From*/*To*) in the Measurements grid to
  select and frame it in the 3D view; the same is offered from the editor's hover card and
  right-click menu for station/survey references (a survey gets a bounding box).
- **Generated Files** rows carry per-row action buttons — open in the external viewer (Loch/Aven),
  **view in internal 3D viewer**, reveal in the file manager, and go to definition — plus an
  **Auto-open** 3-state checkbox (default = use the per-type setting · always · never) whose choice
  is remembered across builds.

## How it works

```
BuildViewModel artifacts ─▶ Model3DViewerViewModel ─▶ Caveview3DAssetHost (127.0.0.1 loopback)
       (.lox/.3d, stale?)          │  stages model + serves viewer.html + CaveView.js
                                   ▼
                          NativeWebView ⇄ viewer.html (CV2.CaveViewer)
   pick {station,x,y,z} ──▶ WebMessageReceived ──▶ StationSourceResolver ──▶ editor navigation
```

CaveView fetches the model over HTTP from its `surveyDirectory`, so a `file://` page would be
CORS-blocked. `Caveview3DAssetHost` therefore serves the vendored assets and the staged model from a
random-port loopback `HttpListener` bound to `127.0.0.1` only.

Station-label → source mapping is **best-effort / preview-quality**: a model station's full dotted
path is matched against the semantic symbol table (exact qualified name, `point@survey`, then a
unique bare last-name). Survey-path prefixes and equates are handled where unambiguous; otherwise the
status line reports that no unique source declaration was found rather than navigating.

## Requirements & fallback

- **Windows:** the WebView2 runtime ships with Windows 11 / modern Edge — no setup.
- **Linux:** install the WebKit engine, e.g. `apt install libwebkit2gtk-4.1-0`.
- **macOS:** WKWebView is built in.

If the web engine or the vendored assets are unavailable, the pane shows a friendly message and an
**Open externally (Loch / Aven)** button instead of a blank control.

## Updating the vendored CaveView.js

`Assets/caveview/CaveView/js/CaveView2.js` is the prebuilt UMD bundle (v2.9.0, three.js bundled in)
from the project's published site, alongside its `css/`, `images/`, `lib/` and the `LICENSE`. To
update it, drop in a newer `CaveView2.js` build from
<https://github.com/aardgoose/CaveView.js>. `viewer.html` is our own host page (the JS↔C# bridge) and
is not part of CaveView.

> **Local patch (re-apply after updating):** the bundle's info-page builds with
> `const n=i.loadedSource.getNames();`, which throws *“Cannot read properties of null (reading
> 'getNames')”* when the sidebar **Menu** is shown and the model is reloaded/refreshed — because we
> load models through `loadCave`, not CaveView's own cave-list, so the UI's source manager has no
> `loadedSource`. We null-guard that single call site to
> `const n=(i.loadedSource?i.loadedSource.getNames():[]);` (the info page then just omits the file
> name). Re-apply this one-token change whenever `CaveView2.js` is re-vendored.
