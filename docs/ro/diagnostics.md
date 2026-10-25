# Catalogul diagnosticelor

> Mesajele localizate se află în `Therion.Core.Resources` (`Strings.resx` / `Strings.ro.resx`).
> Severitățile de mai jos sunt valorile implicite din modul permisiv (lenient); în `ParserMode.Strict`
> rândurile „lenient” sunt ridicate la Error.

## Nucleul parserului (`TH00xx`)

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0002` | Warning | Token neașteptat `<text>`.     | `ThconfigParser` |
| `TH0010` | lenient | Comandă necunoscută `<keyword>`. | `ThconfigParser`, `ThParser`, `Th2Parser` |
| `TH0020` | lenient | Blocului `<x>` îi lipsește terminatorul (de ex. `endsurvey`). | `ThParser` |
| `TH0021` | Error   | Terminator de bloc nepotrivit (s-a găsit un alt `end…`). | `ThParser` |
| `TH0030` | lenient | `fix` malformat (`x`/`y`/`z` lipsă sau nenumerice). | `ThParser` |
| `TH0031` | Warning | `equate` malformat (necesită cel puțin două stații). | `ThParser` |
| `TH0032` | Warning | `data` malformat (necesită cel puțin un nume de stil). | `ThParser` |
| `TH0033` | lenient | Stil de date necunoscut `<style>`. | `ThParser` |
| `TH0034` | lenient | Citire de date necunoscută `<keyword>`. | `ThParser` |
| `TH0037` | lenient | Linie cu o singură coloană într-o linie centrală (`<tok>`): nu este o comandă validă și nici o vizare. | `ThParser` |
| `TH0038` | lenient | `sd` malformat (necesită `<quantity…> <value> <unit>`). | `ThParser` |
| `TH0039` | lenient | `grid-angle` / `vthreshold` malformat (necesită o valoare numerică). | `ThParser` |
| `TH0040` | lenient | `units` malformat (mărime necunoscută / unitate lipsă). | `ThParser` |
| `TH0041` | lenient | `calibrate` malformat (lipsește eroarea de zero). | `ThParser` |
| `TH0042` | lenient | `declination` malformat (necesită o valoare numerică / o listă datată / `-`). | `ThParser` |
| `TH0043` | lenient | Sistem de coordonate necunoscut `<cs>`. | `ThParser`, `ThconfigParser` |
| `TH0056` | lenient | Specificație `infer` invalidă (se așteaptă `<plumbs\|equates> <on\|off>`). | `ThParser` |

### Identificatori și potrivirea blocurilor

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0050` | lenient | Identificatorul conține un caracter din afara setului keyword/ext-keyword. | `ThParser` (survey/map), `Th2Parser` (scrap) |
| `TH0051` | lenient | Id-ul din `endsurvey`/`endscrap` nu corespunde celui de deschidere. | `ThParser`, `Th2Parser` |

### Enumerări pentru argumentele liniei centrale

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0052` | lenient | Indicator de vizare necunoscut (`flags`). | `ThParser` |
| `TH0053` | lenient | Tip de marcaj necunoscut (`mark`). | `ThParser` |
| `TH0054` | lenient | Specificație extend necunoscută (`extend`). | `ThParser` |
| `TH0055` | lenient | Indicator de stație necunoscut (`station`). | `ThParser` |

### Argumentele comenzilor .thconfig

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0060` | lenient | Tip de export necunoscut (`export <type>`). | `ThconfigParser` |
| `TH0061` | lenient | Valoare `-fmt` invalidă pentru tipul de export. | `ThconfigParser` |
| `TH0062` | lenient | Cheie de opțiune layout necunoscută. | `LayoutBodyParser` (`.th` + `.thconfig`) |

### Validare bazată pe schemă (`SchemaValidator`, efortul de acoperire a sintaxei)

