# 3. Primii pași: prima sesiune

> [← Înapoi la pagina principală a ghidului](README.md)

Aceasta este o primă rulare ghidată. Presupune că aveți deja un proiect Therion (un folder cu fișiere `.th`
și cel puțin un `.thconfig`). Dacă nu aveți, luați orice proiect Therion de exemplu ca să puteți urmări pașii.

## Pasul 1 — Deschideți proiectul

Puteți deschide la trei niveluri de cuprindere, toate din meniul **Fișier** (sau din bara de unelte / pagina
de Bun venit):

| Comandă | Folosiți-o când… | Rezultat |
|---|---|---|
| **Deschide dosar…** | Doriți întregul proiect | Încarcă folderul ca **spațiu de lucru** și indexează fiecare fișier Therion din el. |
| **Deschide thconfig…** | Știți care este fișierul de configurare | Îl deschide *și* adoptă folderul său ca spațiu de lucru. |
| **Deschide fișier…** | Doriți doar să vedeți/editați un fișier | Deschide un singur fișier; un spațiu de lucru poate fi totuși dedus din el. |

Puteți de asemenea să trageți fișiere pe fereastră, să alegeți **Deschide cu → ThIDE** (Open with) în
managerul de fișiere sau să le numiți în linia de comandă (`ThIDE /path/to/cave.thconfig`).

Pentru lucru real, **Deschide dosar** (sau Deschide thconfig) este ce vă trebuie — asta îi oferă lui ThIDE
înțelegerea între fișiere descrisă în [Concepte de bază](04-core-concepts.md).

## Pasul 2 — Alegeți thconfig-ul activ

Un proiect poate conține mai multe fișiere `.thconfig`. ThIDE compilează și analizează față de **un singur
thconfig activ** la un moment dat. Dacă spațiul de lucru are exact unul, acesta este ales automat. Altfel,
setați-l:

- În **Explorator proiect**, faceți clic dreapta pe un `.thconfig` → **Setează ca thconfig activ al proiectului**, sau
- Deschideți fișierul și folosiți acțiunea din antetul său **Setează acest fișier ca thconfig activ**.

Thconfig-ul activ pilotează întreaga aplicație: ce se compilează, care diagnostice sunt „la nivelul
spațiului de lucru”, ce rezultate oferă vizualizatoarele.

## Pasul 3 — Uitați-vă în jur

Lăsați-i lui ThIDE un moment pentru indexare (urmăriți indicația **Indexare…** din bara de stare). Apoi:

- **Explorator proiect** (stânga) vă arată fișierele ca arbore imbricat de surse/input-uri.
- Panoul **Diagnostice** listează problemele deja găsite.
- Panoul **Prezentare generală** rezumă peștera (lungime, adâncime, fișiere, ultima compilare).
- Deschideți un fișier `.th` și veți avea colorare sintactică și o filă **Măsurători** lângă sursă.

O hartă completă a interfeței găsiți în [Un tur al interfeței](05-interface-tour.md).

## Pasul 4 — Editați cu încredere

Încercați următoarele ca să simțiți „înțelegerea” editorului (detalii în [Editorul](07-editing.md) și
[Navigare și căutare](08-navigation-and-search.md)):

- Treceți cu mouse-ul peste numele unei stații sau al unui survey pentru un **card informativ** (hover card);
  faceți clic pe **Salt la definiție** (sau Ctrl+clic).
- Apăsați **F2** pe o stație pentru a o **redenumi peste tot** unde este referită (cu previzualizare opțională).
- Apăsați **Ctrl+Shift+P** pentru **Paletă de comenzi** — tastați pentru a rula orice comandă sau folosiți
  prefixul `@` (simbol în acest fișier), `#` (simbol în spațiul de lucru) sau `:42` (salt la linia 42).

Pe măsură ce tastați, ThIDE reverifică fișierul și semnalează problemele prin sublinieri ondulate colorate și
în panoul Diagnostice — vezi [Diagnostice și validare](09-diagnostics-and-validation.md).

## Pasul 5 — Compilați

Rulați **Compilează** (meniul **Compilare → Compilează**, ciocanul din bara de unelte sau paleta). ThIDE:

1. Se oferă să salveze fișierele nesalvate care fac parte din compilare.
2. Rulează Therion, transmițându-i ieșirea în panoul **Ieșire compilator**.
3. Face fiecare mesaj `file:line` clicabil, ca să puteți sări la cauză.
4. Listează tot ce a produs Therion în panoul **Fișiere generate**.

Dacă compilarea eșuează, erorile sunt chiar acolo și se poate face clic pe ele. Detalii complete:
[Compilare și rezultate](10-compiling-and-output.md).

## Pasul 6 — Priviți rezultatul

În funcție de ce exportă thconfig-ul dumneavoastră:

- **Hărțile 2D** (PDF/SVG/PNG) se deschid în [Vizualizator hărți](11-viewers.md#vizualizator-hărți).
- **Modelele 3D** (`.lox` / `.3d`) se deschid în [vizualizatorul 3D încorporat](11-viewers.md#vizualizator-3d) — unde
  puteți face clic pe o stație pentru a sări înapoi la sursa ei `.th` — sau extern în Loch/Aven.
- Nici măcar nu trebuie să compilați ca să aruncați o privire liniilor centrale:
  [Previzualizare principală](11-viewers.md#previzualizare-principală) le desenează direct din model.

## Pasul 7 — Înțelegeți-vă topografia

Deschideți panoul **Prezentare generală** și parcurgeți filele **Tablou de bord / Topografii / Statistici /
Grafice / Echipă / Intrări / Calitate / Audit** pentru o citire în timp real a peșterii. Vezi
[Prezentare generală a topografiei și analize](13-survey-overview-and-analytics.md). Urmăriți continuările cu
[registrul de piste](14-leads-notes-and-metadata.md).

## Unde mergeți mai departe

- Învățați vocabularul cum se cuvine: [Concepte de bază și vocabular](04-core-concepts.md).
- Vedeți întreaga interfață: [Un tur al interfeței](05-interface-tour.md).
- Urmați rețete cap-coadă: [Fluxuri de lucru tipice](06-typical-workflows.md).

---

Următoarea: [Concepte de bază și vocabular →](04-core-concepts.md)
