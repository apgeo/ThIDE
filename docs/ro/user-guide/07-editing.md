# 7. Editorul

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Editorul ThIDE este construit pe AvaloniaEdit și adaptat pentru Therion. Această pagină se ocupă de
scriere; deplasarea *prin* cod este descrisă în [Navigare și căutare](08-navigation-and-search.md).

## Evidențierea sintaxei

Toate cele patru tipuri de fișiere sunt colorate (cuvinte-cheie, identificatori, numere, șiruri,
comentarii, opțiuni, punctuație). În interiorul blocurilor `layout … endlayout`, codul **MetaPost** și
**TeX** încorporat este evidențiat cu propriile analizoare lexicale (vedeți
[docs/layout-and-embedded-code.md](../../layout-and-embedded-code.md)).
Puteți recolora totul din **Setări → Temă & culori → Folosește culori de sintaxă personalizate** sau
pur și simplu treceți la tema **Întunecată**, care adaptează atât interfața, cât și paleta sintaxei.

## Ajutor la tastare

- **Completare automată** — sugestii în funcție de context pe măsură ce tastați (comenzi, opțiuni,
  stații cunoscute).
- **Carduri la trecerea cu mouse-ul** — țineți mouse-ul peste o referință la o
  stație/topografie/fișier pentru un rezumat și acțiuni rapide (salt la definiție, găsirea
  referințelor, redenumire, documentație, afișare în 3D, deschidere).
- **Închiderea automată a parantezelor/ghilimelelor** și indentare automată.

## Fragmente de cod și fișiere noi

Tastați declanșatorul unui fragment de cod la începutul unei linii și apăsați **Tab** (sau alegeți-l
din completarea automată ori din meniul de clic dreapta: **Generează cod** ▸ **Inserează fragment**).
Fragmentul este inserat cu **câmpurile** evidențiate: tastați pentru a-l completa pe primul, **Tab** /
**Shift+Tab** pentru a trece de la unul la altul și **Enter** sau **Esc** când ați terminat. Un câmp
folosit de două ori se schimbă în ambele locuri.

| Declanșator | Inserează |
|---|---|
| `survb` | un bloc `survey … endsurvey` cu data de azi |
| `clt` | un `centreline` cu data de azi, plus echipa și linia `data` ale ultimei ieșiri de deasupra cursorului |
| `leg` | tronsonul următor: de la ultima stație la numărul următor (`A07` → `A08`) |
| `eq`, `inp` | un `equate`, un `input` |
| `scrapb`, `mapb` | blocuri `scrap` (alegeți proiecția) și `map` |
| `thconf`, `layout-plan-500`, `layout-copy`, `export-plan-pdf`, `export-model-lox` | schelet și fragmente de thconfig (oferite în fișierele thconfig) |

**Fragmentele dumneavoastră proprii** se pun în `snippets.json` — în dosarul de setări al ThIDE, doar
pentru dumneavoastră, sau în dosarul `.thide/` al proiectului, ca să le împărțiți cu toți cei care au
proiectul (locul în care stau fișierele proiectului se alege în **Setări → General**). Fișierul este o
listă de intrări:

```json
[
  { "trigger": "fixutm", "description": "fix in UTM 34N", "scope": "th",
    "template": "fix ${1:station} ${2:easting} ${3:northing} ${4:altitude}$0" }
]
```

Șabloanele folosesc sintaxa de fragmente din VS Code: `$1`, `${1:default}`, `${1|plan,elevation|}` (o
alegere), `$0` (unde ajunge cursorul la final) și variabile — `$DATE`, `$TIME`, `$YEAR`, `$FILE_NAME`,
`$FILE_BASENAME`, `$CURRENT_SURVEY`, `$PARENT_SURVEY`, `$TEAM_RECENT`, `$LAST_UNITS`,
`$LAST_CALIBRATE`, `$LAST_INSTRUMENT`, `$LAST_DATA`, `$LAST_STATION`, `$NEXT_STATION` sau
`${NAME:fallback}`. Scrieți `\$` pentru un semn dolar literal. `scope` este `th`, `th2`, `thconfig` sau
`any`. Un fișier stricat este raportat o singură dată, iar restul fragmentelor continuă să funcționeze.

Dialogul **Export rapid** (**Compilare → Export rapid…**) are un buton **Salvează ca fragment…**: el
salvează blocul de export pe care l-ați compus ca fragment de thconfig, oferit apoi în **Generează cod**
▸ **Inserează fragment** în fișierele thconfig.

**Fișier → Fișier nou…** (disponibil și pe pagina Bun venit, în paleta de comenzi și în meniul de clic
dreapta al Exploratorului spațiului de lucru) creează un fișier dintr-un șablon — ieșire de
topografie, topografie de legătură, thconfig, schiță th2, bibliotecă de layout-uri sau fișier gol — îl
previzualizează și poate adăuga linia `input` într-un fișier de topografie din același dosar.
Șabloanele dumneavoastră proprii sunt fișiere JSON în `templates/files/` (dosarul de setări sau
`.thide/` al proiectului), fiecare de forma `{ "id", "title", "description", "extension",
"defaultName", "body" }`, cu un corp scris în aceeași sintaxă de șablon.

