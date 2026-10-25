# 5. Un tur al interfeței

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE este un mediu de lucru **cu panouri andocabile, în stil VS**: un editor în centru, panouri de
unelte în jurul lui, o bară de meniu și o bară de unelte sus, o bară de stare jos. Această pagină
denumește fiecare parte, ca restul ghidului să se poată referi la ea.

## Aranjamentul general

```
┌───────────────────────────────────────────────────────────────────────┐
│  Menu bar:  File  Edit  Search  View  Compile  Tools  Settings  Help    │
│  Toolbar:   open · nav · save · edit · find · go-to · import/export · … │
├───────────────┬───────────────────────────────────────┬───────────────┤
│  Left rail    │            Editor (document tabs)       │  Right rail   │
│  Workspace    │   ┌───────────────────────────────┐     │  Object       │
│  Explorer     │   │  Source  |  Measurements       │     │  Browser      │
│  Outline      │   │                                │     │  3D Viewer    │
│               │   └───────────────────────────────┘     │  Map Viewer   │
├───────────────┴───────────────────────────────────────┴───────────────┤
│  Bottom rail:  Diagnostics · Compiler Output · Generated Files · Log     │
├───────────────────────────────────────────────────────────────────────┤
│  Status bar:  path breadcrumb · line/col · length · build status · 🔔   │
└───────────────────────────────────────────────────────────────────────┘
```

Panourile sunt **andocabile**: trageți o filă pentru a o reandoca, desprindeți-o într-o fereastră
proprie (chiar și pe alt monitor) sau ascundeți-o. Aranjamentul dumneavoastră — inclusiv ferestrele
desprinse — **se păstrează** între sesiuni. **Vizualizare → Resetează la aranjamentul implicit**
readuce totul la starea inițială. **Împarte editorul (fereastră)** deschide documentul activ într-un
editor separat, plutitor.

## Bara de meniu

| Meniu | Conține |
|---|---|
| **Fișier** | Deschide fișier / thconfig / dosar, Fișiere recente, Dosare recente, Redeschide fila închisă, Ieșire |
| **Editare** | Decupează/Copiază/Lipește, comutare majuscule/minuscule, Comutare comentariu, Include în regiune, Pliază tot/Depliază tot, Adaugă semn de carte |
| **Căutare** | Caută, Înlocuiește, Caută/Înlocuiește în fișiere, Salt la linie, Semne de carte |
| **Vizualizare** | Afișarea/ascunderea fiecărui panou (Navigator obiecte, Spațiu de lucru, Diagnostice, Prezentare generală, Jurnal, Previzualizare principală, Vizualizator hărți, Vizualizator 3D, Geologie structurală, Contur, **Asistent**), Hartă relațională, comutatoare pentru spații albe/minimapă, Împarte editorul, **Limbă**, **Mod parser strict** |
| **Compilare** | Compilează / Recompilează / Anulează, Compilează ținta, Export rapid, Unelte Survex, Informații Therion, Deschide în Loch/Aven, Deschide ultimul dosar de ieșire |
| **Unelte** | Import (topografie/DEM/GPX), Export (intrări/tabel), Generează raport, Schiță nouă, Calculatoare, **Animație Blender** |
| **Setări** | Deschide fereastra [Setări](19-settings-and-preferences.md) |
| **Ajutor** | Cartea Therion, Despre ThIDE, Informații de depanare, Bun venit |

## Bara de unelte

Un singur rând de acțiuni rapide cu tooltip-uri (fiecare își indică scurtătura): deschidere
fișier/thconfig/dosar, navigare **Înapoi/Înainte**, Salvează, Decupează/Copiază/Lipește, **Caută în
fișiere**, **Redenumește simbolul**, **Salt la fișier** și **Paletă de comenzi**, salt la simbol
(spațiu de lucru/document), import/export, generare raport, schiță nouă, Deschide în Loch/Aven,
încadrare text, ecran complet, aranjament, **Compilează** și clopoțelul de **notificări**.

## Panourile (vizualizări de unelte)

Fiecare panou de mai jos poate fi afișat sau ascuns din meniul **Vizualizare** și din Paleta de
comenzi (singura excepție este **Animație Blender**, care se deschide din meniul **Unelte**). Fiecare
are propria pagină în ghid:

