# Funcții

> [Index documentație](../README.md) · [README-ul proiectului](../../README.md)

## Inteligența limbajului
- Parsere per fișier pentru `.th`, `.th2`, `.thconfig`, `.xvi`, cu AST-uri tipizate; **tolerante implicit**, modul strict se activează la cerere.
- Model tipizat al poligonalei (`units`, `calibrate`, `declination`, `sd`, `grade`, `cs`, `extend`, `break`, `walls`, `station`, `mark`, …), `thconfig` tipizat (`select` / `export` / `maps` / `layout`) și un model `.th2` detaliat (tipuri și opțiuni de puncte/linii/arii).
- Analiza `layout … endlayout` (inclusiv blocurile opace `code … endcode`), directivele de set de simboluri / legendă și validarea sistemelor de coordonate EPSG/denumite.
- **Graf de conectivitate** între fișiere, verificări ale convențiilor de denumire și **reguli semantice extensibile prin pluginuri** (`rules.json` + DLL-uri plugin).
- Un panou **Diagnostice** cu coduri de diagnostic stabile — vedeți [diagnostics.md](../diagnostics.md) și [layout-and-embedded-code.md](../layout-and-embedded-code.md).

## Editare și navigare
- Editor bazat pe AvaloniaEdit: închidere automată a parantezelor/ghilimelelor, semne de carte, istoric înapoi/înainte al cursorului, comutare comentariu și majuscule/minuscule, evidențiere scurtă la salt la definiție, operații pe linii, inserare dată/echipă.
- **Plierea codului** pentru blocurile Therion și pentru blocurile pliabile **`#@region … #@endregion`** — un strat de directive ale aplicației care se află în interiorul comentariilor Therion (invizibil pentru Therion). *Include în regiune* (`Ctrl+Shift+R`) încadrează o selecție. Vedeți [directives.md](../directives.md).
- **Exploratorul spațiului de lucru** (arbore imbricat de source/input), **Navigator obiecte** (stații / vize / schițe / hărți / referințe XVI și tabele de entități) și un **Contur** al poligonalelor și al stațiilor lor.
- **Paletă de comenzi** (`Ctrl+Shift+P`), deschidere rapidă și salt la simbol.
- **Hartă relațională** — o diagramă interactivă a relațiilor dintre obiecte (topografii, schițe, hărți).
- Căutare / înlocuire și căutare / înlocuire **în fișiere**; drag-and-drop și argumente de fișier „deschide cu”.