## Plierea

- Blocurile Therion (`survey…endsurvey`, `scrap…endscrap`, `centreline…`, `map…`, `layout…`) se
  pliază.
- Blocurile **`#@region … #@endregion`** se pliază și ele — un strat de directive ale aplicației care
  stă *în interiorul* comentariilor Therion (deci Therion îl ignoră). Selectați liniile și rulați
  **Include în regiune** (**Ctrl+Shift+R**, disponibil și în meniul Editare) pentru a le încadra, cu
  un titlu opțional. Referința completă: [Directivele aplicației (`#@`)](../../directives.md).
- **Editare → Pliază tot** și **Editare → Depliază tot** restrâng sau extind totul deodată.

## Comentarii și majuscule

- **Comutare comentariu** — **Ctrl+/** pe linie sau pe selecție.
- **MAJUSCULE** / **minuscule** pentru selecție (meniul Editare sau Paleta de comenzi).

## Operații pe linii

Din meniul contextual al editorului (sau din Paleta de comenzi):

| Acțiune | Ce face |
|---|---|
| **Duplică liniile** | Copiază linia curentă/selecția sub ea însăși. |
| **Mută liniile în sus / Mută liniile în jos** | Mută liniile fără decupare și lipire. |
| **Sortează liniile selectate** | Sortează alfabetic liniile selectate. |
| **Lipește ca date de vize…** | Transformă un tabel de vize (foaie de calcul, CSV, DistoX) într-un bloc `data` (și din **Editare → Lipește ca date de vize…**) — vedeți [Import](17-import-export-and-gis.md#tabele-de-vize-foi-de-calcul-csv-pockettopo-distox). |
| **Generează cod** ▸ **Inserează data de azi** | Inserează data de azi (util pentru `date`). |
| **Generează cod** ▸ **Inserează membru echipă** | Inserează un membru `team` din proiectul dumneavoastră. |

## Formatarea

- **Formatează documentul** — **Shift+Alt+F** reindentează fișierul după imbricarea blocurilor. Nu
  reordonează și nu elimină nicio linie; `therion-cli format --check` aplică aceeași regulă în CI.
- **Formatare la salvare** — activați **Formatează documentul la salvare** în pagina **Editor** a
  setărilor (**Setări → Editor**) pentru a formata automat la fiecare salvare.

Există și un **formatator pentru directivele de regiune**, astfel încât *Include în regiune* produce
linii de directivă curate, cu ghilimelele corecte.

## Semne de carte

- **Editare → Adaugă semn de carte…** marchează linia curentă (cu un titlu opțional).
- **Căutare → Semne de carte…** le listează pe toate; apăsați **Salt la** pentru a sări la unul.
  Excelente ca „locuri de parcare” într-un fișier lung.

## Spații albe și afișare

Comutatoare din meniul **Vizualizare**: **Arată spații și taburi**, **Arată sfârșitul de linie**,
**Arată ghidajele de indentare**, **Arată minimapa**, plus **încadrarea textului** (bara de
instrumente). O **minimapă** oferă o vedere de ansamblu pentru derularea fișierelor lungi.

## Fila Măsurători

Fiecare document `.th`/`.th2` are, lângă **Sursă**, o subfilă **Măsurători**: o grilă actualizată în
timp real cu vizele și stațiile acelui fișier, extrase din graful de obiecte.

- **Filtrați** după de la/la/topografie/marcaje/comentariu (vize) sau după
  nume/topografie/tip (stații).
- **Grupați după** topografie/altele; afișați sau ascundeți coloane; marcați **Supr. / Dupl. / Radial /
  Aprox.**
- Clic dreapta pe un rând: **Salt la declarație** (pentru De la/La), **Arată în vizualizarea 3D**,
  **Redenumește stația…**.
- Dublu-clic pentru a sări la linia din sursă.

Este cea mai rapidă cale de a verifica datele abia introduse fără a părăsi fișierul.

## Lucrul cu fișierele incluse

Proiectele Therion sunt arbori de includeri `input`/`source`. În editor:

- **Intră în fișierul inclus** — **Alt+↓** pe o linie `input` deschide fișierul țintă.
- Antetul fișierului oferă **Mergi la fișierul părinte** (fișierul care îl include pe acesta).
- **Salt la blocul pereche** — **Ctrl+]** sare între începutul unui bloc și `end…`-ul său.

## Predarea unei schițe către Mapiah

Pentru fișierele `.th2`, antetul are **Editează cu Mapiah** — deschide schița în
[Mapiah](17-import-export-and-gis.md#desenare-cu-mapiah) și o reîncarcă automat când salvați acolo.

## Opțiuni de comportament ale editorului

În **Setări → Editor** puteți ajusta: fontul și mărimea indentării, numerele de linie, evidențierea
liniei curente, conversia taburilor în spații, **Necesită dublu-clic pentru a merge la definiție**
(față de legătura activată printr-un singur clic), **Validează la tastare**, previzualizarea
redenumirii și formatarea la salvare. Tot acolo pot fi dezactivate individual îmbunătățirile
editorului. Vedeți [Setări](19-settings-and-preferences.md#editor).

---

Următorul: [Navigare și căutare →](08-navigation-and-search.md)