| Panou | La ce folosește | Ghid |
|---|---|---|
| **Explorator proiect** | Fișierele dumneavoastră ca arbore imbricat de source/input | [mai jos](#exploratorul-de-proiect) |
| **Contur** | Structura de survey-uri/scrap-uri a fișierului curent | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Navigator obiecte** | Tabele cu stații, vizări, fix-uri, equate-uri, hărți, scrap-uri, puncte, linii, suprafețe | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Diagnostice** | Erori / avertismente / informații, cu filtre | [Diagnostice și validare](09-diagnostics-and-validation.md) |
| **Ieșire compilator** | Ieșirea Therion în timp real (Ieșire / Brut) | [Compilare și rezultate](10-compiling-and-output.md) |
| **Fișiere generate** | Tot ce a produs ultima compilare, cu acțiuni pentru fiecare fișier | [Compilare și rezultate](10-compiling-and-output.md) |
| **Prezentare generală** | Tablou de bord, survey-uri, statistici, grafice, echipă, intrări, calitate, audit, piste, sarcini TODO, metadate, media | [Prezentarea generală a ridicării și analize](13-survey-overview-and-analytics.md) |
| **Previzualizare principală** | Schiță în plan/elevație a liniei centrale, fără compilare | [Vizualizatoarele](11-viewers.md#previzualizare-principală) |
| **Vizualizator hărți** | Vizualizator de hărți PNG/SVG/PDF în aplicație | [Vizualizatoarele](11-viewers.md#vizualizator-hărți) |
| **Vizualizator 3D** | Model 3D încorporat (CaveView.js) | [Vizualizatoarele](11-viewers.md#vizualizator-3d) |
| **Geologie structurală** | Calculator de direcție/înclinare a planelor + grafic 3D | [Geologie structurală](15-structural-geology.md) |
| **Jurnal** | Jurnalul intern al aplicației (indexare, lucru în fundal, erori) | — |
| **XVI** | Referințele la schițe (.xvi) și grilele lor | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Asistent** *(experimental)* | Conversație cu un model AI **local** despre proiectul dumneavoastră | [Extensibilitate](20-extensibility.md#asistenți-ai-mcp) |
| **Animație Blender** *(experimental)* | Randează un videoclip / imagini statice ale peșterii prin Blender | [Randări de animații Blender](23-blender-animation.md) |

Unele panouri sunt **dezactivate implicit**, ca proiectele mari să rămână ușoare (Vizualizator hărți,
Vizualizator 3D, Geologie structurală, unele analize) — activați-le din **Setări → Vizualizare /
Analiză topografică**. Vedeți [Setări](19-settings-and-preferences.md).

### Exploratorul de proiect

Arborele de fișiere din stânga. Clic dreapta pe un fișier sau dosar pentru: **Deschide**, **Afișează
în Explorator fișiere**, **Setează ca thconfig activ al proiectului**, **Fișier nou / Folder nou**,
**Șterge**, **Copiază calea completă/relativă**, **Reîmprospătează** și (pe Windows) meniul nativ al
sistemului. Arborele reflectă structura `source`/`input`, astfel încât puteți vedea cum se includ
fișierele unele pe altele.

## Zona editorului

Documentele se deschid ca **file**. Un fișier `.th`/`.th2` are subfilele **Sursă** și **Măsurători**
(grila Măsurători listează vizările/stațiile fișierului). Meniul contextual al filei: **Fixează
fila**, **Închide**, **Închide celelalte / Închide filele din dreapta / Închide toate**, **Desprinde**,
**Copiază numele fișierului**. Ați închis o filă din greșeală? **Fișier → Redeschide fila închisă**.
Tot ce ține de editare se află în [Editorul](07-editing.md).

## Bara de stare

De-a lungul marginii de jos: o **cale de navigare** (breadcrumb) a fișierului activ, **Linie/Col** a
cursorului și selecția, **Lungime/Linii** ale documentului, un indicator **Indexare…** / **Compilare
în curs…**, rezultatul ultimei compilări și o scurtătură către panoul Diagnostice (cu F8 / Shift+F8
pentru a trece de la o problemă la alta).

## Paleta de comenzi și notificările

- **Paletă de comenzi** (**Ctrl+Shift+P**) — cea mai rapidă cale către *orice*. Tastați numele unei
  comenzi sau folosiți prefixele `@` (simbol în acest document), `#` (simbol în spațiul de lucru),
  `:42` (salt la linie). Vedeți [Navigare și căutare](08-navigation-and-search.md).
- **Notificări** — mesaje temporare neblocante (compilare terminată, import eșuat, modificări
  nesalvate recuperate…) se adună sub **clopoțelul** din bara de unelte, ca să le puteți revedea mai
  târziu.

## Teme și limbă

- **Setări → Temă & culori**: temele Implicit sistem / Luminoasă / **Întunecată**; opțional
  **culori de sintaxă personalizate**.
- **Vizualizare → Limbă** (sau Setări → General): **English** și **Română**, aplicate imediat.
  Acest ghid este disponibil și el în ambele limbi.

## Cititoare de ecran

Butoanele și câmpurile au nume accesibile în limba aleasă, astfel încât un cititor de ecran anunță un
buton de bară de unelte care are doar o pictogramă prin ceea ce face (*Compilează*, *Resetează
vederea*), nu ca „PathIcon”. Rândurile din liste și grile nu au încă nume individuale.

---

Următorul: [Fluxuri de lucru tipice →](06-typical-workflows.md)