## Compilare și vizualizare
- **Flux de compilare Therion** cu ieșire în timp real, anulare și diagnostice pe care se poate face clic; un panou **Fișiere generate** cu acțiuni pentru fiecare fișier de ieșire și excepții de la deschiderea automată.
- **Fragmente de cod cu câmpuri și variabile** (dată, echipa recentă, ultima linie de date, stația următoare), fișiere `snippets.json` ale utilizatorului și ale proiectului, fragmente de thconfig (**Export rapid** ▸ **Salvează ca fragment…**) și **Fișier** ▸ **Fișier nou…** din șabloane, cu o linie `input` opțională. Fragmentele de cod sunt oferite și prin LSP.
- **Nou din șablon** — un dosar de topografie creat dintr-un șablon (o singură peșteră, zonă carstică, expediție; importul dosarului sau arhivei .zip a unui club; `therion-cli new`) și **Ieșire nouă** — un fișier de ieșire precompletat din cea mai recentă ieșire și legat în proiect cu `input` + `equate`.
- **Automatizare**: hook-uri pentru deschiderea spațiului de lucru, deschiderea/salvarea fișierelor și pornirea/încheierea/eșecul compilării (cu variabile de mediu `THIDE_*` și ieșirea într-un panou **Automatizare**), fișierele de proiect `.thide/hooks.json` și `.thide/tasks.json` (problem matchers către Diagnostice), condiționate de încrederea în spațiul de lucru, **Compilează mai multe** (matrice thconfig × export) și `therion-cli build`.
- **Corectează toate**: în panoul Diagnostice, *Corectează* / *Corectează toate „<code>” din spațiul de lucru…* cu un lot previzualizat; **Adaugă scrap-urile nereferite în hărți**, **Sugerează echivalări** (→ **Leagă un fișier de topografie**), **Formatează toate fișierele**, **Normalizează proiectul** (terminații de linie, UTF-8, `input`-uri sortate) și **Renumerotează stațiile** (prefix nou sau decalare a numerelor pentru o topografie întreagă, cu toate referințele incluse).
- **Lipește ca date de vize**: tabele din foi de calcul, CSV, PocketTopo sau DistoX → un bloc Therion `data`, cu detectarea coloanelor și unităților, stații denumite pentru descărcările brute (vizele repetate → un singur tronson) și verificări pe fiecare linie; fișierele `.csv`/`.tsv` se importă ca topografie nouă.
- **Dosarul de intrare** (opțional): exporturile de topografie care ajung în `incoming/` sunt oferite pentru import după ce sunt complete și sunt direcționate către **Leagă un fișier de topografie** sau către importatoarele Survex/Compass/GPX; plasarea acestor fișiere prin tragere pe fereastră are același efect.
- **Macrouri**: scenarii cu previzualizare, apoi aplicare, construite din uneltele MCP și rulate fără model — **Unelte** ▸ **Macrouri**, sarcini, hook-uri sau `therion-mcp macro run … [--apply]`; o singură unealtă dintr-un shell cu `therion-mcp call`. Prompturile MCP `audit_workspace` și `prepare_release` trimit mai întâi la macroul corespunzător.
- **Fără interfață grafică**: `therion-cli format --check` (CI / pre-commit), `import` (Survex, Compass, GPX, tabele de vize) și `report` (raport topografic HTML).
- **Presetare LM Studio** (`docs/assistant/thide.preset.json` sau `therion-mcp skills preset`): pachetul de abilități pentru modelele locale.
- **Leagă un fișier de topografie** — alegeți o stație a unui fișier `.th` independent și stația din proiect din care acesta continuă; ThIDE scrie `input` + `equate` (nume relative la topografia de destinație) în fișierul sugerat, copiază fișierul și schițele lui în proiect, verifică duplicatele și previzualizează rezultatul. Vedeți [ghidul utilizatorului, capitolul 24](user-guide/24-linking-surveys.md).
- **Previzualizare 2D în timp real** a poligonalelor — cu echivalările unite și ancorată în punctele fixe, colorare după topografie/fișier/componentă, marcaje de joncțiune pe care se poate face clic și vizibilitate per componentă.
- **Vizualizator hărți** — un afișaj integrat pentru hărțile compilate **PNG / SVG / PDF** (zoom, potrivire la fereastră, navigare între paginile PDF, ecran complet/fereastră separată). Listează ieșirile `export map` ale thconfig-ului activ într-un selector și poate afișa automat prima hartă după o compilare.
- **Vizualizator 3D încorporat** — afișează modelele compilate `.lox` / `.3d` prin [CaveView.js](https://github.com/apgeo/CaveView.js) (fork-ul îmbunătățit folosit de SilexGIS) într-o vizualizare web nativă (fără Chromium inclus); comutatoare pentru orientare/cameră/elemente, suprapuneri de colorare, ecran complet, **clic pe o stație → salt la sursa `.th`**, **Arată în 3D** cu o cruce de vizare și o vedere de aproape opțională (inclusiv radialele) și **export ca pagină web independentă**. Vedeți [3d-viewer.md](../3d-viewer.md).
- Lansatoare **Loch / Aven** și căutarea paginilor PDF din **thbook**.
- ⚠️ **Randări de animații în Blender** *(experimental — în lucru)* — transformați un model compilat într-un videoclip cu rotire în jurul peșterii, o coborâre elicoidală, un zbor prin galerie, imagini statice sau o trecere în stil hartă: ThIDE transformă modelul într-o rețea de poligoane, scrie un script Python pentru Blender și rulează fără interfață grafică propria dumneavoastră instalare Blender. Vedeți [user-guide/23-blender-animation.md](user-guide/23-blender-animation.md).

## Analiză topografică și note
- Statistici de proiect: defalcarea lungimii (suprafață / subteran / duplicat / radiale), diferența de nivel cu stațiile cea mai înaltă/cea mai joasă, extinderea orizontală; lungimea pe topografie și pe dată; membrii echipei și ieșirile; punctele fixe; verificări ale calității datelor.
- File pentru grafice, echipă, intrări și calitate; export în **CSV / Markdown / HTML / LaTeX**; **raport topografic HTML** la cerere și **export în tabel** al stațiilor/vizelor.
- Registru de **piste de explorare** și suprapunere pe hartă (marcaje de continuare, comentarii QM / lead / `?`, fundături), cu ciclu de viață per proiect.
- **Agregator TODO / QM** și un fișier însoțitor de **metadate** per proiect.

> Analizele sunt calculate **în aplicație** din modelul semantic, așa că funcționează în timp real, fără compilare. Sunt de *calitate de previzualizare* (fără compensarea buclelor) — Therion rămâne sursa de referință pentru lungimile compensate.

## Import / export și GIS
- **Import:** Survex (`.svx`), Compass (`.dat`), DEM (ESRI ASCII → `surface`) și puncte GPX (→ `fix`).
- **Export:** intrări și puncte fixe în **KML / GeoJSON / GPX / CSV**.
- **Generarea unui proiect** în jurul unui fișier de topografie `.th` simplu (de exemplu din TopoDroid): arborele de dosare, topografia de legătură, georeferențierea și aspectul (layout) opționale, plus un thconfig cu exporturile alese.
- Generarea de fișiere `.th2` (schiță nouă; schiță din imagine / `.xvi`) și transformări de coordonate (WGS84 ↔ UTM, lat-long).

## Utilitare
- Convertor de coordonate, convertor de unități și un calculator de **declinație magnetică** (sinteză în armonici sferice WMM/IGRF).

## Interfața IDE, performanță și fiabilitate
- Interfață **andocabilă** în stil VS, cu păstrarea aranjamentului și a ferestrelor flotante; o pagină de start **Bun venit** (nou/deschide, thconfig-uri/spații de lucru/fișiere recente, linkuri către unelte externe); **notificări** cu un clopoțel pentru istoric; fixarea filelor / închiderea celorlalte / redeschiderea filelor închise; salvare automată; elemente recente care pot fi fixate și șterse.
- Virtualizarea arborilor, indexare în fundal, un **index de simboluri persistent**, protecții pentru fișierele mari și internarea șirurilor pentru proiectele mari.
- **Recuperare după blocare** / mod sigur cu bufferele nesalvate salvate automat; telemetrie opțională, **doar locală** (dezactivată implicit).
- Multe funcții mai costisitoare pot fi **activate/dezactivate** (**Preferințe** ▸ **Performanță** / **Extensii** / **Vizualizare**), astfel încât proiectele mari să rămână fluide.

## Încredere și siguranță
- Protecție la deschiderea fișierelor binare / supradimensionate, confirmare la ștergere (coș de gunoi vs. definitiv) și un banner pentru modificări externe, cu diff alăturat.

## Extensibilitate și automatizare
- **`therion-cli`** — `validate` / `lint` / `format` / `stats` / `deps` / `gis` / `dump-ast` / `list-stations` fără interfață grafică, plus `new` (proiect dintr-un șablon) și `build` (matrice thconfig × export). Vedeți [Utilizare](../usage.md#command-line-tools).
- **`therion-lsp`** — un Language Server independent de editor (diagnostice prin stdio). Vedeți [lsp.md](../lsp.md).
- **Script hooks** (vedeți Automatizare mai sus) și un **încărcător de pluginuri de reguli semantice** (DLL-uri `ISemanticRule`). Vedeți [plugins.md](../plugins.md).
- ⚠️ **Asistenți AI prin MCP** *(experimental — în lucru)* — un panou **Asistent** integrat, condus de un model local, plus `therion-mcp`, care expune motoarele de parsare, semantică și spațiu de lucru ale ThIDE prin [Model Context Protocol](https://modelcontextprotocol.io), astfel încât un asistent să poată răspunde la întrebări despre un proiect fără să lipiți fișiere într-un chat. Implicit doar citire, cu unelte de scriere opționale, protejate de o etapă de aprobare (previzualizările rulează fără să vă întrebe). Instrucțiunile asistentului includ un **pachet de abilități** (cel integrat, notele dumneavoastră, cel al proiectului; `therion-mcp skills print` pentru alte gazde), iar o **politică de date** aleasă la prima utilizare decide ce ajunge la model: totul, nimic din proiect sau rezultate cu valorile golite. Vedeți [mcp-host-setup.md](../mcp-host-setup.md) și [user-guide/20-extensibility.md](user-guide/20-extensibility.md#asistenți-ai-mcp).
