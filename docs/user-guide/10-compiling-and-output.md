# 10. Compiling & output

> [← Back to the User Guide home](README.md)

Compiling is where ThIDE hands your project to the real **Therion** program to produce the actual
maps and models. ThIDE orchestrates the run and makes the results easy to work with.

> You need Therion installed and detected for this — see [Installation & setup](02-installation-and-setup.md).

## Running a compile

| Command | Where | Does |
|---|---|---|
| **Compile** | **Compile → Compile** (F5) · toolbar hammer · Command Palette | Compile the **active thconfig** (the one selected in the drop-down of the Workspace Explorer), whichever tab has focus. Its tooltip names it. |
| **Compile This thconfig (Focused Tab)** | **Compile → Compile This thconfig (Focused Tab)** · Command Palette | Compile the thconfig open in the focused tab when it is not the active one, without making it active. |
| **Recompile** | **Compile → Recompile** (Ctrl+F5) | Run the compilation of the active thconfig again (Therion always processes the whole thconfig; there is no incremental mode to clear). |
| **Cancel** | **Compile → Cancel** (Shift+F5) · toolbar | Stop a running compilation. |
| **Build Target** | **Compile → Build Target** | Compile one export target of the active thconfig. *Build target* is Therion's own word for an `export` line, so it keeps the name. |
| **Compile a specific thconfig…** | Command Palette | Compile a config other than the active one. |

### Unsaved files

If files involved in the compilation have unsaved changes, ThIDE asks: **Save & compile** or **Compile
without saving** (naming the affected files). Prefer the first. You can make this automatic with
*Save modified files automatically before compiling* ([Settings → Compile & Output](19-settings-and-preferences.md#compile--output)).

### Compile on save (optional)

Enable *Compile on save* (**Settings → Compile & Output**) for a debounced background compilation of the
active thconfig every time you save — watch the spinner and the **Log** panel. Off by default.

### Missing output folders

By default ThIDE **creates missing output folders** before compiling (Therion otherwise errors on a
missing directory); a line is logged when it does. Toggle under **Settings → Compile & Output**.

## Watching it run: the Compiler Output panel

The **Compiler Output** panel streams Therion's messages live.

- **Output** tab — parsed, with clickable `file:line` messages that jump straight to the cause.
- **Raw** tab — the unfiltered console text.
- A **time column** you can switch between *Since last line*, *Timestamp*, and *Since compilation start* —
  handy for spotting which phase is slow.
- **Open log** opens Therion's own log file in the editor.

The first output line names the thconfig being compiled (click it to open the file). The status bar
shows **Compiling *name*…** and then the result; the Overview dashboard's **Last compilation** line
names the thconfig that was compiled (after a restart it reads *Never built this session* until you
compile again). A toast notification summarizes artifacts and warnings (with **Show output**).

## The results: the Generated Files panel

**Generated Files** lists the outputs of one thconfig, named in its header (**Outputs of *name* ·
built *time***). It follows the active thconfig: switch to another one and the list shows that
thconfig's last outputs, or *"… has not been built yet"* with a **Compile** button. After **Compile This
thconfig (Focused Tab)**, it shows that compilation's outputs until the active thconfig changes. If a compilation fails, the
outputs of the previous successful compilation stay listed and are marked **stale**, also after a restart.

Each output has columns **File Name · Auto-open · State · Size · Modified** and, per row, action buttons:

- **Open externally** (Loch / Aven for models; the default app otherwise).
- **View in internal 3D viewer** (for `.lox`/`.3d`) — see [The 3D viewer](11-viewers.md#3d-viewer).
- **Reveal in File Manager**.
- **Go to Definition in thconfig** — jump to the `export` line that produced it.
- **Auto-open** — a 3-state control per file: **▣ default** (use your global setting) · **✓ always** ·
  **✗ never**. Your choice sticks across compilations.

If a workspace input is newer than an output — also after a restart, because it goes by the file times —
a **stale** banner warns that outputs are out of date; recompile to refresh.

### What opens automatically after a compilation

Under **Settings → Compile & Output → After a successful compilation, open:** tick any of **`.lox`** (Loch 3D),
**`.3d`** (Survex/Aven), **`.pdf`** (map export). *Open every matching output* opens them all rather
than just the first. Per-file Auto-open overrides (above) win over these defaults. Leave all unticked
to open nothing.

## Compiling several at once

**Compile → Compile Multiple…** lists every thconfig of the workspace with its `export` lines. Tick the
thconfigs to compile and, under each, the exports to produce; they are compiled one after the other (a
thconfig with some exports unticked is built from a temporary copy with the others commented out).
*Stop at the first failure* ends the run early; **Cancel** stops it. A summary — result, errors,
warnings, time and outputs per thconfig — goes to the **Automation** panel, and your selection is
remembered for the project. From a terminal: `therion-cli build caves --all` (add `--json` for a
machine-readable summary).

## One-off exports: Quick Export

**Compile → Quick Export…** composes a single export without editing your thconfig: pick a **format**,
an optional **survey** (blank = whole workspace), a **scale**, and an **output name**. ThIDE generates
a temporary thconfig from the active workspace, builds it, and opens the result. Great for a quick PDF
of one branch.

## Opening results externally

- **Compile → Open in Loch** / **Open in Aven** launches the external 3D/2D viewer on the output.
- **Compile → Open Last Output Folder** reveals the folder in your file manager.

## Extra toolchain tools

If Survex/Therion provide them, the Compile menu exposes:

- **Survex Tools (.3d):** **dump3d** (text dump of a `.3d`) and **extend** (extended elevation).
- **Therion Info:** **Print Version** and **Print Environment** (also in the Command Palette). Useful
  when checking your toolchain setup or filing a bug.

---

Next: [The viewers: Map, 3D & Mainline Preview →](11-viewers.md)
