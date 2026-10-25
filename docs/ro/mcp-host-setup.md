# Serverul MCP (`therion-mcp`)

> ⚠️ **Experimental — în lucru.** Integrarea MCP este nouă și încă se schimbă. Așteptați-vă la
> asperități și la setări sau comportamente care se modifică de la o versiune la alta.

> **Ciornă.** Serverul headless (22 de unelte doar-citire + 12 care scriu) și conexiunea la IDE-ul
> *pornit* (buffere live, deschiderea fișierelor, controlul panourilor) sunt ambele finalizate. Finisarea
> acestei pagini — matricea de modele și ghidul pentru speologi — vine într-o etapă ulterioară.

`therion-mcp` expune motoarele de parsare, semantică și spațiu de lucru ale ThIDE prin
[Model Context Protocol](https://modelcontextprotocol.io), astfel încât un LLM poate răspunde la întrebări
despre un proiect Therion — ce este stricat, unde este declarată o stație, dacă peștera formează o
singură bucată conectată — fără să lipiți fișiere într-o fereastră de chat.

Comunică MCP prin **stdio**. *Gazda* (Claude Code, LM Studio, o punte Ollama) îl pornește ca proces
copil; rularea lui manuală într-un terminal nu face nimic util.

ThIDE implementează **serverul**. Modelul se află în gazdă.

## Ce poate face

**Douăzeci și două de unelte doar-citire.** Niciuna nu modifică nimic:

| Domeniu | Unelte |
|---|---|
| Proiect | `server_info`, `workspace_info`, `load_workspace`, `list_files`, `read_file`, `project_metadata_get` |
| Corectitudine | `get_diagnostics`, `explain_diagnostic` |
| Navigare | `list_symbols`, `goto_definition`, `find_references` |
| Survey | `survey_graph`, `survey_stats`, `deps_graph`, `list_stations` |
| Note de teren | `list_todos`, `list_leads` |
| Calcul | `structural_analysis`, `convert_units`, `convert_coordinates`, `get_declination` |
| Referință | `search_thbook` |

**Douăsprezece care scriu.** Acestea sunt retrase complet de profilul `data`:

| Domeniu | Unelte |
|---|---|
| Editare | `rename_symbol`, `format_file`, `edit_file` |
| Creare | `scaffold_th2`, `scaffold_topodroid_project`, `import_survey` |
| Export | `export_gis`, `export_tables`, `generate_report` |
| Notele dumneavoastră | `project_metadata_set`, `set_lead_status` |
| Compilare | `run_build` |

`rename_symbol`, `format_file` și `edit_file` modifică fișierele survey — `edit_file` este o operație
de căutare-și-înlocuire care atinge doar textul cerut și doar când transmiteți `dryRun:false` (altfel
afișează o previzualizare). Scheletele (scaffolds) și `import_survey` creează doar fișiere noi — refuză
în loc să suprascrie. Exporturile își înlocuiesc propriul rezultat.
`project_metadata_set` și `set_lead_status` scriu un fișier auxiliar (sidecar) partajat cu IDE-ul,
niciodată un `.th`. `run_build` rulează compilatorul Therion real.

## Resurse și prompturi

Pe lângă unelte, serverul oferă **resurse** (date pe care o gazdă le poate atașa drept context, prin URI)
și **prompturi** (șabloane de sarcini gata făcute, pe care o gazdă le listează după nume). Ambele sunt
doar-citire și disponibile în oricare profil.

Resurse:

| URI | Ce este |
|---|---|
| `therion://file/{path}` | textul unui fișier din proiect (izolat în spațiul de lucru, limitat la 100 KB) |
| `therion://diagnostics` | toate diagnosticele, ca JSON-ul din `get_diagnostics` |
| `therion://stats` | totalurile proiectului + defalcare pe fiecare survey |
| `therion://graph/survey` | conectivitatea peșterii (bucăți, joncțiuni, capete închise) |
| `therion://thbook/{topic}` | ce pagină din Therion Book tratează un termen — o citare, nu textul paginii |

Prompturi — alegeți unul în gazda dumneavoastră pentru a porni o sarcină ghidată: **`audit_workspace`**,
**`fix_diagnostic`** (primește un cod de diagnostic), **`summarize_survey`**, **`prepare_release`**.
Fiecare îi spune modelului doar ce unelte să folosească și în ce ordine; încep cu uneltele doar-citire,
deci fac în continuare analize utile și sub profilul `data`.

> **Despre `search_thbook` și `therion://thbook`.** Therion Book este distribuit ca PDF, așa că acestea
> returnează o *citare* — „Therion Book v6.4.0, p.34” — nu textul paginii. Îndreaptă modelul (și pe
> dumneavoastră) spre pagina de referință; nu o reproduc.

## Profiluri

```bash
therion-mcp --profile data     # the 22 read-only tools, and nothing else
therion-mcp --profile full     # everything (the default)
```

`--profile data` este o limită de înregistrare, nu o instrucțiune. O unealtă retrasă de profil nu apare
deloc în lista de unelte a serverului, așa că nicio insistență — și nicio injecție de prompt dintr-un
fișier `.th` — nu poate face un model să o apeleze. Dacă îndreptați un model local spre un proiect la
care țineți, începeți de aici.

## Compilare

```bash
dotnet build ThIDE.sln -m:1          # -m:1 is required; parallel MSBuild runs out of memory
```

Executabilul ajunge în `src/Therion.Mcp.Server/bin/Debug/net8.0/therion-mcp` (`.exe` pe Windows).

```bash
therion-mcp --help
therion-mcp --version
therion-mcp --workspace /caves/pestera/project.thconfig
therion-mcp --workspace /caves/pestera --profile data
```

`--workspace` primește un `.thconfig`, un `.th` sau un dosar de proiect care conține exact un singur
candidat de punct de intrare. Fără el, modelul trebuie să apeleze `load_workspace` înainte ca orice
altceva să funcționeze. Calea este verificată la pornire, astfel încât o greșeală de scriere eșuează
vizibil, în loc să producă un server care răspunde la nesfârșit „no workspace loaded”.

## Claude Code

```bash
claude mcp add therion -- /abs/path/to/therion-mcp --workspace /abs/path/to/project.thconfig
```

Adăugați `--profile data` la acea linie de comandă dacă îl doriți doar-citire.

Apoi întrebați-l ceva:

> Ce nu este în regulă cu acest proiect? Folosește get_diagnostics și explică orice cod pe care nu l-aș recunoaște.

## LM Studio (≥ 0.3.17)

Editați `mcp.json` (**Program ▸ Install ▸ Edit mcp.json**):

```json
{
  "mcpServers": {
    "therion": {
      "command": "/abs/path/to/therion-mcp",
      "args": ["--workspace", "/abs/path/to/project.thconfig", "--profile", "data"]
    }
  }
}
```
Exemplu de cale a fișierului de configurare a serverelor MCP în LM Studio: `C:\Users\Z\.lmstudio\mcp.json`

Folosiți o cale absolută către executabil: LM Studio nu îl pornește printr-un shell, deci `~` și
căutările în `PATH` nu vor fi rezolvate.

## Conectarea la ThIDE *pornit*

Tot ce este descris mai sus rulează o copie **nouă, headless** a motoarelor pe fișierele de pe disc.
Dacă doriți în schimb ca asistentul să vadă și să controleze aplicația pe care o aveți deschisă —
bufferele live ale editorului, panourile, vizualizatorul 3D — îndreptați același executabil spre IDE-ul
pornit:

```bash
therion-mcp --connect
```

În acest mod `therion-mcp` nu servește propriile unelte; face o **punte** între stdio-ul gazdei și
serverul care rulează *în interiorul* ThIDE. Acel server este oprit implicit — porniți-l din
**Preferințe ▸ MCP** („Activează serverul de unelte AI din aplicație (MCP)”). Ascultă doar pe
loopback, protejat de un bearer token aleatoriu, și le scrie pe amândouă într-un fișier de descoperire
(`%AppData%/ThIDE/mcp-endpoint.json`, `~/.config/ThIDE/…` pe Linux/macOS) pe care `--connect` îl
citește automat. Îl puteți indica explicit cu `therion-mcp --connect /path/to/mcp-endpoint.json`.

Conectat astfel, primiți unelte suplimentare care au sens doar pentru o fereastră live — `get_ui_state`,
`open_file`, `run_command`, `save_all` și altele — iar citirile răspund acum despre modificările
dumneavoastră **nesalvate**, nu despre ultima salvare. Dacă asistentul are voie să *acționeze* asupra
interfeței (nu doar să o citească) este stabilit de comutatorul **„Urmează agentul”** de lângă acea
setare.

```jsonc
// LM Studio mcp.json — drive the running app instead of a headless copy
{ "mcpServers": { "therion-live": {
  "command": "/abs/path/to/therion-mcp",
  "args": ["--connect"]
} } }
```

Dacă ThIDE nu rulează cu serverul pornit, `--connect` se oprește imediat cu un mesaj în acest sens, în
loc să rămână blocat.

## Panoul Asistent încorporat

Nu aveți nevoie deloc de o gazdă externă: **Vizualizare ▸ Asistent** deschide un panou de chat în ThIDE.
Acesta comunică cu un endpoint local compatibil OpenAI — implicit serverul LM Studio
(`http://127.0.0.1:1234/v1`); schimbați endpointul, id-ul modelului și bugetul de ture din
**Preferințe ▸ MCP**. În culise, panoul este doar o altă gazdă: se conectează la același server de unelte
din aplicație descris mai sus (se oferă să îl pornească dacă este oprit), deci vede proiectul live —
inclusiv bufferele nesalvate — prin exact aceleași unelte, cu aceleași măsuri de siguranță.

Uneltele care doar citesc rulează în tăcere. Orice unealtă care scrie (o editare, un schelet, un export,
o compilare) pune conversația în pauză pe un card **Permite / Refuză** care arată apelul exact și
argumentele lui — niciun fișier nu este atins până nu permiteți, iar serverul tot afișează o
previzualizare înainte de a aplica.

În LM Studio, încărcați modelul și porniți **Developer ▸ Local Server** (portul implicit 1234); panoul nu
are nevoie de nimic altceva.

## Ce vede modelul

Fiecare unealtă răspunde cu același înveliș, astfel încât un model poate decide pe baza unui singur câmp:

```json
{ "ok": true,  "data": { … } }
{ "ok": false, "error": { "code": "workspace_not_loaded", "message": "…" } }
```

Eșecurile sunt *răspunsuri*, nu erori de protocol — un model care primește o excepție își încheie tura,
în timp ce unul care primește `{"ok":false,"error":{"code":"symbol_not_found"}}` poate încerca altceva.
Uneltele care returnează liste paginează cu `offset`/`limit` și raportează `total` plus `truncated`.

## Siguranță

- **Profiluri.** `--profile data` retrage din lista de unelte toate uneltele care scriu. Nimic de
  confirmat, nimic de refuzat.
- **Rulare de probă implicită.** `rename_symbol`, scheletele și exporturile returnează un plan și nu
  scriu nimic decât dacă transmiteți `dryRun:false`. `format_file` returnează text decât dacă transmiteți
  `write:true`.
- **Adnotări oneste.** Fiecare unealtă declară `readOnlyHint` și `destructiveHint`, iar un test verifică
  faptul că sunt adevărate — așa decide gazda dumneavoastră când să vă întrebe mai întâi.
- **Nimic nu rămâne scris pe jumătate.** O modificare care atinge mai multe fișiere este validată înainte
  ca vreunul să fie scris și anulată dacă o scriere ulterioară eșuează. Fișierele își păstrează
  codificarea, inclusiv un `.th` care declară `encoding iso-8859-1`; un caracter pe care acea codificare
  nu îl poate reprezenta oprește scrierea, în loc să devină pe tăcute `?`.
- **Crearea nu suprascrie niciodată.** Scheletele și `import_survey` refuză o țintă existentă.
- **Izolarea căilor.** Fiecare argument de tip cale este canonicalizat — legăturile simbolice și `..`
  sunt rezolvate, componentă cu componentă — și refuzat dacă iese din rădăcina spațiului de lucru.
  `load_workspace` este singura excepție: el *definește* rădăcina.
- **Injecția de prompt este reală.** Un fișier `.th` de la un străin poate conține text adresat
  modelului, nu dumneavoastră. Izolarea este cea care împiedică un astfel de fișier să citească `~/.ssh`;
  profilul `data` este cel care îl împiedică să scrie orice.
- **Limite ale rezultatelor.** Conținutul text are implicit maximum 100 KB (`maxBytes`, plafon absolut
  1 MB); listele au implicit 200 de intrări (`limit`, plafon 2000). Modelele locale au ferestre de
  context mici.

## Depanare

**Gazda nu afișează nicio unealtă.** Rulați mai întâi manual `therion-mcp --version`. Dacă funcționează,
verificați că gazda transmite o cale absolută către executabil.

**`get_declination` răspunde `model_unavailable`.** ThIDE nu include `WMM.COF` — este o descărcare NOAA
din domeniul public. Mesajul de eroare numește cele trei căi în care caută serverul; puneți fișierul în
una dintre ele.

**`run_build` răspunde `tool_not_found`.** Therion nu este instalat sau nu se află unde caută serverul:
calea configurată explicit, apoi locațiile de instalare obișnuite, apoi `PATH`.

**O unealtă care scrie lipsește din listă.** Ați pornit serverul cu `--profile data`. Profilul își face
treaba.

**`--connect` răspunde „no running ThIDE MCP server found”.** Fie ThIDE nu rulează, fie serverul din
aplicație este oprit — porniți-l din **Preferințe ▸ MCP**. Dacă fișierul de descoperire se află într-un
loc nestandard, transmiteți calea lui: `therion-mcp --connect /path/to/mcp-endpoint.json`.

**O unealtă de editare returnează `file_dirty` (în aplicație).** Fișierul pe care l-ar modifica este
deschis în ThIDE cu modificări nesalvate, deci scrierea lui v-ar suprascrie sau ramifica munca. Salvați
(sau închideți) acel fișier, apoi reîncercați.

**Totul răspunde `workspace_not_loaded`.** Serverul a fost pornit fără `--workspace`. Fie îl adăugați,
fie cereți modelului să apeleze mai întâi `load_workspace`.

**Diagnosticarea unei sesiuni.** Serverul scrie în jurnal pe stderr de la nivelul `Warning` în sus;
gazda îl afișează. Setați `Logging__LogLevel__Default=Information` în mediul serverului pentru a vedea
fiecare apel de unealtă.

## Note de proiectare

- Arhitectura, modurile de găzduire și modelul de securitate: `.claude/mcp-integration/02-server-architecture.md`
- Contractul live al uneltelor: `.claude/mcp-integration/TOOL-REGISTRY.md`
