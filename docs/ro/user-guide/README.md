# Ghidul utilizatorului ThIDE

<!-- Ștampila de versiune — versiunea ThIDE față de care a fost verificat ultima dată conținutul acestui
     ghid, NU data la care a fost generat PDF-ul. Actualizați-o ori de câte ori actualizați ghidul (vezi
     about-this-guide.md) și păstrați forma „**For ThIDE <version>**”: build/build-user-guide.* citește
     această linie pentru a pune versiunea pe pagina de titlu a PDF-ului/HTML-ului. -->
> **For ThIDE 0.3.0-alpha.2** — conținutul ghidului actualizat ultima dată la 2026-10-25.
> Verificați în **Ajutor → Despre ThIDE** ce versiune rulați; dacă a dumneavoastră este mai nouă,
> unele ecrane s-ar putea să se fi mutat.

> 🇬🇧 **This documentation is also available in English:** [User Guide](../../user-guide/README.md).

Bine ați venit în ghidul utilizatorului ThIDE — un manual practic pentru speologii și topografii care
folosesc **ThIDE** pentru a scrie, verifica, compila și explora proiecte de topografie speologică
[Therion](https://therion.speleo.sk/).

Ghidul este orientat pe sarcini: explică *la ce folosește* fiecare parte a aplicației și cum o
utilizați efectiv în fluxul de lucru de la teren la hartă. Nu presupune că sunteți programator.
Presupune însă că sunteți oarecum familiarizat cu Therion (fișierele `.th` / `.th2` / `.thconfig` și
modul în care este descrisă o peșteră în ele). Dacă Therion vă este nou, țineți **Cartea Therion**
deschisă alături de acest ghid (**Ajutor → Cartea Therion**, inclusă în aplicație).

> **Sunteți nou?** Citiți în ordine [Introducere](01-introduction.md) → [Instalare și configurare](02-installation-and-setup.md)
> → [Primii pași](03-getting-started.md). Restul îl puteți consulta după nevoie.

---

## Cuprins

### Orientare
1. [Introducere — ce este și ce nu este ThIDE](01-introduction.md)
2. [Instalare și configurare](02-installation-and-setup.md)
3. [Primii pași: prima sesiune](03-getting-started.md)
4. [Concepte de bază și vocabular](04-core-concepts.md)
5. [Un tur al interfeței](05-interface-tour.md)

### Lucru
6. [Fluxuri de lucru tipice](06-typical-workflows.md)
7. [Editorul](07-editing.md)
8. [Navigare și căutare](08-navigation-and-search.md)
9. [Diagnostice și validare](09-diagnostics-and-validation.md)

### Compilare și vizualizare
10. [Compilare și rezultate](10-compiling-and-output.md)
11. [Vizualizatoarele: hărți, 3D și previzualizare principală](11-viewers.md)
- [Randări de animații Blender](23-blender-animation.md) — *experimental, în lucru* (pagina 23,
  la sfârșitul acestui ghid)

### Analizați-vă topografia
12. [Navigator obiecte și Contur](12-object-browser-and-outline.md)
13. [Prezentare generală a topografiei și analize](13-survey-overview-and-analytics.md)
14. [Piste de explorare, TODO-uri și metadate](14-leads-notes-and-metadata.md)
15. [Geologie structurală](15-structural-geology.md)
16. [Harta relațională](16-relational-map.md)

### Schimb de date
17. [Import, export și GIS](17-import-export-and-gis.md)
18. [Calculatoare și convertoare](18-calculators.md)

### Configurare și extindere
19. [Setări și preferințe](19-settings-and-preferences.md)
20. [Extensibilitate (CLI, LSP, asistenți AI, automatizare UI, plugin-uri, hook-uri)](20-extensibility.md) — include
    [panoul Asistent și integrarea MCP](20-extensibility.md#asistenți-ai-mcp), *experimentale*

### Referință
21. [Scurtături de tastatură](21-keyboard-shortcuts.md)
22. [Depanare și întrebări frecvente](22-troubleshooting-and-faq.md)
- [Despre acest ghid (cum îl editați și extindeți)](about-this-guide.md)

---

## Cum se leagă acest ghid de restul documentației

Folderul [`docs/`](../README.md) conține și material de **referință** care aprofundează câteva
subiecte (catalogul complet al codurilor de diagnostic, protocolul language server, API-ul de plugin-uri,
împachetarea). Acestea sunt legate din paginile relevante de aici. Ghidul utilizatorului este ușa de
intrare; documentele de referință sunt anexa.

- Prezentarea proiectului și pornire rapidă: [README-ul principal](../../../README.md)
- Lista completă de funcționalități (o pagină): [docs/features.md](../features.md)
- Toate codurile de diagnostic: [docs/diagnostics.md](../diagnostics.md)

---

*ThIDE se află într-un stadiu alpha de dezvoltare. Unele ecrane și scurtături s-ar putea să se fi mutat
de când a fost scris acest ghid — dacă ceva nu corespunde, vă rugăm să ne ajutați să-l corectăm (vezi
[Despre acest ghid](about-this-guide.md)).*
