# 7. Editorul

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Editorul ThIDE este construit pe AvaloniaEdit și adaptat pentru Therion. Această pagină acoperă
experiența de scriere; deplasarea *prin* cod este descrisă în [Navigare și căutare](08-navigation-and-search.md).

## Evidențierea sintaxei

Toate cele patru tipuri de fișiere sunt colorate (cuvinte-cheie, identificatori, numere, șiruri,
comentarii, opțiuni, punctuație). În interiorul blocurilor `layout … endlayout`, codul **MetaPost** și
**TeX** încorporat este evidențiat cu propriile analizoare lexicale (vedeți
[docs/layout-and-embedded-code.md](../layout-and-embedded-code.md)).
Puteți recolora totul din **Setări → Temă & culori → Folosește culori de sintaxă personalizate** sau
pur și simplu treceți la tema **Întunecată**, care adaptează atât interfața, cât și paleta sintaxei.

## Ajutor la tastare

- **Completare automată** — sugestii în funcție de context pe măsură ce tastați (comenzi, opțiuni,
  stații cunoscute).
- **Carduri la hover** — treceți cu mouse-ul peste o referință la o stație/survey/fișier pentru un
  rezumat și acțiuni rapide (salt la definiție, găsire referințe, redenumire, documentație, afișare în
  3D, deschidere).
- **Închiderea automată a parantezelor/ghilimelelor** și indentare automată.

## Plierea

- Blocurile Therion (`survey…endsurvey`, `scrap…endscrap`, `centreline…`, `map…`, `layout…`) se
  pliază.
- Blocurile **`#@region … #@endregion`** se pliază și ele — un strat de directive ale aplicației care
  trăiește *în interiorul* comentariilor Therion (deci Therion îl ignoră). Selectați liniile și rulați
  **Include în regiune** (**Ctrl+Shift+R**, disponibil și în meniul Editare) pentru a le încadra, cu un
  titlu opțional. Referința completă: [Directivele aplicației (`#@`)](../directives.md).
- **Editare → Pliază tot / Depliază tot** restrâng sau extind totul deodată.

## Comentarii și majuscule

- **Comutare comentariu** — **Ctrl+/** pe linie sau pe selecție.
- **MAJUSCULE** / **minuscule** pentru selecție (meniul Editare sau Paleta de comenzi).

## Operații pe linii

Din meniul contextual al editorului (sau din Paleta de comenzi):

| Acțiune | Ce face |
|---|---|
| **Duplică linia (liniile)** | Copiază linia/selecția curentă sub ea însăși. |
| **Mută linia (liniile) în sus / în jos** | Mută liniile fără decupare și lipire. |
| **Sortează liniile selectate** | Sortează alfabetic liniile selectate. |
| **Inserează data de azi** | Inserează data de azi (util pentru `date`). |
| **Inserează membru echipă** | Inserează un membru `team` din proiectul dumneavoastră. |

## Formatarea

- **Formatează documentul** — **Shift+Alt+F** reindentează fișierul după imbricarea blocurilor.
- **Formatare la salvare** — activați-o din **Setări → Editor** pentru a formata automat la fiecare
  salvare.

Există și un **formatator pentru directivele de regiune**, astfel încât *Include în regiune* produce
linii de directivă curate, cu ghilimele corecte.

## Semne de carte

- **Editare → Adaugă semn de carte…** marchează linia curentă (cu un titlu opțional).
- **Căutare → Semne de carte…** le listează pe toate; apăsați **Salt la** pentru a ajunge la ele.
  Excelent pentru puncte de reper într-un fișier lung.

## Spații albe și afișare

Comutatoare din meniul **Vizualizare**: **Arată spații și taburi**, **Arată sfârșitul de linie**,
**Arată ghidajele de indentare**, **Arată minimapa**, plus **încadrarea textului** (bara de unelte).
O **minimapă** oferă o derulare de ansamblu pentru fișierele lungi.

## Fila Măsurători

Fiecare document `.th`/`.th2` are o subfilă **Măsurători** lângă **Sursă**: o grilă actualizată în
timp real cu vizările și stațiile fișierului, extrase din graful de obiecte.

- **Filtrați** după de la/până la/survey/indicatori/comentariu (vizări) sau nume/survey/tip (stații).
- **Grupați după** survey/altele; afișați sau ascundeți coloane; marcați **Supr. / Dupl. / Radial /
  Aprox.**.
- Clic dreapta pe un rând: **Salt la declarație** (pentru De la/Până la), **Arată în vizualizarea
  3D**, **Redenumește stația…**.
- Dublu-clic pentru a sări la linia din sursă.

Este cea mai rapidă cale de a verifica datele abia introduse fără a părăsi fișierul.

## Lucrul cu fișierele incluse

Proiectele Therion sunt arbori de includeri `input`/`source`. În editor:

- **Intră în fișierul inclus** — **Alt+↓** pe o linie `input` deschide ținta.
- Antetul fișierului oferă **Mergi la fișierul părinte** (fișierul care îl include pe acesta).
- **Salt la blocul pereche** — **Ctrl+]** sare între deschiderea unui bloc și `end…`-ul său.

## Predarea unui scrap către Mapiah

Pentru fișierele `.th2`, antetul conține **Editează cu Mapiah** — lansează schița în
[Mapiah](17-import-export-and-gis.md#desenare-cu-mapiah) și o reîncarcă automat când salvați acolo.

## Opțiunile de comportament ale editorului

Din **Setări → Editor** puteți ajusta: dimensiunea fontului și a indentării, numerele de linie,
evidențierea liniei curente, conversia taburilor în spații, **Necesită dublu-clic pentru a merge la
definiție** (față de hyperlink cu un singur clic), **Validează la tastare**, previzualizarea
redenumirii și formatarea la salvare. Tot acolo pot fi dezactivate și îmbunătățiri individuale ale
editorului. Vedeți [Setări](19-settings-and-preferences.md#editor).

---

Următorul: [Navigare și căutare →](08-navigation-and-search.md)
