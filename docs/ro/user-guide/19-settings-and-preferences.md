# 19. Setări și preferințe

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Deschideți **Setări** din bara de meniu (sau *Preferințe…* din Paleta de comenzi). Fereastra are o
**casetă de căutare** (*Caută în setări…*) — tastați pentru a sări la orice opțiune. Secțiunile din
lateral grupează opțiunile; această pagină le parcurge pe fiecare. **Resetează tot la valorile
implicite** se află în partea de jos.

Majoritatea funcțiilor sensibile la performanță au aici un comutator explicit de activare/dezactivare,
astfel încât să puteți regla ThIDE pentru un proiect mic și ordonat sau pentru un sistem uriaș cu mai
multe peșteri.

## General

| Setare | Note |
|---|---|
| **Redeschide fișierele din sesiunea anterioară la pornire** | Restaurează filele deschise. |
| **Afișează pagina de bun venit la pornire** | Pagina de start [Bun venit](03-getting-started.md). |
| **Limbă** | English / Română, aplicată imediat. |
| **Salvare automată** | Dezactivată · **După o întârziere** (setați secundele) · **La pierderea focalizării**. |
| **Înregistrează utilizarea anonimă & rapoartele de eroare** | **Dezactivat implicit.** Când este activat, evenimentele sunt scrise în `%AppData%/ThIDE/telemetry` și **nu părăsesc niciodată calculatorul dumneavoastră** — totul este doar local. |

## Temă și culori

- **Tema aplicației:** Implicit sistem · Luminoasă · **Întunecată** (adaptează atât controalele, cât și
  culorile de sintaxă ale editorului).
- **Folosește culori de sintaxă personalizate** — înlocuiește paleta cu propriile valori `#RRGGBB` pentru
  cuvânt-cheie, identificator, număr, șir, comentariu, opțiune, punctuație (fiecare cu un eșantion live).

## Editor

| Setare | Note |
|---|---|
| **Dimensiune font**, **Dimensiune indentare** | Textul editorului și indentarea. |
| **Arată numerele de linie**, **Evidențiază linia curentă** | Ajutoare de afișare. |
| **Convertește taburile în spații** | Stilul de indentare. |
| **Formatează documentul la salvare** | Reindentează după imbricarea blocurilor la fiecare salvare. |
| **Arată o previzualizare înainte de a aplica redenumirea unui simbol** | Lista de confirmare a redenumirii cu F2. |
| **Validează la tastare (fișier + întregul proiect)** | Revalidare profundă la o pauză în tastare, nu doar la salvare. **Dezactivat implicit** — poate fi costisitor în spațiile de lucru mari. |
| **Necesită dublu-clic pentru a merge la definiție** | Activat: un singur clic plasează cursorul, dublu clic navighează. Dezactivat: un singur clic navighează ca un hyperlink. Ctrl+clic navighează întotdeauna. |

## Funcții editor

O secțiune proprie în bara laterală: câte o casetă de bifare pentru fiecare îmbunătățire a editorului
(completare, ajutor pentru semnătură, minihartă, antete fixe, breadcrumbs, peek, împărțire/diff,
eșantioane de culoare, spații albe, Enter inteligent, fragmente…). Dezactivați orice vi se pare deranjant.
**O funcție afișată în gri aici a fost dezactivată la build** și nu poate fi activată din această
fereastră.

## Performanță

Protecții care mențin peșterile mari responsive:

| Setare | Efect |
|---|---|
| **Linii maxime de evidențiere / Dimensiune maximă de evidențiere (KB)** | Peste această limită, evidențierea sintaxei și tooltip-urile la trecerea mouse-ului sunt dezactivate pentru fișier. |
| **Linii maxime de analiză / Dimensiune maximă de analiză (KB)** | Peste această limită, un fișier nu este analizat și rămâne în afara grafului de obiecte. |
| **Stații maxime în căutare** | Limitează câte stații listează *Salt la simbol*. |
| **Timp maxim de încărcare la pornire (secunde)** | Bugetul pentru redeschiderea fișierelor din ultima sesiune; restul sunt omise (și înregistrate în jurnal). `0` = fără limită. |
| **Reîncarcă fișierele deschise modificate în afara editorului** | Preluare automată a editărilor externe. |
| **Actualizează graful de obiecte când fișierele se schimbă pe disc** | Menține analiza la zi cu modificările de pe disc. |

Fișierele individuale care ating o limită afișează un banner cu **Analizează oricum / Evidențiază
oricum**, ca să puteți trece peste limită de la caz la caz.

## Proiect

