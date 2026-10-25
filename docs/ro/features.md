# Funcții

> [Index documentație](README.md) · [README-ul proiectului](../../README.md)

## Inteligența limbajului
- Parsere per fișier pentru `.th`, `.th2`, `.thconfig`, `.xvi`, cu AST-uri tipizate; **tolerante implicit**, modul strict se activează opțional.
- Model tipizat de linie centrală (`units`, `calibrate`, `declination`, `sd`, `grade`, `cs`, `extend`, `break`, `walls`, `station`, `mark`, …), `thconfig` tipizat (`select` / `export` / `maps` / `layout`) și un model `.th2` detaliat (tipuri și opțiuni de puncte/linii/arii).
- Analiza `layout … endlayout` (inclusiv blocurile opace `code … endcode`), directivele de set de simboluri / legendă și validarea sistemelor de coordonate EPSG/denumite.
- **Graf de conectivitate** între fișiere, verificări ale convențiilor de denumire și **reguli semantice extensibile prin plugin-uri** (`rules.json` + DLL-uri plugin).
- Țintele `input` / `source` se rezolvă la fel ca în Therion: numele literal, apoi `.th`, apoi `.th2` — astfel un `input cave` fără extensie găsește `cave.th` (sau `cave.th2`) atât în editor, cât și în spațiul de lucru și în verificarea *Fișier negăsit (File not found)*.
- Un panou **Diagnostice** cu coduri de diagnostic stabile — vedeți [diagnostics.md](diagnostics.md) și [layout-and-embedded-code.md](layout-and-embedded-code.md).

## Editare și navigare
- Editor bazat pe AvaloniaEdit: închidere automată a parantezelor/ghilimelelor, semne de carte, istoric înapoi/înainte al cursorului, comutare comentariu și majuscule/minuscule, evidențiere scurtă la salt la definiție, operații pe linii, inserare dată/echipă.
- **Pliere de cod** (code folding) pentru blocurile Therion și pentru blocurile pliabile **`#@region … #@endregion`** — un strat de directive de aplicație care trăiește în interiorul comentariilor Therion (invizibil pentru Therion). *Include în regiune* (`Ctrl+Shift+R`) încadrează o selecție. Vedeți [directives.md](directives.md).
- **Explorator proiect** (arbore imbricat de source/input), **Navigator obiecte** (stații / vizări / scrap-uri / hărți / referințe XVI și tabele de entități) și un **Contur** al liniilor centrale și al stațiilor lor.
- **Paletă de comenzi** (`Ctrl+Shift+P`), deschidere rapidă și salt la simbol.
- **Hartă relațională** — o diagramă interactivă a relațiilor dintre obiecte (survey-uri, scrap-uri, hărți).
- Căutare / înlocuire și căutare / înlocuire **în fișiere**; drag-and-drop și argumente de fișier „deschide cu” (căi relative sau absolute, pe orice platformă).

