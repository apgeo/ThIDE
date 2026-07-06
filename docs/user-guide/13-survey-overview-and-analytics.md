# 13. Survey Overview & analytics

> [← Back to the User Guide home](README.md)

The **Overview** panel (**View → Overview**, labelled *Overview (Dashboard / Surveys / Audit)*) is
your project cockpit: a set of tabs that summarise the whole cave, computed live from the model. It's
where a coordinator spends a lot of time.

> All figures here are **preview-quality**, computed in-app without Therion's loop adjustment —
> excellent for tracking and sanity checks, but **Therion is the source of truth** for final adjusted
> lengths. Analytics can be switched off on very large workspaces
> ([Settings → Survey analytics](19-settings-and-preferences.md#survey-analytics)).

## The tabs

| Tab | What you get |
|---|---|
| **Dashboard** | At-a-glance cards: entry config, file count, **total length**, **depth**, **last build**; **Quick actions** (Build, Open output folder). |
| **Surveys** | Every survey with its length and rollups. |
| **Statistics** | Length breakdown (surface / underground / duplicate / splay), vertical range with **hi/lo stations**, horizontal extent, fixed points. |
| **Charts** | **Length by survey** (top 30) and **length by date**. |
| **Team** | Team members and their contribution; **Trips (by date)**; copy as CSV. |
| **Entrances** | Entrances / fixed points with coordinates. |
| **Quality** | Data-quality checks surfaced as a list. |
| **Audit** | On-demand structural gaps — see below. |
| **Leads** | Exploration-leads register — see [Leads, TODOs & metadata](14-leads-notes-and-metadata.md). |
| **TODOs** | Aggregated TODO/FIXME/QM comments — same page. |
| **Metadata** | Per-workspace sidecar notes — same page. |
| **Media** | Referenced `.xvi` and on-disk media, with orphans — same page. |

## Reading the numbers

- **Length breakdown** separates real cave passage from **surface**, **duplicate**, and **splay**
  shots, so your "cave length" means what you expect.
- **Vertical range** names the **highest and lowest stations**, not just the number — handy for
  writing up a cave's depth.
- **Length by date** and **Trips** turn your `date`/`team` lines into a survey-history timeline.

## The Audit tab

The **Audit** tab is **on-demand**: click **Calculate** (then **Recalculate** to refresh). It searches
for structural loose ends:

- **Orphan files** — Therion files not bound to *any* thconfig in the workspace (so they'd never be
  built).
- **Unreferenced scraps** — scraps not included in any map.
- **Unexported maps** — maps declared but not selected for export.

It's Calculate-gated because the search is heavier than the other tabs; run it before a release to
catch things that would silently drop out of the compiled output.

## Getting data out

Most grids offer **Copy (CSV)** / **Copy as Markdown** and per-table **Export CSV…**. For richer
deliverables, use the dedicated exporters and the HTML report — see
[Import, export & GIS](17-import-export-and-gis.md) — which also cover **entrances/fixed points**
export to KML/GeoJSON/GPX/CSV and **station/shot table** export.

---

Next: [Exploration leads, TODOs & metadata →](14-leads-notes-and-metadata.md)
