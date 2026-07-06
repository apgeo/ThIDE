# 14. Exploration leads, TODOs & metadata

> [← Back to the User Guide home](README.md)

Three related tabs in the **Overview** panel help you manage the *unfinished* parts of a cave and the
notes around it. All are project-wide and update from the live model.

## Exploration leads

The **Leads** tab is a register of every place the cave might continue — built automatically from your
survey. It picks up the usual conventions: continuation flags, `QM` / lead / `?` comments, and
dead-ends (passage ends).

**What it shows**

- Each lead with a **status**: **Open**, **Checked**, **Pushed**, or **Dead**.
- **Show all passage ends** widens the net to every dangling end, not just explicitly-marked leads.
- **Copy as Markdown** to drop the list into a trip report or wiki.

**On the map**

Leads can be **overlaid on the Mainline Preview**, so you can see *where* the going leads are, not
just a list. This makes trip planning ("which leads are near each other?") much easier.

**Keeping it live**

By default the register **recomputes as you type** (from the unsaved editor buffers, debounced), so
it's always current. On very large workspaces you can turn that off
([Settings → Build & Output](19-settings-and-preferences.md#build--output)) and refresh manually with
the **Re-scan** button — which always works regardless of the setting.

## TODOs

The **TODOs** tab aggregates **TODO / FIXME / QM** comments from across the project into one list, so
loose notes-to-self don't get buried in individual files. Click an entry to jump to it. The scan is
part of Survey analytics and can be disabled
([Settings → Survey analytics → TODO / FIXME / QM comment scan](19-settings-and-preferences.md#survey-analytics)).

## Metadata

The **Metadata** tab is a **per-workspace sidecar** for notes that don't belong in the `.th` source —
things like a project description, licence, region, or free-form notes. It's stored *alongside* your
files (not inside them), so it never interferes with Therion. **Save** writes it; **Reload** re-reads
it.

## Media

The **Media** tab scans for media the project references — chiefly the `.xvi` sketches used by scraps
— and flags **on-disk orphans** (media present but unreferenced). Useful for tidying an image folder
before archiving. It's part of Survey analytics and can be disabled
([Settings → Survey analytics → Media file scan](19-settings-and-preferences.md#survey-analytics)).

---

Next: [Structural geology →](15-structural-geology.md)
