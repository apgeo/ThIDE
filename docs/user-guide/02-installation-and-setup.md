# 2. Installation & setup

> [← Back to the User Guide home](README.md)

This page gets ThIDE running and connected to the external tools it drives.

## 1. Prerequisites

| Requirement | Why | Notes |
|---|---|---|
| **.NET 8 runtime/SDK** (`8.0.x`) | ThIDE is a .NET 8 app | The SDK is needed if you build from source; a packaged release needs only the runtime. |
| **Therion toolchain** (`therion`) | Compiling caves | Not bundled. Install from the [Therion site](https://therion.speleo.sk/). ThIDE targets **v6.4.0**. |
| **A platform web engine** | The embedded 3D viewer & structural 3D plot | Windows: WebView2 (already on Windows 11 / modern Edge). macOS: WKWebView (built in). Linux: WebKitGTK — `apt install libwebkit2gtk-4.1-0`. |

Optional companions ThIDE can launch if present:

- **Loch** and/or **Aven** — external 3D/2D viewers for compiled models.
- **Survex** — provides `dump3d` and `extend` tools (see [Compiling & output](10-compiling-and-output.md)).
- **Mapiah** — a visual `.th2` sketch/scrap editor ThIDE can hand files off to.

## 2. Getting ThIDE

- **From a release build:** unpack/install it and launch the `ThIDE` executable.
- **From source:**

  ```sh
  git clone <your-fork-url> ThIDE
  cd ThIDE
  dotnet restore ThIDE.sln
  # -m:1 is required: the full solution can exhaust memory under parallel builds.
  dotnet build ThIDE.sln -m:1 -c Release
  dotnet run --project ThIDE
  ```

## 3. First launch

On first launch you'll see the **Welcome** page. From here you can create a new file, open an existing
file/`thconfig`/folder, jump to recent items, reach the external-tools settings, or open the Therion
Book. (You can reopen it any time with **Help → Welcome**, and turn off "Show this page on startup".)

## 4. Point ThIDE at your external tools

ThIDE auto-detects tools on your `PATH` and in the usual install locations, but you can set explicit
paths:

1. Open **Settings** (menu bar) → **External Tools**.
2. Each row shows the tool, whether it was **Detected**, its **Version**, and the detection
   **Source**. Use the **Override** column to point at a specific executable, and **Test** to
   confirm it runs.

If a tool is missing when you try to use it, ThIDE tells you which one and where to set it (for
example, the "Mapiah not found" dialog links straight to this settings page).

> ThIDE launches Therion, Loch, Aven and Mapiah as **separate programs** over the command line — it
> does not embed or link their code. See [External tools & licensing](../usage.md#external-tools).

## 5. Optional: file associations

To make Therion files open in ThIDE when you double-click them in your file manager, use
**Settings → File Associations** and click **Associate** (or **Associate all**). This is per-user and
needs no administrator rights. Details and platform notes: [docs/file-association.md](../file-association.md).

## 6. Where ThIDE stores your settings

User settings and per-project state live under your platform's application-data folder
(`%AppData%/ThIDE` on Windows; the XDG / `~/Library` equivalents on Linux/macOS): preferences,
keyboard shortcuts, theme and language, the symbol index and parse cache, crash-recovery buffers,
plugins, and per-project sidecars (leads, metadata). You rarely need to touch this folder directly,
but it's where a few features (declination model, plugins) ask you to drop files.

---

Next: [Getting started: your first session →](03-getting-started.md)