- **Salt la fișier (Ctrl+P): caută doar fișierele conectate la thconfig-ul activ** — când este
  dezactivat, Ctrl+P caută în *toate* fișierele Therion din folderul spațiului de lucru (istoricul este
  inclus întotdeauna). Vedeți [Navigare și căutare](08-navigation-and-search.md#deschiderea-fișierelor-și-comutarea-între-ele).

## Compilare și ieșire

| Setare | Note |
|---|---|
| **După o compilare reușită, deschide:** `.lox` / `.3d` / `.pdf` | Ce se deschide automat după o compilare; **Deschide fiecare ieșire corespunzătoare** sau doar prima. |
| **Compilează la salvare** | Recompilare în fundal, cu temporizare, a thconfig-ului activ la salvare. Dezactivat implicit. |
| **Creează dosarele de ieșire lipsă înainte de compilare** | Evită eșecul Therion din cauza unui director absent. Activat implicit. |
| **Salvează automat fișierele modificate înainte de compilare** | Sare peste solicitarea de salvare. Dezactivat implicit. |
| **Recalculează registrul de indicii pe măsură ce tastați** | Reîmprospătare live a [Pistelor](14-leads-notes-and-metadata.md) din conținutul nesalvat. Activat implicit; dezactivați-l în spațiile de lucru uriașe. |

## Vizualizare

O singură secțiune cu trei grupuri — panourile de previzualizare, analiza și un comutator de diagnostic.

**Panouri de previzualizare (Preview panels).** Activați/dezactivați previzualizările doar-citire;
dezactivarea uneia ascunde panoul ei *și* intrarea ei de meniu (resetați aranjamentul sau reporniți
pentru a închide un panou deja deschis):

- **Previzualizare principală a liniei centrale** (+ schița ei în plan/elevație).
- **Vizualizator de hărți în aplicație (PNG / SVG / PDF)** — plus **Afișează automat harta randată după
  o compilare** și **Afișează automat prima hartă la deschiderea spațiului de lucru**.
- **Vizualizator 3D încorporat** (CaveView.js) — plus **Afișează automat modelul 3D (.lox / .3d) după o
  compilare**.
- **Modul Geologie structurală**.
- **Deschide PDF-urile pe care faceți clic în vizualizatorul intern**.

Vedeți [Vizualizatoarele](11-viewers.md) și [Geologie structurală](15-structural-geology.md).

**Analiză topografică.** Dezactivați oricare dintre acestea în spațiile de lucru foarte mari, pentru a
evita munca de la fiecare reconstruire a grafului:

- **Statistici de proiect, grafice, echipă, intrări & calitatea datelor** (filele din [Prezentare generală](13-survey-overview-and-analytics.md)).
- **Filele de entități din Navigator obiecte** (topografii, puncte fixe, echivalări, schițe, hărți,
  puncte, linii, suprafețe).
- **Scanare comentarii TODO / FIXME / QM**.
- **Scanare manager media** (`.xvi` referite + fișiere orfane de pe disc).

**Diagnostice.**

- **Un fix local (fără cs) ancorează o topografie deconectată** — când este **dezactivat** (implicit), o
  porțiune ancorată doar printr-un simplu `fix 0 0 0` (fără sistem de coordonate) generează în
  continuare un avertisment (`TH_SEM_015`); activați-l pentru a trata orice fix ca ancorare și a
  suprima acel avertisment. Vedeți [Diagnostice](09-diagnostics-and-validation.md).

## Unelte externe

Aici indicați lui ThIDE unde se află Therion, Loch, Aven, Mapiah, Survex. Fiecare rând afișează
**Unealtă · Detectat · Versiune · Sursă · Suprascriere · Rezultat**. Setați o cale în **Suprascriere** și
verificați-o cu **Testează**. Vedeți
[Instalare](02-installation-and-setup.md#4-indicați-i-lui-thide-uneltele-externe).

## Asocieri de fișiere

Faceți ca tipurile de fișiere Therion să se deschidă în ThIDE (per utilizator, fără drepturi de
administrator). Fiecare rând afișează **Tip · Descriere · Stare**; **Asociază** / **Elimină** pentru
fiecare tip sau **Asociază toate**. Note specifice platformei pe pagină și în
[docs/file-association.md](../file-association.md).

## Extensii

Cârlige pentru utilizatori avansați (toate dezactivate implicit; vedeți [Extensibilitate](20-extensibility.md)):

- **Activează cârlige de script** — rulează o comandă **la deschidere / salvare / compilare** (folosiți
  `{file}` pentru cale).
- **Încarcă pluginuri** — reguli semantice personalizate (DLL-uri `ISemanticRule`) din
  `%AppData%/ThIDE/plugins`, încărcate la pornire.

### Server de unelte AI (MCP) — *experimental*

- **Activează serverul de unelte AI din aplicație (MCP)** — permite unui asistent AI local să acceseze
  proiectul deschis prin Model Context Protocol. Ascultă **doar pe 127.0.0.1** (loopback), pe un port
  aleatoriu, protejat de un bearer token; portul și tokenul sunt scrise în
  `%AppData%/ThIDE/mcp-endpoint.json`. **Dezactivat implicit.** Vedeți
  [docs/mcp-host-setup.md](../mcp-host-setup.md).
- **Urmează agentul (permite uneltelor AI să controleze fereastra)** — când este activat, asistentul
  poate deschide fișiere, focaliza panouri, rula comenzi, salva și schimba aranjamentul. Când este
  dezactivat, poate în continuare să *citească* proiectul și să răspundă la întrebări, dar nu poate
  atinge interfața. Activat implicit (odată ce serverul însuși este activat).

### Panoul Asistent — *experimental*

Modelul local, compatibil OpenAI, cu care comunică [panoul Asistent](20-extensibility.md#asistenți-ai-mcp)
integrat (implicit LM Studio). **Panoul are nevoie și de serverul de unelte AI de mai sus** pentru a
accesa proiectul.

| Setare | Note |
|---|---|
| **Endpoint** | de exemplu `http://127.0.0.1:1234/v1`. |
| **Id model** | de exemplu `qwen3-coder-30b-a3b-instruct`. |
| **Buget de tururi de instrumente** | Câte schimburi cu instrumentele poate face o singură întrebare (1–50). |
| **Răspunde în limbaj natural după folosirea instrumentelor** | Împiedică modelul să vă lipească înapoi JSON brut. |
| **Produ întotdeauna un răspuns scris** | Sintetizează unul chiar și când modelul nu returnează niciunul. |
| **Transmite răspunsul cu un indicator de progres în timp real** | |
| **Cere modelului să pună sursa Therion propusă în blocuri de cod** | Vă oferă butoanele **Copiază / Inserează / Înlocuiește** pe bloc. |
| **Context proiect** | Cât din rezumatul proiectului primește modelul la început: **Niciunul (doar unelte)** · **Fișă (rezumat compact)** · **Pachet (rezumat bogat)**. Cifrele sunt un instantaneu, așa că modelului i se cere să verifice cu instrumentele. |

**CLI**-ul (`therion-cli`) și **LSP**-ul (`therion-lsp`) sunt executabile separate, nu setări de aici.

## Depanare

Comutatoare de nivel scăzut pentru **vizualizările web încorporate** ([vizualizatorul 3D](11-viewers.md#vizualizator-3d)
și graficele structurale). **Modificările intră în vigoare după repornirea ThIDE.** Aveți nevoie de
această secțiune doar dacă un panou web nu funcționează corect — vedeți
[Depanare și întrebări frecvente](22-troubleshooting-and-faq.md).

**Motor web încorporat (Linux / WebKitGTK):**

| Setare | Note |
|---|---|
| **Dezactivează redarea DMA-BUF** | Rezolvă un **panou web gol (alb/negru)** pe multe sisteme Linux (drivere NVIDIA, Wayland). Doar Linux; **activat implicit**. Ignorat dacă `WEBKIT_DISABLE_DMABUF_RENDERER` este deja setat în mediul dumneavoastră. |
| **Dezactivează compunerea accelerată (ultimă soluție)** | Forțează randarea software când panoul este *în continuare* gol. **Dezactivează WebGL, deci vizualizatorul 3D nu poate randa** — este destinat graficelor structurale 2D. Doar Linux; dezactivat implicit. |
| **Vizualizare web offscreen (experimental)** | Randează conținutul web prin compozitorul aplicației, în loc să încorporeze o fereastră nativă. Poate rezolva probleme de încorporare, poate afecta gestionarea intrărilor. Doar Linux; dezactivat implicit. |

**Toate platformele (All platforms):**

- **Activează instrumentele de dezvoltare web** — permite deschiderea inspectorului/DevTools al
  motorului din panoul vizualizatorului 3D și prin clic dreapta ▸ **Inspectează**. Activat implicit.

## Scurtături tastatură

Reasociați orice comandă: faceți clic pe câmpul **Combinație** al acesteia și apăsați combinația de
taste. Coloanele afișează **Comandă · Combinație · Implicit**; **Resetează** un rând sau pe toate.
Comenzile livrate **fără** o combinație implicită sunt listate și ele aici, deci aici le asociați. Valorile
implicite curente sunt listate în [Scurtături de tastatură](21-keyboard-shortcuts.md).

---

Următorul: [Extensibilitate (CLI, LSP, asistenți AI, automatizarea interfeței, pluginuri, cârlige) →](20-extensibility.md)
