# 20. Extensibilitate (CLI, LSP, asistenți AI, pluginuri, hook-uri)

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Pentru utilizatorii avansați, felul în care ThIDE înțelege Therion este disponibil și *în afara*
interfeței grafice și poate fi extins. Nimic din toate acestea nu este necesar în utilizarea de zi cu zi —
dar există atunci când doriți să automatizați sau să integrați.

## Instrumentul de linie de comandă (`therion-cli`)

Un instrument **fără interfață grafică** (headless) care rulează aceeași analiză ca aplicația — ideal
pentru scripturi, CI sau o verificare rapidă fără a deschide interfața grafică:

```sh
therion-cli validate path/to/project.thconfig   # raportează diagnosticele
therion-cli lint     path/to/file.th             # verificări de stil / convenții
therion-cli format   path/to/file.th --write     # reindentare pe loc
therion-cli format   caves --check               # listează fișierele care s-ar schimba; ieșire 1 dacă există (CI, pre-commit)
therion-cli import   raw/trip.csv --out caves/trip.th   # Survex, Compass, GPX sau un tabel de vize → .th nou
therion-cli report   path/to/project.thconfig --out output/report.html
therion-cli stats    path/to/project.thconfig    # lungime/adâncime etc.
therion-cli deps     path/to/project.thconfig --dot   # graful de includeri (Graphviz)
therion-cli gis      path/to/project.thconfig --format kml --out entrances.kml
therion-cli new      --list                      # șabloane de proiect (integrate și importate)
therion-cli new      single-cave --out ~/caves/pit --var project=pit
```

