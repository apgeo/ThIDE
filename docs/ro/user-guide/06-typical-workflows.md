# 6. Fluxuri de lucru tipice

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Rețete care înlănțuie funcțiile așa cum lucrați de fapt. Fiecare trimite la pagina detaliată pentru
pașii implicați.

## A. Continuarea unui proiect existent

1. **Fișier → Deschide dosar…** (sau **Fișier → Deschide thconfig…**) → alegeți proiectul.
2. Dacă vi se cere sau dacă există mai multe configurații, setați **thconfig-ul activ**
   ([Concepte de bază](04-core-concepts.md#thconfig-ul-activ)).
3. Lăsați-l să se **indexeze** (bara de stare), apoi parcurgeți panoul **Diagnostice** pentru a vedea
   ce este deja stricat.
4. Editați — bazați-vă pe **redenumirea cu F2**, pe **salt la definiție** și pe **Paleta de comenzi**
   (Ctrl+Shift+P).
5. **Compilați** (**Compilare → Compilează**, F5), corectați erorile (pe care se poate face clic),
   repetați.
6. Examinați rezultatul în **vizualizatoarele de hărți / 3D**; verificați cifrele în panoul
   **Prezentare generală**.

## B. Introducerea datelor de la o ieșire nouă

**Unelte → Ieșire nouă…** face pregătirile în locul dumneavoastră: completează data, echipa (toți cei
numiți în proiect, cu participanții ultimei ieșiri bifați), unitățile, calibrarea, instrumentele și
ordinea datelor de la cea mai recentă ieșire, denumește topografia după dată și — după ce alegeți
stația din proiect de la care pornește ieșirea — scrie fișierul nou lângă celelalte ieșiri, plus
linia `input` și un schelet de `equate` (marcat `# check the tie-in`, mereu în engleză, indiferent de
limba aplicației) acolo unde proiectul le ține pe ale sale. Topografia nouă devine soră cu topografia
stației; bifați *Pune ieșirea în interiorul topografiei stației* pentru a o plasa cu un nivel mai jos.
Pentru prima ieșire a unui proiect nou (sau a unei intrări noi) bifați *Fără legătură (prima ieșire /
intrare)* și alegeți topografia în care intră: se scrie doar linia `input`. Fișierul nou se deschide
cu cursorul pe linia goală unde vine primul tronson. Liniile `input` și `equate` ajung în fișiere care
pot să nu fie deschise, așa că sunt scrise pe disc — **Editare → Anulează modificarea din mai multe
fișiere** anulează întreaga ieșire (vedeți
[anularea la nivelul spațiului de lucru](05-interface-tour.md#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere)). Apoi:

1. Deschideți fișierul `.th` al topografiei respective (sau creați un fișier nou: **Fișier → Fișier
   nou…**).
2. Tastați `centreline` / `data` / vizele. Pe măsură ce tastați:
   - Completarea automată și ajutorul la trecerea cu mouse-ul vă ghidează prin sintaxă.
   - Fila **Măsurători** afișează vizele analizate, ca să le puteți verifica din ochi.
   - Diagnosticele semnalează citirile greșite (nenumerice, în afara intervalului), punctele fixe
     duplicate, stațiile necunoscute.
3. Folosiți **Inserează data de azi** și **Inserează membru echipă** (meniul contextual al
   editorului) pentru liniile `date`/`team`.
4. Dacă doriți, cuprindeți ieșirea într-un bloc pliabil, cu nume, cu **Include în regiune**
   (Ctrl+Shift+R) — vedeți [directivele](../../directives.md).
5. Verificați că ieșirea se leagă de restul peșterii: deschideți **Previzualizare principală**,
   colorați după **Componentă**; o bucată îngrămădită în origine este deconectată — adăugați
   `equate`-ul care lipsește.

**Ieșirea a sosit ca fișier `.th` separat?** Folosiți **Unelte → Leagă un fișier de topografie…**:
alegeți o stație a ieșirii și stația din proiect din care o continuă, iar ThIDE scrie liniile `input`
și `equate` acolo unde se află celelalte ale proiectului, copiind fișierul (și schițele lui) în proiect
— vedeți [Legarea unui fișier de topografie](24-linking-surveys.md).

## C. Validare și curățenie înainte de partajare

1. Activați **Vizualizare → Mod parser strict** pentru o verificare mai severă.
2. Parcurgeți panoul **Diagnostice** de sus în jos: faceți clic pe un rând pentru a sări la el;
   folosiți **Remediere rapidă** (Ctrl+.) acolo unde este oferită; **Suprimă acest cod** pentru
   tiparele intenționate.
3. Urmăriți verificările la nivelul spațiului de lucru: topografii deconectate (TH_SEM_015), erori de
   închidere a buclelor, greșeli grosolane, includeri care nu duc nicăieri — vedeți
   [Diagnostice](09-diagnostics-and-validation.md).
4. **Formatează documentul** (Shift+Alt+F) pentru a reindenta după imbricarea blocurilor; opțional,
   activați *Formatează documentul la salvare*.
5. Folosiți fila **Prezentare generală → Audit** pentru a găsi fișierele orfane, schițele nereferite
   și hărțile neexportate.

## D. Compilarea și exportul rezultatelor finale

1. Asigurați-vă că **thconfig-ul activ** exportă ce doriți (`export map`, `export model`, …).
2. **Compilați** (thconfig-ul activ). Urmăriți **Ieșire compilator**; deschideți **Fișiere generate**
   pentru a vedea fișierele produse.
3. Acțiuni pentru fiecare rezultat în Fișiere generate: deschidere într-o aplicație externă,
   **Vizualizează în vizualizatorul 3D intern**, afișare în managerul de fișiere, salt la linia
   `export` sau setarea unei excepții de **Deschidere automată** pentru fișierul respectiv.
4. Aveți nevoie de un export ocazional care nu e în thconfig? **Compilare → Export rapid…** compune o
   configurație temporară, o compilează și deschide rezultatul. Vedeți
   [Compilare și rezultate](10-compiling-and-output.md).

## E. Desenarea / editarea schițelor de galerie

1. Creați scheletul unei schițe: **Unelte → Schiță nouă (.th2)…** (goală sau pornind de la o imagine /
   o schiță `.xvi`).
2. Editați fișierul `.th2` în editorul ThIDE sau predați-l lui **Mapiah** cu **Editează cu Mapiah** din
   antetul fișierului — modificările se reîncarcă automat când salvați acolo.
3. Verificați încrucișat cu **Navigator obiecte → Schițe / Puncte / Linii / Suprafețe** și cu panoul
   **XVI**.

## F. Importul dintr-un alt program

1. **Unelte → Importă topografie (Survex / Compass)…**, sau **Unelte → Importă DEM ca suprafață
   (.asc)…**, sau **Unelte → Importă puncte GPX (→ fix)…**.
2. ThIDE convertește în construcții Therion (un `.th`, un `surface` sau linii `fix`).
3. Legați fișierul nou în thconfig și compilați. Vedeți [Import, export și GIS](17-import-export-and-gis.md).

## G. Urmărirea pistelor de explorare

1. Marcați pistele în fișierele `.th` cum o faceți de obicei (marcaje de continuare, comentarii
   `QM`/lead/`?`, capete închise).
2. Deschideți **Prezentare generală → Piste**: ThIDE le adună într-un registru cu stare (Deschis /
   Verificat / Explorat / Mort) și le poate suprapune peste previzualizare.
3. Ciclul de viață complet și agregatorul de TODO/QM: [Piste, TODO-uri și metadate](14-leads-notes-and-metadata.md).

## H. Verificarea stării unui proiect mare, de către coordonator

1. Deschideți întregul dosar; setați thconfig-ul activ.
2. **Prezentare generală → Tablou de bord** pentru lungime/adâncime/ultima compilare dintr-o privire;
   **Statistici / Grafice** pentru detalii; **Echipă / Intrări / Calitate** pentru oameni, căi de
   acces și probleme în date.
3. **Prezentare generală → Audit** (apăsați **Calculează**) pentru lipsurile structurale.
4. **Vizualizare → Hartă relațională…** pentru a *vedea* cum se leagă topografiile, schițele și
   hărțile.
5. Dacă aplicația merge greu, ajustați **Setări → Performanță** și dezactivați previzualizările grele
   pe care nu le folosiți — [Setări](19-settings-and-preferences.md).

---

Următorul: [Editorul →](07-editing.md)