Emise de etapa declarativă de schemă (`Therion.Syntax/Schema/`); completată incremental —
vedeți `.claude/therion-syntax/PLAN.md`. Pot fi activate/dezactivate pe categorie/secțiune prin
`ParserOptions.Validation` (`SchemaValidationOptions`); note de performanță în `.claude/therion-syntax/PERF.md`.

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0063` | Error | Comanda are mai puține argumente decât sunt necesare. | `SchemaValidator` |
| `TH0064` | Warning | Comanda are mai multe argumente decât sunt permise. | `SchemaValidator` |
| `TH0065` | lenient | Valoarea argumentului/opțiunii nu respectă tipul sau enumerarea declarată. | `SchemaValidator` |
| `TH0066` | lenient | Opțiune nevalidă pentru acest tip de obiect (de ex. `-text` pe un punct care nu este etichetă; matrice pe tipuri preluată din thpoint.cxx). | `Th2PointRules` |
| `TH0067` | Info | Cuvânt cheie corect, dar cu majuscule/minuscule greșite (`thmatch_token` din Therion face distincție între ele). Se declanșează pentru toate tabelele de enumerări din schemă — grafia stocată este recuperată chiar și prin căutările noastre insensibile la majuscule; tabelele pe care Therion le potrivește cu `thcasematch_token` sunt excluse prin `ValueSpec.CaseSensitive = false`. | `SchemaValidator` |
| `TH0068` | Warning | Nu este nici număr, nici valoare specială (`-` `.` NaN Inf up down). | `SchemaValidator` |
| `TH0069` | Warning | Valoare numerică în afara intervalului din schemă (vthreshold 0–90, fix sd > 0, …). | `SchemaValidator`, `ThCentrelineRules` |

`ThCentrelineRules` (noduri tipizate ale liniei centrale; secțiunea `centreline`) emite și: `TH0064` (fix > 7
argumente, extend > 3 stații, > 21 de citiri — `THDATA_MAX_ITEMS` este 22, dar numără și tokenul de stil),
`TH0065` (rol de echipă / mărime de instrument necunoscute, amestec de clase lungime↔unghi la sd/units), `TH0042`
(declinație numerică fără unități), `TH0055` (indicator `fixed` direct; `explored` fără
`continuation`).

### Validarea ordinii în `data <style> <readings>` (`ThCentrelineRules`, lenient)

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH0070` | lenient | Citire nevalidă pentru stilul de date (de ex. `gradient` în afara `normal`). | `DataStyles.ValidateOrder` |
| `TH0071` | lenient | Citire listată de două ori (aliasurile contează: `tape` ≡ `length`). | `DataStyles.ValidateOrder` |
| `TH0072` | lenient | „Not all data for given style” — lipsesc citiri obligatorii. | `DataStyles.ValidateOrder` |
| `TH0073` | lenient | `newline` nu poate fi prima sau ultima citire. | `DataStyles.ValidateOrder` |
| `TH0074` | lenient | `station` amestecat cu `from`/`to`; citire intercalată după `newline`; sau o citire neintercalată (de vizare) înainte de `newline` în date intercalate. | `DataStyles.ValidateOrder` |

