# 10. Compiling & output

> [← Back to the User Guide home](README.md)

Compiling is where ThIDE hands your project to the real **Therion** program to produce the actual
maps and models. ThIDE orchestrates the run and makes the results easy to work with.

> You need Therion installed and detected for this — see [Installation & setup](02-installation-and-setup.md).

## Running a compile

| Command | Where | Does |
|---|---|---|
| **Compile** | Compile menu · toolbar hammer · Command Palette | Build the **active thconfig**. |
| **Recompile** | Compile menu | Force a clean rebuild. |
| **Cancel** | Compile menu · toolbar | Stop a running build. |
| **Build target: …** | Compile → Build target | Build a specific target from the config. |
| **Build a specific thconfig…** | Command Palette | Build a config other than the active one. |

### Unsaved files

If files involved in the build have unsaved changes, ThIDE asks: **Save & compile** or **Compile
without saving** (naming the affected files). Prefer the first. You can make this automatic with
*Save modified files automatically before compiling* ([Settings → Build & Output](19-settings-and-preferences.md#build--output)).

### Compile on save (optional)

Enable *Compile on save* (Settings → Build & Output) for a debounced background rebuild of the active
thconfig every time you save — watch the spinner and the **Log** panel. Off by default.

### Missing output folders

By default ThIDE **creates missing output folders** before compiling (Therion otherwise errors on a
missing directory); a line is logged when it does. Toggle under Settings → Build & Output.

## Watching it run: the Compiler Output panel

The **Compiler Output** panel streams Therion's messages live.

- **Output** tab — parsed, with clickable `file:line` messages that jump straight to the cause.
- **Raw** tab — the unfiltered console text.
- A **time column** you can switch between *Since last line*, *Timestamp*, and *Since build start* —
  handy for spotting which phase is slow.
- **Open log** opens Therion's own log file in the editor.

The status bar shows **Build in progress…** and then **Last build succeeded/failed**; a toast
notification summarizes artifacts and warnings (with **Show output**).

## The results: the Generated Files panel

Everything the build produced is listed in **Generated Files**, with columns **File Name · Auto-open
· State · Size · Modified** and, per row, action buttons:

- **Open externally** (Loch / Aven for models; the default app otherwise).
- **View in internal 3D viewer** (for `.lox`/`.3d`) — see [The 3D viewer](11-viewers.md#3d-viewer).
- **Reveal in File Manager**.
- **Go to Definition in thconfig** — jump to the `export` line that produced it.
- **Auto-open** — a 3-state control per file: **▣ default** (use your global setting) · **✓ always** ·
  **✗ never**. Your choice sticks across builds.

If a workspace input changed since the last build, a **stale** banner warns that outputs are out of
date — rebuild to refresh.

### What opens automatically after a build

Under **Settings → Build & Output → After a successful build, open:** tick any of **`.lox`** (Loch 3D),
**`.3d`** (Survex/Aven), **`.pdf`** (map export). *Open every matching output* opens them all rather
than just the first. Per-file Auto-open overrides (above) win over these defaults. Leave all unticked
to open nothing.

## One-off exports: Quick Export

**Compile → Quick Export…** composes a single export without editing your thconfig: pick a **format**,
an optional **survey** (blank = whole workspace), a **scale**, and an **output name**. ThIDE generates
a temporary thconfig from the active workspace, builds it, and opens the result. Great for a quick PDF
of one branch.

## Opening results externally

- **Compile → Open in Loch** / **Open in Aven** launches the external 3D/2D viewer on the output.
- **Compile → Open last output folder** reveals the folder in your file manager.

## Extra toolchain tools

If Survex/Therion provide them, the Compile menu exposes:

- **Survex tools (.3d):** **dump3d** (text dump of a `.3d`) and **extend** (extended elevation).
- **Therion info:** **Print version** and **Print environment** (also in the Command Palette). Useful
  when checking your toolchain setup or filing a bug.

---

Next: [The viewers: Map, 3D & Mainline preview →](11-viewers.md)
