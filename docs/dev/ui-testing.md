# UI testing and automation

ThIDE has two complementary ways to drive the real, running app, for unattended test runs as well
as for interactive use by a developer or a coding agent.

| | In-app automation server | AT-SPI driver (`uidrive`) |
|---|---|---|
| Talks to | Avalonia's automation-peer tree, from inside the process | the Linux accessibility bus, from outside |
| Protocol | MCP over loopback HTTP (`ui_*` tools) | Python library + CLI |
| Platforms | Windows, Linux, macOS | Linux |
| Input | invoke/toggle/select/expand patterns, routed key and text events | AT-SPI actions, real X11 mouse and keyboard (xdotool) |
| Can read | everything the tree has, plus the code editor's text and any property path (`DataContext.Status`) | names, states, bounds, text of text boxes |
| Screenshots | rendered by the app (no display needed; native web views come out blank) | real screen capture (includes the 3D web view) |
| Tests | `tests/ThIDE.UiTests` (C#, xUnit) | `tests/e2e-linux` (pytest) |

Both use the same selector language and the same invented fixture project (`tests/ui-fixtures/`).

## Running the tests

```sh
tests/run-ui-tests.sh            # both suites
tests/run-ui-tests.sh mcp        # C# suite only
tests/run-ui-tests.sh atspi      # AT-SPI suite only (Linux)
tests/run-ui-tests.sh atspi -k 3d -x   # extra args go to pytest / dotnet test
```

On Linux the script runs everything on a private Xvfb display with its own D-Bus session and a
throw-away profile, so it needs no desktop and never touches your settings. Results, including a
screenshot, the element tree and the app log for every failed AT-SPI test, go to `tests/ui-artifacts/`.

`dotnet test ThIDE.sln` stays a unit-test run: the UI tests skip themselves unless `THIDE_UI_TESTS=1`.

Linux prerequisites (Ubuntu): `xvfb xfwm4 at-spi2-core xdotool python3-gi gir1.2-atspi-2.0
python3-pil python3-pytest libwebkit2gtk-4.1-0`. Install `therion` and `survex` too for the compile
and 3D tests (they skip without Therion).

## Selectors

| Selector | Matches |
|---|---|
| `#Toolbar.OpenFile` | automation id (`AutomationProperties.AutomationId`, else `x:Name`) |
| `name:3D Viewer` or `3D Viewer` | exact accessible name |
| `name~:^Open` / `text~:demo\.lox` | regex on name / on text content |
| `role:TabItem` | automation control type (`role:page tab` in AT-SPI terms) |
| `type:TextEditor` | control class (in-app server only) |
| `state:selected` | selected, focused, checked, expanded, enabled, showing (in-app server; uidrive has `--state`) |
| `a;b` | both terms |
| `#Viewer3D.View >> role:Text` | a descendant |
| `...@2` | the third match |

Automation ids follow `<Area>.<Thing>`: `Menu.File`, `Menu.Build.Build`, `Toolbar.Compile`,
`Welcome.OpenWorkspaceFolder`, `Viewer3D.ResetView`, `Diagnostics.Filter`,
`WorkspaceExplorer.Tree`, `Preferences.Ok`. New interactive controls should get one, and icon-only
controls also need a localized `AutomationProperties.Name` (usually the tooltip's key).

Tools that act on an element wait up to 5 s for it to appear, so a call right after a click doesn't
race the UI.

## Driving the app by hand or from an agent

```sh
U="python3 tools/uidrive/uidrive.py"
$U start --automation --project tests/ui-fixtures/demo-cave/demo.thconfig
$U tree                                   # what's on screen (compact; --hidden / --raw for more)
$U click "role:page tab;name:3D Viewer"   # AT-SPI action; --mouse for a real click
$U type "# hello" --into "#Editor"        # real keyboard
$U shot --of "#Viewer3D.View"             # PNG path is printed
$U call ui_get '{"selector": "type:TextEditor"}'           # in-app server: full editor text
$U call ui_menu '{"path": "Build > Build"}'
$U call ui_screenshot '{"selector": "#Toolbar.Panel"}' --png toolbar.png
$U log                                    # app stdout/stderr
$U stop
```

The session (display, bus, profile, app) survives the CLI exiting, so an agent can issue one
command per step and reconnect after a disconnect. Use `--session NAME` to run several side by side.

To give an MCP-capable agent the `ui_*` tools natively, start with `--automation` (the `default`
session listens on port 47800) and register the server, e.g. copy `tools/uidrive/mcp.example.json`
into your local MCP config. Its `headersHelper` reads the bearer token of the running session.

## The in-app server

`ThIDE/Services/UiAutomation/`. It starts only when the process is launched with
`THIDE_UI_AUTOMATION=1`, binds to 127.0.0.1, requires a bearer token, and writes its endpoint
(`port`, `token`, `url`) to `THIDE_UI_AUTOMATION_ENDPOINT` (default
`<app data>/ThIDE/ui-automation-endpoint.json`, owner-only). `THIDE_UI_AUTOMATION_PORT` and
`THIDE_UI_AUTOMATION_TOKEN` pin the port and token. It is separate from the user-facing project MCP
server and is never enabled by a setting.

Tools: `ui_windows`, `ui_tree`, `ui_find`, `ui_get`, `ui_invoke`, `ui_menu`, `ui_set_text`, `ui_key`,
`ui_type`, `ui_wait`, `ui_screenshot`.

## Known limits

- Avalonia's MenuItem peers have no invoke/expand pattern; the server drives `MenuItem` directly and
  `uidrive click` falls back to a real mouse click.
- The AvaloniaEdit editor has no AT-SPI text interface: type into it with `uidrive`, read it with
  `ui_get`.
- Keys sent through the in-app server are routed events, not OS input; use `uidrive key` when the
  platform input path itself is under test.
- On Windows/macOS the C# suite runs against your real ThIDE profile (only Linux is isolated).
