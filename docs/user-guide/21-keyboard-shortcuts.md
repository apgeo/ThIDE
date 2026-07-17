# 21. Keyboard shortcuts

> [← Back to the User Guide home](README.md)

The shortcuts below are the **defaults**. Almost every one is **rebindable** in
**Settings → Keyboard Shortcuts** — click a command's gesture field and press your combination.
That settings page is the authoritative, always-current list; this page is a quick reference.

> On macOS, read **Ctrl** as **⌘** where your bindings map it that way.

**Not every command ships with a gesture.** Plenty of them — *Open File*, *Open Folder*, *Split
Editor*, *Quick Export*, *Duplicate Lines*, the panel toggles, and others — are deliberately
**unbound by default** and reachable from the menus or the Command Palette instead. They still appear
as rows in **Settings → Keyboard Shortcuts**, so you can assign whatever you like. If a shortcut you
expect from another editor does nothing, that is why — bind it there.

## Files & navigation

| Shortcut | Command |
|---|---|
| `Ctrl+S` | Save |
| `Ctrl+P` | Go to File (quick open) |
| `Ctrl+Shift+P` | Command Palette (Go to Action) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Cycle open documents & panels, most-recent first (hold `Ctrl`, tap `Tab`) |
| `Ctrl+Shift+T` | Reopen Closed Tab |
| `Alt+Left` / `Alt+Right` | Navigate Back / Forward |

## Editing

| Shortcut | Command |
|---|---|
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | Cut / Copy / Paste |
| `Ctrl+/` | Toggle Comment |
| `Ctrl+Shift+R` | Enclose in Region |
| `Shift+Alt+F` | Format Document |
| `Ctrl+.` | Quick Fix |
| `Ctrl+Space` | Trigger Completion |
| `Ctrl+G` | Go to Line |

## Code intelligence

| Shortcut | Command |
|---|---|
| `F12` | Go to Definition |
| `Shift+F12` | Find References |
| `F2` | Rename Symbol (across the project) |
| `Alt+F12` | Peek Definition |
| `Ctrl+]` | Go to Matching Block |
| `Alt+↓` / `Alt+↑` | Step Into / Step Out of Included File |

## Search & diagnostics

| Shortcut | Command |
|---|---|
| `Ctrl+Shift+F` | Find in Files |
| `Ctrl+Shift+H` | Replace in Files |
| `F8` / `Shift+F8` | Next / Previous diagnostic |
| `Ctrl+Shift+D` | Toggle the Diagnostics panel |

## Build & external viewers

| Shortcut | Command |
|---|---|
| `F5` | Build the active thconfig |
| `Ctrl+F5` | Rebuild (force a full recompile) |
| `Shift+F5` | Cancel the running build |
| `F9` | Open the compiled model in **Loch** |
| `F10` | Open the compiled model in **Aven** |

See [Compiling & output](10-compiling-and-output.md).

## Panels & window

| Shortcut | Command |
|---|---|
| `Ctrl+Alt+L` | Toggle the Workspace Explorer |
| `Ctrl+Shift+D` | Toggle the Diagnostics panel |
| `F11` | Toggle full screen |

The other panel toggles (Object Browser, Outline, Project, Log, Live Preview, Map Viewer, 3D Viewer,
Structural Geology, Blender Animation) ship **unbound** — use the **View** menu, or bind them
yourself in **Settings → Keyboard Shortcuts**.

## Command Palette prefixes

Inside the palette (`Ctrl+Shift+P`):

| Type | Jumps to |
|---|---|
| `@name` | A symbol in the current document |
| `#name` | A symbol in the whole workspace |
| `:42` | Line 42 of the current file |

## Mouse conventions

| Action | Effect |
|---|---|
| **Ctrl+click** an identifier | Go to definition (always, regardless of the click-nav setting) |
| **Double-click** an identifier | Go to definition (or place caret — see [Settings → Editor](19-settings-and-preferences.md#editor)) |
| **Ctrl+scroll** in a viewer | Zoom (Map Viewer, Relational Map) |
| **Double-click** a grid/table row | Jump to source |

---

Next: [Troubleshooting & FAQ →](22-troubleshooting-and-faq.md)
