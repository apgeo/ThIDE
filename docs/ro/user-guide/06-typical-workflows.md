# 6. Fluxuri de lucru tipice

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Rețete care înlănțuie funcțiile așa cum lucrați de fapt. Fiecare trimite la pagina detaliată pentru
pașii implicați.

## A. Continuarea unui proiect existent

1. **Fișier → Deschide dosar...** (sau Deschide thconfig) → alegeți proiectul.
2. Dacă vi se cere sau dacă există mai multe configurări, setați **thconfig-ul activ**
   ([Concepte de bază](04-core-concepts.md#thconfig-ul-activ)).
3. Lăsați-l să se **indexeze** (bara de stare), apoi parcurgeți panoul **Diagnostice** pentru orice
   este deja stricat.
4. Editați — bazați-vă pe **redenumirea cu F2**, **salt la definiție** și **Paletă de comenzi**
   (Ctrl+Shift+P).
5. **Compilați** (Compilare → Compilează), corectați erorile (pe care se poate face clic),
   repetați.
6. Examinați rezultatul în **vizualizatoarele de hărți / 3D**; verificați valorile în panoul
   **Prezentare generală**.

## B. Introducerea datelor dintr-o tură nouă

1. Deschideți fișierul `.th` al survey-ului respectiv (sau creați un fișier nou: clic dreapta în
   **Explorator proiect** → **Fișier nou**).
2. Tastați `centreline` / `data` / vizările. Pe măsură ce tastați:
   - Completarea automată și ajutorul la hover vă ghidează prin sintaxă.
   - Fila **Măsurători** afișează vizările analizate, ca să le puteți verifica din ochi.
   - Diagnosticele semnalează citirile greșite (nenumerice, în afara intervalului), fix-urile
     duplicate, stațiile necunoscute.
3. Folosiți **Inserează data de azi** și **Inserează membru echipă** (meniul contextual al
   editorului) pentru liniile `date`/`team`.
4. Dacă doriți, includeți tura într-un bloc pliabil cu nume, cu **Include în regiune**
   (Ctrl+Shift+R) — vedeți [directivele](../directives.md).
5. Verificați că tura se leagă de restul peșterii: deschideți **Previzualizare principală**,
   colorați **după Componentă**; o porțiune îngrămădită în origine este deconectată — adăugați
   `equate`-ul care lipsește.

## C. Validare și curățare înainte de partajare

1. Activați **Vizualizare → Mod parser strict** pentru o trecere mai strictă.
2. Parcurgeți panoul **Diagnostice** de sus în jos: clic pe un rând pentru a sări la el; folosiți
   **Remediere rapidă** (Ctrl+.) acolo unde este oferită; **Suprimă acest cod** pentru tiparele
   intenționate.
3. Urmăriți verificările la nivelul spațiului de lucru: survey-uri deconectate (TH_SEM_015), erori de
   închidere a buclelor, greșeli grosolane, includeri fără țintă — vedeți
   [Diagnostice](09-diagnostics-and-validation.md).
4. **Formatează documentul** (Shift+Alt+F) pentru a reindenta după imbricarea blocurilor; opțional
   activați *Formatează documentul la salvare*.
5. Folosiți fila **Prezentare generală → Audit** pentru a găsi fișiere orfane, scrap-uri
   nereferențiate și hărți neexportate.

## D. Compilarea și exportul rezultatelor

1. Asigurați-vă că **thconfig-ul activ** exportă ceea ce doriți (`export map`, `export model`, …).
2. **Compilați**. Urmăriți **Ieșire compilator**; deschideți **Fișiere generate** pentru a vedea
   fișierele produse.
3. Acțiuni pentru fiecare rezultat în Fișiere generate: deschidere externă, **Vizualizează în
   vizualizatorul 3D intern**, afișare în managerul de fișiere, salt la linia `export` sau setarea
   unei excepții **Deschidere automată** pentru fișierul respectiv.
4. Aveți nevoie de un rezultat ocazional care nu e în thconfig? **Compilare → Export rapid…**
   compune o configurare temporară, o compilează și deschide rezultatul. Vedeți
   [Compilare și rezultate](10-compiling-and-output.md).

## E. Desenarea / editarea galeriilor

1. Creați scheletul unui scrap: **Unelte → Schiță nouă (.th2)…** (gol, sau dintr-o imagine / schiță
   `.xvi`).
2. Editați fișierul `.th2` în editorul ThIDE sau predați-l către **Mapiah** cu **Editează cu
   Mapiah** din antetul fișierului — modificările se reîncarcă automat când salvați acolo.
3. Verificați încrucișat cu **Navigator obiecte → Schițe / Puncte / Linii / Suprafețe** și cu panoul
   **XVI**.

## F. Importul dintr-un alt program

1. **Unelte → Importă topografie (Survex / Compass)…**, sau **Importă DEM ca suprafață (.asc)…**,
   sau **Importă puncte GPX (→ fix)…**.
2. ThIDE convertește în construcții Therion (un `.th`, un `surface` sau linii `fix`).
3. Legați noul fișier în thconfig și compilați. Vedeți [Import, export și GIS](17-import-export-and-gis.md).

## G. Urmărirea pistelor de explorare

1. Marcați pistele în fișierul `.th` așa cum o faceți de obicei (indicatori de continuare,
   comentarii `QM`/lead/`?`, fundături).
2. Deschideți **Prezentare generală → Piste**: ThIDE le adună într-un registru cu stare (Deschis /
   Verificat / Explorat / Mort) și le poate suprapune pe previzualizare.
3. Ciclul de viață complet și agregatorul TODO/QM: [Piste, sarcini TODO și metadate](14-leads-notes-and-metadata.md).

## H. Verificarea stării unui proiect mare de către coordonator

1. Deschideți întregul dosar; setați thconfig-ul activ.
2. **Prezentare generală → Tablou de bord** pentru lungime/adâncime/ultima compilare dintr-o
   privire; **Statistici / Grafice** pentru detaliere; **Echipă / Intrări / Calitate** pentru
   oameni, căi de acces și problemele datelor.
3. **Prezentare generală → Audit** (apăsați **Calculează**) pentru lipsurile structurale.
4. **Hartă relațională** (meniul Vizualizare) pentru a *vedea* cum se leagă survey-urile, scrap-urile
   și hărțile.
5. Dacă merge greu, ajustați **Setări → Performanță** și dezactivați previzualizările costisitoare pe
   care nu le folosiți — [Setări](19-settings-and-preferences.md).

---

Următorul: [Editorul →](07-editing.md)