## Semantică (`TH_SEM_xxx`)

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH_SEM_001` | Warning | Referință de stație nerezolvată `<name>` (cu sugestie opțională „did you mean”). Referințele din equate sunt validate la nivelul întregului spațiu de lucru, deci țintele din alte fișiere / calificate cu `@` se rezolvă în tot proiectul, iar acesta se declanșează doar când o referință nu se rezolvă nicăieri. | `SemanticBinder` (standalone) / `WorkspaceSemanticModel.ValidateEquateReferences` (project) |
| `TH_SEM_002` | Warning | Stația `<name>` este fixată de mai multe ori. | `SemanticBinder` |
| `TH_SEM_003` | Warning | Referință de stație malformată `<name>`. | `SemanticBinder` |
| `TH_SEM_005` | Warning | Numărul de coloane al rândului de date nu corespunde ordinii citirilor. | `SemanticBinder` |
| `TH_SEM_006` | Error   | Valoarea din rândul de date nu este validă pentru citirea respectivă (lungime/busolă/clinometru nenumerice). | `SemanticBinder` |
| `TH_SEM_007` | Warning | Valoarea din rândul de date este în afara intervalului pentru citirea respectivă (busolă > 360°, clinometru > 180°). | `SemanticBinder` |
| `TH_SEM_010` | Info    | Coliziune de nume între fișiere (DIAG-05). | `ProjectDiagnostics` |
| `TH_SEM_011` | Warning | Eroare de închidere a buclei peste toleranță (DIAG-02). | `ProjectDiagnostics` |
| `TH_SEM_012` | Warning/Info | Vizare greșită/aberantă (DIAG-03). | `ProjectDiagnostics` |
| `TH_SEM_013` | Warning | Nepotrivire între vizarea directă și cea inversă (DIAG-04). | `ProjectDiagnostics` |
| `TH_SEM_014` | Warning | Fișier negăsit: o țintă `input`/`source` care nu există pe disc, raportată pe linia care o numește (DIAG-06). | `ProjectDiagnostics` |
| `TH_SEM_015` | Warning | O bucată conectată de survey nu este legată nici de rețeaua principală, nici georeferențiată printr-un `fix` — este „în aer” (se numesc fișierele sursă și cele două stații de capăt). | `ProjectDiagnostics` |
| `TH_SEM_NAMING` | configurable | Încălcarea unei reguli de convenție de denumire definite de utilizator (LANG-13). | `NamingConventionRule` |

## Formatul XVI (export Tcl `set XVI*`)

Stratul de sintaxă (`Therion.Syntax`):

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH_XVI_001` | lenient | Variabilă `set XVI…` necunoscută. | `XviParser` |
| `TH_XVI_002` | lenient | Instrucțiune neașteptată, alta decât `set`. | `XviParser` |
| `TH_XVI_003` | Error   | Blocului `{` îi lipsește `}` de închidere. | `XviParser` |
| `TH_XVI_004` | lenient | `XVIgrid` nu are 8 valori numerice. | `XviParser` |

Stratul semantic (`Therion.Semantics`):

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH_XVI_050` | Error   | O țintă `-sketch` referită dintr-un scrap `.th2` nu a fost găsită pe disc. | `XviIndex` |

## Formatul de desen .th2 (`TH2_xxx`)

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH2_001` | lenient | `point` malformat (necesită x, y, tip; coordonatele trebuie să fie numerice). | `Th2Parser` |
| `TH2_002` | lenient | Blocului `line` îi lipsește `endline`. | `Th2Parser` |
| `TH2_003` | lenient | Blocului `area` îi lipsește `endarea`. | `Th2Parser` |
| `TH2_004` | lenient | Tip de punct necunoscut `<type>`. | `Th2Parser` |
| `TH2_005` | lenient | Tip de linie necunoscut `<type>`. | `Th2Parser` |
| `TH2_006` | lenient | Tip de arie necunoscut `<type>`. | `Th2Parser` |
| `TH2_008` | lenient | Subtip invalid pentru tipul de punct (doar station/air-draught/water-flow/u: acceptă subtipuri; matrice de valori pe tipuri preluată din thpoint.cxx). | `Th2PointRules` |
| `TH2_009` | lenient | `-option` necunoscută pe un obiect point/line/area. | `Th2Parser` |
| `TH2_010` | lenient | Blocului `scrap` îi lipsește `endscrap`. | `Th2Parser` |

## Directive ale aplicației (`THIDE_DIR_xxx`)

Extensii specifice ThIDE scrise în comentarii Therion (`#@…`) — invizibile pentru Therion.
Produse de `DirectiveScanner` și expuse prin `DocumentParser`.

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `THIDE_DIR_001` | Warning | Lui `#@region` îi lipsește `#@endregion`. | `DirectiveScanner` |
| `THIDE_DIR_002` | Warning | `#@endregion` nu are un `#@region` corespunzător. | `DirectiveScanner` |

## Spațiu de lucru (`TH_WS_xxx`)

| Cod | Severitate | Mesaj | Sursă |
|---|---|---|---|
| `TH_WS_001` | Error   | Cale negăsită: `<path>`.                    | `ProjectEntryPointResolver` |
| `TH_WS_002` | Warning | Nu s-a găsit niciun fișier de configurare Therion în `<folder>`. | `ProjectEntryPointResolver` |