Există și `dump-ast` și `list-stations`. Lista completă de comenzi și exemple:
[docs/usage.md → Instrumente de linie de comandă](../../usage.md#command-line-tools).

## Serverul de limbaj (`therion-lsp`)

Un **Language Server independent de editor**, care oferă diagnosticele Therion prin stdio — configurați
orice editor compatibil LSP (VS Code, Neovim, …) să îl folosească și veți avea verificările ThIDE în
editorul preferat. Instalarea și configurarea clientului: **[docs/lsp.md](../../lsp.md)**.

## Asistenți AI (MCP)

> **Experimental — în lucru.** Integrarea MCP și panoul Asistent sunt recente și încă se schimbă.
> Așteptați-vă la asperități și la setări sau comportamente care se modifică de la o versiune la alta.

ThIDE poate permite unui **asistent AI local** să răspundă la întrebări despre proiectul
dumneavoastră — *„ce nu e în regulă cu această peșteră?”*, *„este totul o singură bucată conectată?”*,
*„unde este declarată stația 12?”* — fără să lipiți fișiere într-o fereastră de chat. Face acest lucru
vorbind **Model Context Protocol (MCP)**, la fel cum unelte precum Claude sau un model local din
**LM Studio** comunică cu instrumentele lor.

Trei moduri de utilizare:

- **Panoul Asistent integrat (nicio configurare în afară de un model local).** **Vizualizare → Asistent**
  deschide un chat în ThIDE care comunică cu un model local servit de **LM Studio** (sau cu orice
  endpoint compatibil OpenAI — setați-l în **Setări → Extensii**). Răspunde pe baza proiectului deschis,
  iar fiecare modificare pe care o propune se oprește mai întâi la un card **Permite / Refuză** — nimic nu
  este scris până nu permiteți. Înainte de prima întrebare vă întreabă ce are voie să trimită modelului:
  tot (implicit, pentru un model de pe propriul calculator), nimic din proiect sau rezultatele cu valorile
  ascunse.
- **Pe fișierele de pe disc (fără ThIDE).** Un mic program, `therion-mcp`, rulează motoarele ThIDE fără
  interfață grafică. Gazda AI (LM Studio, Claude Code, …) îl lansează și poate apoi pune întrebări despre
  un dosar de proiect. Citește fișierele dumneavoastră; poate de asemenea să formateze, să genereze
  structuri de proiect, să importe/exporte și să ruleze Therion — sau, dacă preferați, poate fi limitat
  la **doar citire** cu un singur comutator.
- **Pe aplicația pe care o aveți deschisă.** Activați **Setări → Extensii → Activează serverul de unelte
  AI din aplicație (MCP)**, iar ThIDE-ul *care rulează* oferă aceleași capabilități plus unele în timp
  real: asistentul poate vedea fișierul pe care îl editați (inclusiv modificările nesalvate), poate
  deschide un fișier, focaliza un panou sau arăta o stație în vizualizarea 3D. Dacă are voie să *atingă*
  fereastra — nu doar să o citească — decide caseta **„Urmează agentul”** de lângă această setare;
  debifați-o ca asistentul să nu vă umble la aranjament, permițându-i în continuare să răspundă la
  întrebări.

**Este sigur?** Serverul din aplicație ascultă doar pe propriul calculator (loopback), în spatele unei
parole aleatorii, și este **dezactivat implicit**. Un asistent nu poate ieși niciodată din dosarul
proiectului, iar o editare a unui fișier pe care îl editați chiar acum (cu modificări nesalvate) este
refuzată, în loc să vă suprascrie munca. Dacă sunteți precaut, porniți-l în modul doar citire.

Configurarea unei anumite gazde AI (LM Studio, Claude Code) înseamnă câteva rânduri de configurație —
vedeți pașii detaliați în **[docs/mcp-host-setup.md](../../mcp-host-setup.md)**.

## Pluginuri de reguli semantice

Puteți adăuga **propriile reguli de validare** fără a recompila ThIDE:

1. Scrieți un mic tip .NET care implementează **`ISemanticRule`**.
2. Puneți fișierul `.dll` compilat în dosarul `plugins` al dosarului de setări al ThIDE (vedeți
   [Instalare](02-installation-and-setup.md#6-unde-își-stochează-thide-setările)).
3. Activați **Încarcă pluginuri** în [Setări → Extensii](19-settings-and-preferences.md#extensii) și
   reporniți.

Regula dumneavoastră produce apoi diagnostice la fel ca cele integrate. API-ul și exemple:
**[docs/plugins.md](../../plugins.md)**. Regulile pot fi configurate și printr-un `rules.json`.

## Hook-uri de script

Rulați o comandă externă în momentele-cheie:

1. Activați **hook-urile de script** în [Setări → Extensii](19-settings-and-preferences.md#extensii).
2. Cele trei comenzi simple (**La deschidere**, **La salvare**, **La compilare**) funcționează ca înainte —
   pornite și lăsate să ruleze singure. `{file}` este calea fișierului (la compilare, thconfig-ul care se
   compilează), acum pusă între ghilimele, astfel încât o cale cu spații rămâne un singur argument;
   *la salvare* urmează acum și **Salvează toate** și salvarea automată.
3. **Alte hook-uri** adaugă oricâte doriți, fiecare pentru un eveniment — *WorkspaceOpened*,
   *FileOpened*, *FileSaved*, *BuildStarted*, *BuildFinished*, *BuildFailed* — cu un timp limită.
   Ieșirea lor ajunge în panoul **Automatizare** (**Vizualizare → Automatizare**), dacă nu sunt marcate
   *Detașat*.

Comenzile pot folosi `{file}`, `${file}`, `${fileDirname}`, `${fileBasename}`, `${workspaceRoot}`,
`${activeThconfig}`, `${entry}` și `${env:NAME}`. Fiecare hook primește și variabile de mediu:
`THIDE_EVENT`, `THIDE_FILE`, `THIDE_WORKSPACE`, `THIDE_THCONFIG`, `THIDE_ENTRY`, iar după o compilare
`THIDE_BUILD_EXIT`, `THIDE_BUILD_ERRORS`, `THIDE_BUILD_WARNINGS`, `THIDE_BUILD_LOG`, `THIDE_OUTPUTS`
(separate ca în `PATH`) și `THIDE_ELAPSED_MS` — astfel *BuildFinished* poate publica PDF-ul, iar
*BuildFailed* vă poate anunța. Folosiți-le pentru copii de rezervă, post-procesare, notificări sau un
commit în sistemul de versionare.

### Hook-uri și sarcini ale proiectului (`.thide/`)

Un proiect își poate purta propria automatizare în dosarul său `.thide/` (inclus în commit odată cu
peștera):

```jsonc
// .thide/hooks.json
{ "version": 1, "hooks": [
  { "event": "BuildFinished", "command": "rsync -a output/ server:/caves/${env:USER}/", "timeoutSeconds": 120 },
  { "event": "FileSaved", "task": "lint" }
] }

// .thide/tasks.json
{ "version": 1, "tasks": [
  { "id": "lint", "title": "Check names", "command": "python3 tools/check.py ${file}", "problemMatcher": "file-line" },
  { "id": "all", "title": "Build every cave", "type": "builtin", "builtin": "batch-build", "entries": ["caves/a.thconfig", "caves/b.thconfig"] },
  { "id": "therion-all", "title": "Therion on the master", "command": "therion", "args": ["all.thconfig"], "problemMatcher": "therion" }
] }
```

Sarcinile se rulează din **Unelte → Sarcini** sau din paleta de comenzi (*Sarcină: …*). Câmpul
`problemMatcher` al unei sarcini (`therion` sau `file-line` pentru `path:line[:col]: message`) transformă
ieșirea ei în intrări în panoul Diagnostice. O sarcină poate fi `shell` (implicit), `builtin` (`build`,
`batch-build`) sau `macro`.

### Macrouri

Un macro este o listă de apeluri de instrumente — aceleași instrumente pe care un asistent AI le
folosește prin MCP — rulate fără niciun model, cu variabile pe care le completați. **Unelte → Macrouri**
(sau paleta de comenzi, *Macro: …*) listează macrourile integrate (*Release check*: diagnostice,
compilare, raport; *End of trip*: convertește un export Survex/Compass aflat în proiect — în
`incoming/`, de exemplu — și listează avertismentele), pe ale dumneavoastră (`macros/*.json` în dosarul
de setări al ThIDE) și pe cele ale proiectului (`.thide/macros/*.json`). Fiecare rulare face **întâi o
previzualizare** în panoul Automatizare: pașii care își pot arăta modificarea fără să o facă (`dryRun`
sau `write: false`) o arată, pașii doar-citire rulează, iar restul sunt listați ca fiind în așteptare;
ThIDE întreabă apoi înainte de aplicare (o previzualizare eșuată nu este aplicată niciodată). Pentru
instrumentele care previzualizează cu `dryRun`, un fișier modificat între previzualizare și aplicare
este refuzat. Căile sunt relative la proiect: un fișier ales pentru o variabilă `file` trebuie să se
afle în interiorul lui.

```json
{ "title": "Tidy a trip", "description": "Format it and check it",
  "vars": [ { "id": "file", "prompt": "Survey file", "type": "file" } ],
  "steps": [
    { "id": "fmt", "tool": "format_file", "args": { "path": "${file}", "write": true } },
    { "id": "diag", "tool": "get_diagnostics", "args": { "file": "${file}", "minSeverity": "warning" } },
    { "id": "build", "tool": "run_build", "continueOnError": true } ] }
```

Argumentele pot folosi `${variable}`, `${steps.<id>.<path>}` (o valoare din rezultatul unui pas
anterior, de exemplu `${steps.imp.data.target}`), `${file:basename(var)}`, `${file:dir(var)}`, `${date}`
și `${workspaceRoot}`. Un pas care eșuează oprește macroul, cu excepția cazului în care specifică
`continueOnError`. Tipuri de variabile: `text`, `number`, `bool`, `choice` (cu `choices`), `file`,
`folder`. O sarcină poate rula un macro (`"type": "macro", "macro": "<name>"`). Dintr-un terminal:
`therion-mcp macro run tidy --workspace cave.thconfig --var file=trips/a.th` face previzualizarea;
adăugați `--apply` pentru a face modificările (`--json` pentru un raport, `therion-mcp macro list`
pentru nume). Macrouri integrate: `audit` (diagnostice, graful topografiei, starea desenelor),
`release-check` (diagnostice, compilare, raport HTML) și `end-of-trip` (importă un export
Survex/Compass, apoi avertismentele).

**Un singur instrument din shell.** `therion-mcp call <tool> --workspace cave.thconfig --arg key=value`
(sau `--args '{"key": …}'`, `--args-file f.json`) apelează un singur instrument și afișează rezultatul
lui JSON; codul de ieșire este 1 când instrumentul raportează un eșec. `therion-mcp call --list`
enumeră instrumentele. Instrumentele care modifică fișiere fac în continuare doar previzualizarea, dacă
nu transmiteți `dryRun=false` (sau `write=true` pentru `format_file`). Un `run_build` al unui thconfig
cu linii `system` (comenzi shell pe care le rulează Therion) este refuzat din terminal dacă nu adăugați
`--allow-system-commands`, la `call` sau la `macro run`; în ThIDE, în schimb, aplicația vă întreabă
dacă aveți încredere în dosar.

**Încredere.** Comenzile dintr-un proiect rulează doar după ce acordați încredere dosarului său: prima
dată când ar trebui să ruleze ceva, ThIDE numește dosarul și vă întreabă. Răspunsul este reținut;
**Setări → Extensii → Dosare de încredere** le listează și puteți elimina unul de acolo. Citirea
fragmentelor de cod și a șabloanelor proiectului nu cere niciodată confirmare. Comutatorul
*Citește hook-uri, sarcini și macrouri din dosarul .thide al proiectului* dezactivează toate acestea.

## Unde se află lucrurile

- Pluginuri: `plugins/*.dll` în dosarul de setări al ThIDE
- Configurația regulilor: `rules.json` (dosarul de setări)
- Fragmente de cod: `snippets.json`; șabloane de fișiere: `templates/files/*.json` — în dosarul de setări,
  pentru uzul propriu, și în dosarul `.thide/` al proiectului (sau în geamănul său privat din dosarul de
  setări, după cum alegeți în **Setări → General**), pentru proiect. Vedeți
  [Editorul → Fragmente de cod și fișiere noi](07-editing.md#fragmente-de-cod-și-fișiere-noi).
- CLI-ul și LSP-ul sunt **executabile separate** livrate alături de aplicație, nu interfața grafică
  propriu-zisă.

Vedeți și documentul pentru dezvoltatori [docs/architecture.md](../../architecture.md) dacă doriți să
reutilizați bibliotecile de bază în propriul program .NET.

---

Următorul: [Scurtături tastatură →](21-keyboard-shortcuts.md)