## Compilare și vizualizare
- **Flux de compilare Therion** cu ieșire în timp real, anulare și diagnostice pe care se poate face clic; un panou **Fișiere generate** cu acțiuni pentru fiecare ieșire și suprascrieri ale deschiderii automate.
- **Previzualizare 2D în timp real** a liniilor centrale — cu stații unite prin equate și ancorate prin fix, colorare după survey/fișier/componentă, marcaje de joncțiune pe care se poate face clic și vizibilitate per componentă.
- **Vizualizator hărți** — un randor intern pentru hărțile compilate **PNG / SVG / PDF** (zoom, potrivire la fereastră, navigare între paginile PDF, ecran complet/fereastră separată). Listează ieșirile `export map` ale thconfig-ului activ într-un selector și poate afișa automat prima hartă după o compilare.
- **Vizualizator 3D încorporat** — afișează modelele compilate `.lox` / `.3d` prin [CaveView.js](https://github.com/aardgoose/CaveView.js) într-o vizualizare web nativă (fără Chromium inclus); comutatoare pentru orientare/cameră/elemente, suprapuneri de colorare, ecran complet și **clic pe o stație → salt la sursa `.th`**. Vedeți [3d-viewer.md](3d-viewer.md).
- Lansatoare **Loch / Aven** și căutarea paginilor PDF din **thbook**.
- ⚠️ **Randări de animații Blender** *(experimental — în lucru)* — transformați un model compilat într-un videoclip de survol, o coborâre elicoidală, un zbor prin peșteră, imagini statice sau o trecere pe hartă: ThIDE creează rețeaua de poligoane a modelului, scrie un script Python pentru Blender și rulează fără interfață propria dumneavoastră instalare Blender. Vedeți [user-guide/23-blender-animation.md](user-guide/23-blender-animation.md).

## Analiză topografică și note
- Statistici de proiect: defalcarea lungimii (suprafață / subteran / duplicat / splay), denivelare cu stațiile cea mai înaltă/cea mai joasă, extindere orizontală; lungime pe survey și pe dată; membri ai echipei și ture; puncte fixe; verificări de calitate a datelor.
- File de grafice, echipă, intrări și calitate; export în **CSV / Markdown / HTML / LaTeX**; **raport HTML de survey** la cerere și **export în tabel** al stațiilor/vizărilor.
- Registru de **piste de explorare** și suprapunere pe hartă (marcaje de continuare, comentarii QM / lead / `?`, fundături), cu ciclu de viață per proiect.
- **Agregator TODO / QM** și un fișier însoțitor de **metadate** per proiect.

> Analizele sunt calculate **în aplicație** din modelul semantic, așa că funcționează în timp real, fără compilare. Sunt de *calitate de previzualizare* (fără compensarea buclelor) — Therion rămâne sursa de adevăr pentru lungimile compensate.

## Import / export și GIS
- **Import:** Survex (`.svx`), Compass (`.dat`), DEM (ESRI ASCII → `surface`) și puncte GPX (→ `fix`).
- **Export:** intrări și puncte fixe în **KML / GeoJSON / GPX / CSV**.
- Schelete `.th2` (scrap nou; schiță din imagine / `.xvi`) și transformări de coordonate (WGS84 ↔ UTM, lat-long).

## Utilitare
- Convertor de coordonate, convertor de unități și un calculator de **declinație magnetică** (sinteză în armonici sferice WMM/IGRF).

## Interfața IDE, performanță și fiabilitate
- Interfață **andocabilă** în stil VS, cu păstrarea aranjamentului și a ferestrelor flotante; o pagină de start **Bun venit** (nou/deschide, thconfig-uri/spații de lucru/fișiere recente, linkuri către unelte externe); **notificări** toast cu un clopoțel de istoric; fixare tab / închide celelalte / redeschide tab-ul închis; salvare automată; elemente recente fixabile și ștergibile.
- Virtualizarea arborilor, indexare în fundal, un **index de simboluri persistent**, protecții pentru fișiere mari și internarea șirurilor pentru proiecte mari.
- **Recuperare după blocare** / mod sigur cu buffere nesalvate salvate automat; telemetrie opțională, **doar locală** (dezactivată implicit).
- Multe funcții mai costisitoare pot fi **activate/dezactivate** (**Preferințe** ▸ **Performanță** / **Extensii** / **Vizualizare**), astfel încât proiectele mari să rămână responsive.

## Accesibilitate
- Controalele interactive au un **automation id** stabil (`Toolbar.Compile`, `Menu.File.OpenFile`, `Viewer3D.ResetView`, …), iar butoanele doar cu pictogramă au un **nume accesibil localizat**, astfel încât cititoarele de ecran anunță ce face un buton, nu tipul lui.

## Încredere și siguranță
- Protecție la deschiderea fișierelor binare / supradimensionate, confirmare la ștergere (coș vs. definitiv) și un banner pentru modificări externe, cu diff alăturat.

## Extensibilitate și automatizare
- **`therion-cli`** — `validate` / `lint` / `format` / `stats` / `deps` / `gis` / `dump-ast` / `list-stations` fără interfață grafică. Vedeți [Utilizare](usage.md#unelte-de-linie-de-comandă).
- **`therion-lsp`** — un Language Server independent de editor (diagnostice prin stdio). Vedeți [lsp.md](lsp.md).
- **Script hooks** (la deschidere / salvare / compilare) și un **încărcător de plugin-uri de reguli semantice** (DLL-uri `ISemanticRule`). Vedeți [plugins.md](plugins.md).
- ⚠️ **Asistenți AI prin MCP** *(experimental — în lucru)* — un panou **Asistent** integrat, condus de un model local, plus `therion-mcp`, care expune motoarele de parsare, semantică și spațiu de lucru ale ThIDE prin [Model Context Protocol](https://modelcontextprotocol.io), astfel încât un asistent să poată răspunde la întrebări despre un proiect fără să lipiți fișiere într-un chat. Doar citire implicit, cu unelte de scriere opționale, protejate de o etapă de aprobare. Vedeți [mcp-host-setup.md](mcp-host-setup.md) și [user-guide/20-extensibility.md](user-guide/20-extensibility.md#asistenți-ai-mcp).
- **Automatizare UI** *(pentru teste și agenți de programare)* — un server opțional în aplicație (`THIDE_UI_AUTOMATION=1`, loopback + bearer token, toate platformele) care expune arborele de automatizare al ferestrei ca unelte MCP `ui_*`, și `tools/uidrive`, care controlează aplicația prin magistrala de accesibilitate Linux. Suitele end-to-end rulează pe ambele. Vedeți [dev/ui-testing.md](dev/ui-testing.md).
