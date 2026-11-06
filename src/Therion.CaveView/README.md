# Therion.CaveView

The [CaveView.js](https://github.com/apgeo/CaveView.js) 3D survey viewer for .NET hosts, and a
stand-alone web page written around it.

- **`CaveViewRuntime`** — the viewer runtime (bundle, workers, stylesheet, logo, WebGL detector,
  interface catalogues) as embedded resources, by relative path. Serve them as the viewer's `home`
  directory. ThIDE's 3D viewer serves them from its loopback host.
- **`ModelPageExporter`** — writes a folder that shows one model on any static web server:
  `index.html`, `assets/` (the page's script and stylesheet, and `station-mark.js`),
  `caveview/<version>/` (the runtime) and `model/`. `Plan()` lists the files without writing,
  `WriteToDirectory()` and `WriteZip()` write them.
- **`ModelPlaces`** — the entrances and commented stations of a `.lox` / `.3d` file, named by the
  path CaveView gives them, as the places a page lists.
- **`Page/assets/station-mark.js`** — flies a viewer to a station (centred, or a close-up) and draws a
  crosshair that follows it; points at a splay through its station and measured direction. Used by the
  exported page and by ThIDE's viewer.

```csharp
var model = ModelPageModel.FromFile("cave.lox");
var options = new ModelPageOptions
{
    Title = "Peștera Mare",
    Description = "Survey 2019–2026",
    Language = "ro",
    Places = ModelPlaces.FromFile("cave.lox"),
};
ModelPageExporter.WriteToDirectory(model, options, "out/pestera-mare");   // or WriteZip(model, options, stream)
```

A page links to a station with `index.html#station=<full name>`; other links on a page holding the
model can fly it with `<a href="#" data-station="<full name>">`. A page opened straight from a disk
(`file://`) cannot fetch its model: serve the folder, or export with `EmbedModel = true`.

## The vendored runtime

`Runtime/` is a subset of the fork's build: `js/CaveView2.min.js`, `js/workers/*.js` (without their
`.min` twins, which nothing asks for), `css/caveview.css`, `images/logo.svg`, `lib/Detector.js`,
`lib/lang-{ro,it,sl}.json` and the MIT `LICENSE`.

| | |
|---|---|
| version | **2.9.0-slx.23** (`CaveViewRuntime.Version`, and `CV2.VERSION` in the bundle) |
| source | `apgeo/CaveView.js`, branch `silexgis`, commit `d5a593a2` (the stamp; `e6363220`, the branch tip, differs only in docs) |
| `js/CaveView2.min.js` | SHA-256 `74be814f67c86a23cc93dee2f9e84939f60ffaf963b8ab0f35c1b41e34efcee9` |

slx.22 added over slx.21: `focusStation(ref, { span })` — a close-up of a station;
`getStationScreenPosition(ref, offset)` and `getStationSplays(ref)` — where to draw a host's own mark;
and the side panel's information page no longer throws for a model loaded through `loadCave()`.
slx.23 adds a live marker placed at a splay's far end (`{ station, offset }`, flagged by
`CV2.LIVE_MARKER_OFFSETS`), and on touch a station is selected as the tap ends rather than as it begins,
so turning the model from a station selects nothing (`CV2.STATION_SELECTED_ON_TAP`). Only the bundle
differs from slx.21; every other file is byte-identical.

To update: build the fork (`npm ci && npm run build`, then `npm test`), copy the subset above from its
`build/CaveView/` into `Runtime/`, set `CaveViewRuntime.Version` to the new distribution version (a test
checks the bundle names it), and update the table above. Do not edit the vendored files in place: a
change belongs in the fork, under a new version.
