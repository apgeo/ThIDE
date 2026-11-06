# Embedded 3D Model Viewer

ThIDE can render a compiled cave model (`.lox` / `.3d`) in a dockable **3D Viewer** pane —
rotate / pan / zoom, color-by overlays, and **station picking that jumps back to the `.th` source**.

The renderer is [CaveView.js](https://github.com/apgeo/CaveView.js) (MIT) — the enhanced fork of
[aardgoose/CaveView.js](https://github.com/aardgoose/CaveView.js) that SilexGIS also uses — carried by
the `Therion.CaveView` library and hosted inside Avalonia 12's **`NativeWebView`**. The web view uses
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
  fly the 3D view to it and put a **crosshair** on it, which follows the station as you turn the model
  and stays until another station is shown or the model is reloaded; the same is offered from the
  editor's hover card and right-click menu for station/survey references (a survey is framed instead).
  The **🔍 Close-up** switch in the toolbar decides how: on (the default, remembered) it zooms in on
  about 30 m around the station; off it only centres the station at the current zoom. Either way the
  direction you look from is kept. A station clicked in the model gets the crosshair too.
- **Splays** — on a splay row, *Show To* (the anonymous `-` / `.` end) points the crosshair at the end
  of that splay: the model's splays from the station are compared with the shot's compass and clino
  and the closest one is marked (the status line says when the nearest one differs by more than 25°,
  or when the model has no splays there). The Splays layer is switched on for it.
- A name the source spells differently from the model — one survey level more or less, because the
  model was compiled from a parent or a child survey — is matched to the one model station whose path
  ends with it. The status line says what was found, or that the name is not in the loaded model.
- **🌍 Web page…** exports the loaded model as a stand-alone web page — see below.
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
   "Show in 3D" ──▶ cvShow(name, {zoom, splay}) ──▶ station-mark.js ──▶ {type:"shown", …} ──▶ status
```

CaveView fetches the model over HTTP from its `surveyDirectory`, so a `file://` page would be
CORS-blocked. `Caveview3DAssetHost` therefore serves the vendored assets and the staged model from a
random-port loopback `HttpListener` bound to `127.0.0.1` only: `viewer.html` (our JS↔C# bridge) from
the app's resources, `/CaveView/**` and `/assets/station-mark.js` from `Therion.CaveView`. The page is
opened with `?lang=ro` when the interface is Romanian, so CaveView's own controls follow.

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

## Exporting the model as a web page

**🌍 Web page…** writes the loaded model as a folder that works on **any web server** — a club
website, GitHub Pages, a plain `python3 -m http.server`: no server code, nothing fetched from
elsewhere. The dialog asks for a **title** and a line of **text**, and offers to:

- **list places** — the model's entrances and the stations the surveyors commented; choosing one
  flies the model there and puts the crosshair on it (with the same close-up switch beside the list);
- **open with the current view** — the layers, colouring and orientation set in the toolbar now;
- **work opened straight from a disk** — carries the model inside the page (`model/model.js`, about a
  third larger), for sending the folder to someone who will just double-click `index.html`. Without
  it, a page opened from a disk cannot read its model file (a browser rule), and says so.

The page has CaveView's toolbar (views, colouring, layers, rotation, full screen), adapts to a phone,
and can be linked to a place: `index.html#station=<full station name>`. Its `README.txt` says how to
publish it. The page is written by the `Therion.CaveView` library (`ModelPageExporter`), which any
.NET application can use the same way — SilexGIS included.

## Updating CaveView.js

The runtime lives in `src/Therion.CaveView/Runtime/` — version **2.9.0-slx.23** of the fork — and its
`README.md` records the source commit, the bundle's SHA-256 and how to update it. Changes to the
viewer itself are made in the fork, under a new distribution version, never in the vendored files.
The earlier local patch to the information page (a null `loadedSource` when the sidebar **Menu** is
shown) is fixed in the fork since slx.22.
