# 11. Vizualizatoarele: hărți, 3D și previzualizare principală

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE vă oferă trei moduri de a vă *vedea* peștera fără să părăsiți aplicația. Două arată rezultate **compilate**
(**Vizualizator hărți** și **Vizualizator 3D**); unul arată o **previzualizare în timp real** direct din model, fără nicio
compilare (**Previzualizare principală**). Toate trei sunt panouri andocabile din meniul **Vizualizare** și fiecare poate fi
activat/dezactivat din **Setări → Vizualizare**.

---

## Vizualizator hărți

Un vizualizator integrat pentru **hărți 2D** compilate — **PNG, SVG și PDF**. Deschideți-l din **Vizualizare → Vizualizator hărți**
(sau **Paletă de comenzi** → *Comută Vizualizatorul de hărți*).

**Încărcarea unei hărți**

- **Deschide…** încarcă direct orice fișier PNG/SVG/PDF.
- **Selectorul de rezultate** listează hărțile declarate de thconfig-ul activ prin `export map` care există pe
  disc — alegeți una pentru a o afișa.
- **Afișare automată:** cu *Afișează automat harta randată după o compilare* (sau *…prima hartă la deschiderea spațiului de lucru*)
  activată, ThIDE vă deschide prima hartă potrivită. O setare de **Deschidere automată** pe fișier poate impune o
  anumită hartă.

**Deplasarea pe hartă**

- **Zoom** cu butoanele `–` / `+` sau cu **Ctrl+derulare**; **100%** resetează; **Încadrează** micșorează harta pentru a încăpea în
  fereastră (nu o mărește niciodată peste 100%).
- **Navigarea între paginile PDF** (**◀ / ▶**) apare pentru PDF-urile cu mai multe pagini.
- **Reîmprospătează** randează din nou (de ex. după o recompilare). Randarea se face în afara firului interfeței, așa că un PDF mare
  nu blochează aplicația — cât timp lucrează, se afișează un indicator de activitate.
- Butoanele **Ecran complet**, **desprindere pe alt monitor** și **mutare în panoul central** se află în
  colțul din dreapta sus.
- Subsolul arată numele fișierului, pagina, procentul de zoom, dimensiunea fișierului și data ultimei modificări.

> Preferați ca PDF-urile pe care faceți clic să se deschidă aici și nu într-o aplicație externă? Activați *Deschide PDF-urile pe care faceți clic în
> vizualizatorul intern* din [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare).

---

## Vizualizator 3D

Un **vizualizator 3D încorporat** care randează modelele de peșteră compilate `.lox` / `.3d` chiar în ThIDE,
folosind [CaveView.js](https://github.com/aardgoose/CaveView.js) în motorul web nativ al sistemului de operare (fără
browser inclus). Deschideți-l din **Vizualizare → Vizualizator 3D**.

> **Dezactivat implicit.** Activați *Vizualizator 3D încorporat* (și, opțional, *Afișează automat modelul 3D
> după o compilare*) din **Setări → Vizualizare**. Are nevoie de un motor web al platformei — consultați
> [Instalare](02-installation-and-setup.md#1-cerințe-preliminare).

**Încărcarea unui model**

- Lista derulantă **Model** cuprinde fiecare model produs de thconfig-ul activ (fiecare `export model` al cărui
  rezultat este un `.lox`/`.3d`), plus orice astfel de fișier aflat lângă thconfig; țintele necompilate apar cu *„— necompilat
  (not built)”*.
- **Deschide…** încarcă direct orice `.lox`/`.3d`. După o compilare, cel mai nou model se încarcă automat când afișarea automată
  este activă (`.lox` are prioritate față de `.3d`).
- Un model mai vechi decât sursele sale este marcat ca **învechit** — recompilați pentru a-l reîmprospăta.

**Controale** (după modelul galeriilor publice CaveView): **orientare** (**Plan** / profil N·E·S·W),
**cameră** (**Perspectivă** / **Ortografică**) și comutatoare pentru elemente — **Pereți, Radiale, Suprafață, Stații,
Etichete, Nume, Cutie** (cutia de încadrare), **Rotire automată, Lumină, HUD** și **Meniu** (meniul lateral propriu
CaveView). **Colorează după:** **Înălțime** · **Topografie** · **Lungime** · **Înclinare** · **Simplu**. **Resetează vederea** reîncadrează modelul;
**↺ Implicite** readuce toate comutatoarele la starea inițială; **Ecran complet** trece în modul fără margini (Esc pentru ieșire).

**Funcția de bază — clic către sursă**

- **Faceți clic pe o stație** în vederea 3D pentru ca editorul să sară la locul unde stația este declarată în
  sursa `.th`.
- În sens invers, **Arată în vizualizarea 3D** — clic dreapta pe o stație (sau pe *De la*/*La* al unei vizări) în
  grila **Măsurători** ori folosiți cardul de hover / clic dreapta din editor pe o referință de stație sau de survey — pentru a o
  selecta și încadra în 3D (un survey primește o cutie de încadrare).

Pentru referința detaliată (cum sunt asociate stațiile cu sursa, cerințe, soluții de rezervă), consultați
**[docs/3d-viewer.md](../3d-viewer.md)**.

---

## Previzualizare principală

O **schiță în timp real, în plan/elevație, a liniilor centrale**, desenată direct din graful de obiecte — **fără
compilare**. Deschideți-o din **Vizualizare → Previzualizare principală**. Este cel mai rapid mod de a vedea dacă
datele au sens geometric pe măsură ce tastați. (Activați *Previzualizare principală a liniei centrale* din **Setări →
Vizualizare**.)

**Vederi**

- **Plan** (privire de sus, nordul în sus) sau un **profil proiectat** de-a lungul **N / E / S / W** ori un azimut
  **Personalizat**, ales cu un cursor (0–359°).

**Ce se afișează**

| Comutator | Efect |
|---|---|
| **Radiale** (**ca linii**) | Arată vizările spre pereți (splay) — ca linii estompate sau doar ca puncte la capătul îndepărtat. |
| **Etichete** / survey.station | Un nume la fiecare stație, opțional calificat cu survey-ul său. |
| **Stații** | Punctul de la fiecare stație. |
| **Joncțiuni** | Marcaje pe care se poate face clic, acolo unde survey-urile sunt îmbinate prin `equate`. |
| **Legendă** | Lista de vizibilitate — treceți cu mouse-ul peste un rând pentru a localiza grupul respectiv. |
| **Separă** | Distribuie porțiunile deconectate într-o grilă, în loc să le stivuiască la origine. |

**Culoare** — **Niciuna**, **Topografie**, **Fișier** sau **Componentă**. Colorarea **după Componentă** este
modul rapid de a depista porțiunile deconectate: tot ce este stivuit la origine (sau stă izolat) nu este
legat de rețeaua principală. Rândul **Grupuri** (toate / niciunul / inversează / **principal**) vă permite să afișați/ascundeți
porțiuni individuale.

În spate, previzualizarea unește stațiile legate prin `equate`, se ancorează pe `fix`-uri și tratează echivalările între fișiere și
cele calificate cu `@` — astfel reflectă felul în care peștera este efectiv îmbinată. Rămâne totuși de
**calitate de previzualizare** (fără compensarea buclelor); compilați cu Therion pentru harta compensată.

---

Următorul: [Navigator obiecte și Contur →](12-object-browser-and-outline.md)
