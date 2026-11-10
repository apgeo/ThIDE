# 3. Primii pași: prima sesiune

> [← Înapoi la pagina principală a ghidului](README.md)

Aceasta este o primă rulare ghidată. Presupune că aveți deja un proiect Therion (un dosar cu fișiere `.th`
și cel puțin un `.thconfig`). Dacă nu aveți, luați orice proiect Therion de exemplu ca să puteți urmări
pașii.

## Pasul 1 — Deschideți proiectul

Puteți deschide la trei niveluri de cuprindere, toate din meniul **Fișier** (sau din bara de instrumente /
pagina Bun venit):

| Comandă | Folosiți-o când… | Rezultat |
|---|---|---|
| **Deschide dosar…** | Doriți întregul proiect | Încarcă dosarul ca **spațiu de lucru** și indexează fiecare fișier Therion din el. |
| **Deschide thconfig…** | Știți care este fișierul de configurare | Îl deschide *și* adoptă dosarul lui ca spațiu de lucru. |
| **Deschide fișier…** | Doriți doar să vedeți sau să editați un fișier | Deschide un singur fișier; un spațiu de lucru poate fi totuși dedus din el. |

Pentru lucrul propriu-zis, **Deschide dosar** (sau Deschide thconfig) este ce vă trebuie — asta îi oferă
lui ThIDE înțelegerea peste granițele fișierelor descrisă în [Concepte de bază](04-core-concepts.md).

**Porniți de la zero?** **Fișier → Nou din șablon…** (disponibil și pe pagina Bun venit) creează un dosar
nou de topografie pornind de la un șablon (ThIDE nu are un fișier de proiect propriu: dosarul și
thconfig-ul lui sunt proiectul). Alegeți *O singură peșteră* (fișier de topografie, thconfig cu o hartă în
plan și un model 3D, `data/`, `sketches/`, `output/`), *Zonă carstică* (câte un thconfig pentru fiecare
peșteră, plus unul principal) sau *Expediție* (câte un dosar și un thconfig pentru fiecare an). Completați
cele câteva câmpuri (numele pot conține litere, cifre, `_` și `-`), alegeți un dosar, iar ThIDE scrie
fișierele și deschide thconfig-ul. **Importă un dosar de șablon…** / **Importă un .zip de șablon…** adaugă
un șablon propriu al clubului: un dosar cu un `template.json` (`id`, `title`, `description`, `variables` —
fiecare `{ "id", "prompt", "default", "type": "text|name|number|bool|choice|cs|date" }` — `directories`,
`openAfterCreate`) și fișierele de scris, ale căror căi și conținut folosesc `${id}`. Aceleași șabloane
funcționează și din linia de comandă: `therion-cli new --list`, `therion-cli new single-cave --out ~/caves/pit
--var project=pit`.

## Pasul 2 — Alegeți thconfig-ul activ

Un proiect poate conține mai multe fișiere `.thconfig`. ThIDE compilează și analizează pe baza **unui
singur thconfig activ** la un moment dat. Dacă spațiul de lucru are exact unul, acesta este ales automat.
Altfel, setați-l:

- În **Exploratorul spațiului de lucru**, alegeți-l din lista derulantă din partea de sus a panoului (ea
  arată thconfig-ul activ; țineți mouse-ul deasupra ei pentru calea completă) sau faceți clic dreapta pe un
  `.thconfig` și alegeți **Setează ca thconfig activ al spațiului de lucru**, ori
- deschideți fișierul și folosiți acțiunea din antetul lui, **Setează acest fișier ca thconfig activ**.

Thconfig-ul activ pilotează întreaga aplicație: ce se compilează, care diagnostice sunt „la nivelul
spațiului de lucru”, ce rezultate oferă vizualizatoarele.

## Pasul 3 — Uitați-vă în jur

Lăsați-i lui ThIDE un moment pentru indexare (urmăriți indicația **Indexare…** din bara de stare). Apoi:

- **Exploratorul spațiului de lucru** (în stânga) vă arată fișierele ca arbore imbricat de surse și
  fișiere incluse.
- Panoul **Diagnostice** listează problemele pe care le-a găsit deja.
- Panoul **Prezentare generală** rezumă peștera (lungime, adâncime, fișiere, ultima compilare).
- Deschideți un fișier `.th` și veți avea colorare sintactică, precum și o filă **Măsurători** lângă
  sursă.

O hartă completă a interfeței găsiți în [Un tur al interfeței](05-interface-tour.md).

## Pasul 4 — Editați cu încredere

Încercați următoarele ca să simțiți „înțelegerea” editorului (detalii în [Editorul](07-editing.md) și
[Navigare și căutare](08-navigation-and-search.md)):

- Țineți mouse-ul deasupra numelui unei stații sau al unei topografii pentru un **card informativ**;
  faceți clic pe **Salt la definiție** (sau Ctrl+clic).
- Apăsați **F2** pe o stație pentru a o **redenumi peste tot** unde este referită (cu o previzualizare
  opțională).
- Apăsați **Ctrl+Shift+P** pentru **Paletă de comenzi** — tastați pentru a rula orice comandă sau puneți
  în față prefixul `@` (simbol din acest fișier), `#` (simbol din spațiul de lucru) sau `:42` (salt la
  linia 42).

Pe măsură ce tastați, ThIDE reverifică fișierul și semnalează problemele prin sublinieri ondulate colorate
și în panoul Diagnostice — vezi [Diagnostice și validare](09-diagnostics-and-validation.md).

## Pasul 5 — Compilați

Rulați **Compilează** (meniul **Compilare → Compilează**, **F5**, ciocanul din bara de instrumente sau
paleta). Se compilează întotdeauna thconfig-ul activ, indiferent ce filă are focusul. ThIDE:

1. Se oferă să salveze fișierele nesalvate care fac parte din compilare.
2. Rulează Therion și transmite în timp real tot ce afișează acesta în panoul **Ieșire compilator**.
3. Face din fiecare mesaj `file:line` unul pe care se poate face clic, ca să puteți sări la cauză.
4. Listează tot ce a produs Therion în panoul **Fișiere generate**.

Dacă compilarea eșuează, erorile sunt chiar acolo și se poate face clic pe ele. Detalii complete:
[Compilare și rezultate](10-compiling-and-output.md).

## Pasul 6 — Priviți rezultatul

În funcție de ce exportă thconfig-ul dumneavoastră:

- **Hărțile 2D** (PDF/SVG/PNG) se deschid în [Vizualizator hărți](11-viewers.md#vizualizatorul-de-hărți).
- **Modelele 3D** (`.lox` / `.3d`) se deschid în [vizualizatorul 3D încorporat](11-viewers.md#vizualizatorul-3d) — unde
  puteți face clic pe o stație pentru a sări înapoi la sursa ei `.th` — sau extern, în Loch/Aven.
- Nici măcar nu trebuie să compilați ca să aruncați o privire asupra poligonalelor:
  [Previzualizare principală](11-viewers.md#previzualizarea-principală) le desenează direct din model.

## Pasul 7 — Înțelegeți-vă topografia

Deschideți panoul **Prezentare generală** și parcurgeți filele **Tablou de bord / Topografii / Statistici /
Grafice / Echipă / Intrări / Calitate / Audit** pentru o imagine la zi a peșterii. Vezi
[Prezentarea generală a topografiei și analize](13-survey-overview-and-analytics.md). Țineți evidența
continuărilor cu [registrul de piste](14-leads-notes-and-metadata.md).

## Unde mergeți mai departe

- Învățați vocabularul cum se cuvine: [Concepte de bază și vocabular](04-core-concepts.md).
- Vedeți întreaga interfață: [Un tur al interfeței](05-interface-tour.md).
- Urmați rețete complete, de la cap la coadă: [Fluxuri de lucru tipice](06-typical-workflows.md).

---

Următoarea: [Concepte de bază și vocabular →](04-core-concepts.md)
