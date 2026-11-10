# 5. Un tur al interfeței

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE este un mediu de lucru **cu panouri andocabile, în stil VS**: un editor în centru, panouri de
unelte în jurul lui, o bară de meniu și o bară de instrumente sus, o bară de stare jos. Această pagină
denumește fiecare parte, ca restul ghidului să se poată referi la ea.

## Aranjamentul general

```
┌───────────────────────────────────────────────────────────────────────┐
│  Menu bar:  File  View  Edit  Search  Tools  Compile  Settings  Help    │
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
│  Status bar:  path breadcrumb · line/col · length · compile status · 🔔 │
└───────────────────────────────────────────────────────────────────────┘
```

Panourile sunt **andocabile**: trageți o filă pentru a o andoca în altă parte, desprindeți-o într-o
fereastră proprie (chiar și pe alt monitor) sau ascundeți-o. Aranjamentul dumneavoastră — inclusiv
ferestrele desprinse — **se păstrează** de la o sesiune la alta. **Vizualizare → Aspect → Restaurează
aspectul implicit** readuce totul la starea inițială. **Împarte editorul (fereastră)** deschide
documentul activ într-un editor separat, plutitor.

## Bara de meniu

| Meniu | Conține |
|---|---|
| **Fișier** | Fișier nou…, Nou din șablon…, Deschide fișier / thconfig / dosar…, Fișiere recente, Dosare recente, Redeschide fila închisă, Salvează, Salvează ca…, Salvează toate, Închide fila, Ieșire |
| **Vizualizare** | **Limbă**, **Bun venit**, câte un comutator pentru fiecare panou (Navigator obiecte, Exploratorul spațiului de lucru, Prezentare generală, Jurnal, **Asistent**, Automatizare, Previzualizare principală, Vizualizator hărți, Vizualizator 3D, Geologie structurală, Diagnostice, Contur), Hartă relațională…, **Mod parser strict**, comutatoare pentru spațiile albe și minimapă, Împarte editorul (fereastră), **Aspect** (salvarea și încărcarea aranjamentului, restaurarea aranjamentului implicit, împărțirea panourilor laterale) |
| **Editare** | Anulează / Refă, **Anulează modificarea din mai multe fișiere** / Refă (anularea [la nivelul spațiului de lucru](#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere)), Decupează / Copiază / Lipește, Lipește ca date de vize…, Șterge, Selectează tot, MAJUSCULE / minuscule, Comutare comentariu, Include în regiune, Pliază tot / Depliază tot, Adaugă semn de carte…, Salt la definiție, Vezi definiția, Găsește referințele, Redenumește simbolul… |
| **Căutare** | Caută, Înlocuiește, Caută în fișiere…, Înlocuiește în fișiere…, Salt la linie…, Semne de carte… |
| **Unelte** | Importă topografie / DEM / GPX, Generează proiect…, **Leagă un fișier de topografie…**, **Ieșire nouă…**, Urmărește dosarul de intrare, Adaugă scrap-urile nereferite în hărți…, Sugerează echivalări…, Formatează toate fișierele…, Normalizează proiectul…, Renumerotează stațiile…, Macrouri, Sarcini, Exportă intrări / puncte fixe, Exportă tabel de date, Generează raport topografic (HTML)…, Schiță nouă (.th2)…, **Animație Blender…**, Calculatoare |
| **Compilare** | Compilează, Compilează acest thconfig (fila activă), Recompilează, Oprește compilarea, Compilează mai multe…, Compilează ținta, Export rapid…, Deschide în Loch / Aven, Unelte Survex, Informații Therion, Deschide ultimul dosar de ieșire |
| **Setări** | Preferințe… (deschide fereastra [Setări](19-settings-and-preferences.md), Ctrl+,) și Scurtături tastatură… (aceeași fereastră, la lista de scurtături) |
| **Ajutor** | Ghid de utilizare, Cartea Therion, Informații de depanare, Despre ThIDE |

Meniul afișează lângă fiecare comandă scurtătura ei curentă; o comandă fără scurtătură nu afișează
nimic.

## Bara de instrumente

Un singur rând de acțiuni rapide, cu indicații (tooltip-uri) — un tooltip arată o singură dată
scurtătura curentă a comenzii și nimic pentru o comandă fără scurtătură: deschidere
fișier/thconfig/dosar, navigare **Înapoi/Înainte**, Salvează, Decupează/Copiază/Lipește, **Caută în
fișiere**, **Redenumește simbolul**, **Salt la fișier** și **Paletă de comenzi**, salt la simbol
(spațiu de lucru/document), import/export, generare raport, schiță nouă, Deschide în Loch/Aven,
încadrare text, ecran complet, aranjament, **Compilează** și clopoțelul de **notificări**.

## Panourile (vizualizări de unelte)

Majoritatea panourilor de mai jos pot fi afișate sau ascunse din meniul **Vizualizare** și din Paleta
de comenzi. Excepțiile: **Ieșire compilator**, **Fișiere generate** și **XVI** nu au o intrare în
meniul Vizualizare — sunt file în benzile lor laterale — iar **Animație Blender** se deschide din
meniul **Unelte**. Fiecare are propria pagină în ghid:

| Panou | La ce folosește | Ghid |
|---|---|---|
| **Exploratorul spațiului de lucru** | Fișierele dumneavoastră, ca arbore imbricat după source/input | [mai jos](#exploratorul-spațiului-de-lucru) |
| **Contur** | Structura de topografii/schițe a fișierului curent | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Navigator obiecte** | Tabele cu stații, vize, puncte fixe, echivalări, hărți, schițe, puncte, linii, suprafețe | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Diagnostice** | Erori / avertismente / informații, cu filtre | [Diagnostice și validare](09-diagnostics-and-validation.md) |
| **Ieșire compilator** | Ieșirea Therion în timp real (Ieșire / Brut) | [Compilare și rezultate](10-compiling-and-output.md) |
| **Fișiere generate** | Tot ce a produs ultima compilare, cu acțiuni pentru fiecare fișier | [Compilare și rezultate](10-compiling-and-output.md) |
| **Prezentare generală** | Tablou de bord, topografii, statistici, grafice, echipă, intrări, calitate, audit, piste, TODO-uri, metadate, media | [Prezentarea generală a topografiei și analize](13-survey-overview-and-analytics.md) |
| **Previzualizare principală** | Schiță a poligonalei în plan/profil, fără compilare | [Vizualizatoarele](11-viewers.md#previzualizarea-principală) |
| **Vizualizator hărți** | Vizualizator de hărți PNG/SVG/PDF în aplicație | [Vizualizatoarele](11-viewers.md#vizualizatorul-de-hărți) |
| **Vizualizator 3D** | Model 3D încorporat (CaveView.js) | [Vizualizatoarele](11-viewers.md#vizualizatorul-3d) |
| **Geologie structurală** | Calculator de direcție/înclinare a planelor + grafic 3D | [Geologie structurală](15-structural-geology.md) |
| **Jurnal** | Jurnalul intern al aplicației (indexare, lucru în fundal, erori) | — |
| **XVI** | Referințele la schițele (.xvi) și grilele lor | [Navigator obiecte și Contur](12-object-browser-and-outline.md) |
| **Asistent** *(experimental)* | Conversație cu un model AI **local** despre proiectul dumneavoastră | [Extensibilitate](20-extensibility.md#asistenți-ai-mcp) |
| **Animație Blender** *(experimental)* | Randează un videoclip / imagini statice ale peșterii prin Blender | [Randări de animații Blender](23-blender-animation.md) |

Unele panouri sunt **dezactivate implicit**, ca proiectele mari să rămână ușoare (Vizualizator hărți,
Vizualizator 3D, Geologie structurală, unele analize) — activați-le din pagina **Vizualizare** a
setărilor (**Setări → Vizualizare**). Vedeți [Setări](19-settings-and-preferences.md).

### Exploratorul spațiului de lucru

Arborele de fișiere din stânga. Lista derulantă de deasupra lui alege **thconfig-ul activ**. Clic
dreapta pe un fișier sau dosar oferă: **Deschide**, **Afișează în Explorator fișiere**, **Setează ca
thconfig activ al spațiului de lucru**, **Fișier nou… / Dosar nou**, **Șterge** (cu numele dat de
sistemul dumneavoastră: *Mută în Coșul de reciclare* pe Windows, *Mută în coșul de gunoi* pe Linux și macOS,
*Șterge definitiv* acolo unde nu există coș de gunoi), **Copiază calea completă / relativă**,
**Reîmprospătează** și (pe Windows) meniul nativ al sistemului. Arborele reflectă structura
`source`/`input`, așa că vedeți cum se includ fișierele unele pe altele.

## Zona editorului

Documentele se deschid în **file**. Un fișier `.th`/`.th2` are subfilele **Sursă** și **Măsurători**
(grila Măsurători listează vizele/stațiile acelui fișier). Meniul de clic dreapta al filei: **Fixează
fila**, **Închide**, **Închide celelalte / Închide filele din dreapta / Închide toate**, **Desprinde**,
**Copiază numele fișierului**. Ați închis o filă din greșeală? **Fișier → Redeschide fila închisă**.
Tot ce ține de editare se află în [Editorul](07-editing.md).

Notificările despre un fișier sau despre sesiune apar într-un banner deasupra editorului, nu într-o
notificare flotantă care poate ieși din cadrul unui ecran mic: un fișier modificat pe disc de alt
program, un fișier care depășește o limită de dimensiune și — după o închidere neașteptată —
**bannerul de recuperare**, care numește fișierele ale căror modificări nesalvate au fost păstrate și
oferă **Restaurează** sau **Șterge copiile** (vedeți [Depanare](22-troubleshooting-and-faq.md#fiabilitate-și-siguranță-ce-faceți-când-ceva-nu-merge)).

### Anularea unei unelte care a scris în mai multe fișiere

Redenumește simbolul, Înlocuiește în fișiere, **Ieșire nouă…**, **Leagă un fișier de topografie…**,
**Renumerotează stațiile…**, **Formatează toate fișierele…**, **Normalizează proiectul…** și *Corectează
toate* modifică mai multe fișiere deodată. Fișierele care **nu sunt deschise** sunt scrise imediat pe
disc — un fișier închis nu are istoric în editor — iar un fișier deschis cu modificări nesalvate
primește schimbarea în editorul său, așa că rămâne nesalvat. Fiecare astfel de rulare este o singură
intrare în **anularea la nivelul spațiului de lucru**: **Editare → Anulează modificarea din mai multe
fișiere** (după ce există o operație, elementul de meniu o numește, de exemplu *Anulează: Redenumire*)
sau butonul **Anulează** din notificare readuce toate fișierele într-un singur pas, pe disc și în
editoare, iar **Refă** aplică din nou modificările. Dacă unul dintre fișiere s-a schimbat între timp,
nu se restaurează nimic, iar mesajul îl numește.

## Bara de stare

De-a lungul marginii de jos: **calea** fișierului activ (breadcrumb), **Linie/Col** a cursorului și
selecția, **Lungime/Linii** ale documentului, un indicator **Indexare…** / **Se compilează…**,
rezultatul ultimei compilări și o scurtătură către panoul Diagnostice (cu F8 / Shift+F8 pentru a trece
de la o problemă la alta).

## Paleta de comenzi și notificările

- **Paletă de comenzi** (**Ctrl+Shift+P**) — cea mai rapidă cale către *orice*. Tastați numele unei
  comenzi sau folosiți prefixele `@` (simbol din acest document), `#` (simbol din spațiul de lucru),
  `:42` (salt la linie). Vedeți [Navigare și căutare](08-navigation-and-search.md).
- **Notificări** — notificările care nu blochează lucrul (compilare terminată, import eșuat…) se
  adună sub **clopoțelul** din bara de instrumente, ca să le puteți revedea mai târziu.

## Teme și limbă

- **Setări → Temă & culori**: temele Implicit sistem / Luminoasă / **Întunecată**; opțional,
  **culori de sintaxă personalizate**.
- **Vizualizare → Limbă** (sau **Setări → General**): **English** și **Română**. Meniurile și
  panourile se schimbă imediat (din Setări, când apăsați OK); câteva etichete din listele deja
  deschise se actualizează abia după o repornire.

---

Următorul: [Fluxuri de lucru tipice →](06-typical-workflows.md)