# 19. Setări și preferințe

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Deschideți fereastra de setări cu **Setări → Preferințe…** (**Ctrl+,** sau *Preferințe…* din Paleta de
comenzi); **Setări → Scurtături tastatură…** o deschide direct la lista de scurtături. Fereastra are o
**casetă de căutare** (*Caută în setări…*) — tastați pentru a sări la orice opțiune. Secțiunile (paginile)
din lateral grupează opțiunile; această pagină le parcurge pe fiecare, iar restul ghidului scrie o pagină
sub forma **Setări → Vizualizare** (și **Setări → Vizualizare → Scanare manager media** pentru un control
de pe ea). Modificările se aplică cu **OK**; **Renunță** (sau Esc) le anulează.

Majoritatea funcțiilor sensibile la performanță au aici un comutator explicit de activare/dezactivare,
astfel încât să puteți regla ThIDE pentru un proiect mic și ordonat sau pentru un sistem uriaș cu mai
multe peșteri.

## General

| Setare | Note |
|---|---|
| **Redeschide fișierele din sesiunea anterioară la pornire** | Restaurează filele deschise. |
| **Afișează pagina de bun venit la pornire** | Pagina de start [Bun venit](03-getting-started.md). |
| **Limbă** | English / Română (și din **Vizualizare → Limbă**). Meniurile, panourile și mesajele își schimbă limba când apăsați OK; câteva etichete din listele deja deschise se actualizează după o repornire. |
| **Salvare automată** | Dezactivată · **După o întârziere** (setați secundele) · **La pierderea focalizării**. |
| **Înregistrează utilizarea anonimă & rapoartele de eroare** | **Dezactivat implicit.** Când este activat, evenimentele sunt scrise în `telemetry`, în dosarul de setări al ThIDE, și **nu părăsesc niciodată calculatorul dumneavoastră** — totul rămâne local. |

## Temă & culori

- **Tema aplicației:** Implicit sistem · Luminoasă · **Întunecată** (adaptează atât controalele, cât și
  culorile de sintaxă ale editorului).
- **Folosește culori de sintaxă personalizate** — înlocuiește paleta cu propriile valori `#RRGGBB`
  pentru cuvânt-cheie, identificator, număr, șir, comentariu, opțiune, punctuație (fiecare cu o mostră
  actualizată pe loc).

## Editor

| Setare | Note |
|---|---|
| **Dimensiune font**, **Dimensiune indentare** | Textul editorului și indentarea. |
| **Arată numerele de linie**, **Evidențiază linia curentă** | Ajutoare de afișare. |
| **Convertește taburile în spații** | Stilul de indentare. |
| **Formatează documentul la salvare** | Reindentează după imbricarea blocurilor la fiecare salvare. |
| **Arată o previzualizare înainte de a aplica redenumirea unui simbol** | Lista de confirmare a redenumirii cu F2. |
| **Validează la tastare (fișier + întregul proiect)** | Revalidare completă la o pauză în tastare, nu doar la salvare. **Dezactivat implicit** — poate fi costisitor în spațiile de lucru mari. |
| **Necesită dublu-clic pentru a merge la definiție** | Activat: un singur clic plasează cursorul, dublu-clicul navighează. Dezactivat: un singur clic navighează ca un hyperlink. Ctrl+clic navighează întotdeauna. |

## Funcții editor

O secțiune proprie în bara laterală: câte o casetă de bifare pentru fiecare îmbunătățire a editorului
(completare, ajutor pentru semnătură, minihartă, antete fixe, breadcrumbs, peek, împărțire/diff, mostre
de culoare, spații albe, Enter inteligent, fragmente de cod…). Dezactivați orice vi se pare deranjant.
**O funcție afișată în gri aici a fost dezactivată la compilarea aplicației** și nu poate fi activată din
această fereastră.

## Performanță

Protecții care mențin peșterile mari responsive:

| Setare | Efect |
|---|---|
| **Linii maxime de evidențiere / Dimensiune maximă de evidențiere (KB)** | Peste această limită, evidențierea sintaxei și informațiile la trecerea mouse-ului sunt dezactivate pentru fișier. |
| **Linii maxime de analiză / Dimensiune maximă de analiză (KB)** | Peste această limită, un fișier nu este analizat și rămâne în afara grafului de obiecte. |
| **Stații maxime în căutare** | Limitează câte stații listează *Salt la simbol*. |
| **Timp maxim de încărcare la pornire (secunde)** | Bugetul de timp pentru redeschiderea fișierelor din ultima sesiune; restul sunt omise (și consemnate în jurnal). `0` = fără limită. |
| **Reîncarcă fișierele deschise modificate în afara editorului** | Preia automat editările externe. |
| **Actualizează graful de obiecte când fișierele se schimbă pe disc** | Menține analiza la zi cu modificările de pe disc. |

Fișierele individuale care ating o limită afișează un banner cu **Analizează oricum / Evidențiază oricum**,
ca să puteți trece peste limită de la caz la caz.

## Spațiu de lucru

- **Salt la fișier (Ctrl+P): caută doar fișierele conectate la thconfig-ul activ** — dezactivat implicit.
  Când este dezactivat, Ctrl+P listează *toate* fișierele Therion din dosarul spațiului de lucru: întâi
  fișierele thconfig-ului activ, celelalte în gri și marcate „(not in *name*.thconfig)”. Când este
  activat, acele alte fișiere sunt omise, inclusiv cele recente și cele deschise. Vedeți
  [Navigare și căutare](08-navigation-and-search.md#deschiderea-fișierelor-și-comutarea-între-ele).

## Compilare și ieșire

| Setare | Note |
|---|---|
| **După o compilare reușită, deschide:** `.lox` / `.3d` / `.pdf` | Ce se deschide automat după o compilare; **Deschide fiecare ieșire corespunzătoare** sau doar prima. |
| **Compilează la salvare** | Compilare în fundal, cu temporizare, a thconfig-ului activ la salvare. Dezactivat implicit. |
| **Tastă rapidă de compilare la nivel de sistem** | **Doar Windows**, dezactivat implicit: Ctrl+Alt+B compilează chiar și când ThIDE nu este în prim-plan (cât timp e activat, combinația AltGr+B nu mai ajunge la celelalte programe). |
| **Creează dosarele de ieșire lipsă înainte de compilare** | Evită eșecul Therion din cauza unui director absent. Activat implicit. |
| **Salvează automat fișierele modificate înainte de compilare** | Sare peste întrebarea de salvare. Dezactivat implicit. |

## Vizualizare

O singură secțiune cu trei grupuri — panourile de previzualizare, analizele și un comutator pentru
diagnostice.

**Panouri de previzualizare.** Activați/dezactivați previzualizările doar-citire; dezactivarea uneia îi
ascunde panoul *și* intrarea din meniu (resetați aranjamentul sau reporniți pentru a închide un panou
deja deschis):

- **Previzualizare principală** (schița poligonalei în plan/profil).
- **Vizualizator de hărți în aplicație (PNG / SVG / PDF)** — plus **Afișează automat harta randată după o
  compilare** și **Afișează automat prima hartă la deschiderea spațiului de lucru**.
- **Vizualizator 3D încorporat** (CaveView.js) — plus **Afișează automat modelul 3D (.lox / .3d) după o
  compilare**.
- **Modul Geologie structurală**.
- **Deschide PDF-urile pe care faceți clic în vizualizatorul intern**.

Vedeți [Vizualizatoarele](11-viewers.md) și [Geologie structurală](15-structural-geology.md).

**Analiză topografică.** Dezactivați oricare dintre acestea în spațiile de lucru foarte mari, pentru a
evita munca făcută la fiecare reconstruire a grafului:

- **Statistici de proiect, grafice, echipă, intrări & calitatea datelor** (filele din
  [Prezentare generală](13-survey-overview-and-analytics.md)).
- **Filele de entități din Navigator obiecte** (topografii, puncte fixe, echivalări, schițe, hărți,
  puncte, linii, suprafețe).
- **Scanare comentarii TODO / FIXME / QM**.
- **Scanare manager media** (`.xvi` referențiate + fișiere orfane de pe disc).
- **Recalculează registrul de indicii pe măsură ce tastați** — reîmprospătare live a
  [pistelor](14-leads-notes-and-metadata.md) din conținutul nesalvat. Activat implicit; dezactivați-l în
  spațiile de lucru uriașe.

**Diagnostice.**

- **Un fix local (fără cs) ancorează o topografie deconectată** — când este **dezactivat** (implicit), o
  porțiune ancorată doar printr-un simplu `fix 0 0 0` (fără sistem de coordonate) primește în continuare
  un avertisment (`TH_SEM_015`); activați-l pentru a trata orice fix ca ancorare și a suprima acel
  avertisment. Vedeți [Diagnostice](09-diagnostics-and-validation.md).

## Unelte externe

Aici îi indicați lui ThIDE unde se află Therion, Loch, Aven, Mapiah, Survex. Fiecare rând afișează
**Unealtă · Detectat · Versiune · Sursă · Suprascriere · Rezultat**. Setați o cale în **Suprascriere** și
verificați-o cu **Testează**: testul rulează calea din suprascriere (și vă spune dacă acea cale nu
există), nu copia detectată. Vedeți
[Instalare](02-installation-and-setup.md#4-indicați-i-lui-thide-uneltele-externe).

## Asocieri de fișiere

Faceți ca tipurile de fișiere Therion să se deschidă în ThIDE (per utilizator, fără drepturi de
administrator). Fiecare rând afișează **Tip · Descriere · Stare**; **Asociază** / **Elimină** pentru
fiecare tip sau **Asociază toate**. Note specifice fiecărei platforme găsiți pe pagină și în
[docs/file-association.md](../../file-association.md).

## Extensii

Hook-uri pentru utilizatori avansați (toate dezactivate implicit; vedeți
[Extensibilitate](20-extensibility.md)):

- **Activează cârlige de script** — rulează o comandă **la deschidere / salvare / compilare** (folosiți
  `{file}` pentru cale).
- **Încarcă pluginuri** — reguli semantice personalizate (DLL-uri `ISemanticRule`) din dosarul `plugins`
  al dosarului de setări al ThIDE, încărcate la pornire.

### Server de unelte AI (MCP) — *experimental*

- **Activează serverul de unelte AI din aplicație (MCP)** — permite unui asistent AI local să acceseze
  proiectul deschis prin Model Context Protocol. Ascultă **doar pe 127.0.0.1** (loopback), pe un port
  aleatoriu, protejat de un bearer token; portul și tokenul sunt scrise în `mcp-endpoint.json`, în
  dosarul de setări al ThIDE. **Dezactivat implicit.** Vedeți
  [docs/mcp-host-setup.md](../../mcp-host-setup.md).
- **Urmează agentul (permite uneltelor AI să controleze fereastra)** — când este activat, asistentul poate
  deschide fișiere, focaliza panouri, rula comenzi, salva și schimba aranjamentul. Când este dezactivat,
  poate în continuare să *citească* proiectul și să răspundă la întrebări, dar nu poate atinge interfața.
  Activat implicit (odată ce serverul însuși este activat).

### Panoul Asistent — *experimental*

Modelul local, compatibil OpenAI, cu care comunică [panoul Asistent](20-extensibility.md#asistenți-ai-mcp)
integrat (implicit LM Studio). **Panoul are nevoie și de serverul de unelte AI de mai sus** pentru a
accesa proiectul.

| Setare | Note |
|---|---|
| **Endpoint** | de exemplu `http://127.0.0.1:1234/v1`. |
| **Id model** | de exemplu `qwen3-coder-30b-a3b-instruct`. |
| **Buget de tururi de instrumente** | Câte schimburi cu instrumentele poate face o singură întrebare (1–50). |
| **Răspunde în limbaj natural după folosirea instrumentelor** | Împiedică modelul să vă returneze JSON brut. |
| **Produ întotdeauna un răspuns scris** | Sintetizează unul chiar și când modelul nu returnează niciunul. |
| **Transmite răspunsul cu un indicator de progres în timp real** | |
| **Cere modelului să pună sursa Therion propusă în blocuri de cod** | Vă oferă butoanele **Copiază / Inserează / Înlocuiește** pe bloc. |
| **Contextul spațiului de lucru** | Cât din rezumatul proiectului primește modelul la început: **Niciunul (doar unelte)** · **Fișă (rezumat compact)** · **Pachet (rezumat bogat)**. Cifrele sunt un instantaneu, așa că modelului i se cere să le verifice cu instrumentele. |
| **Adaugă pachetul de cunoștințe la instrucțiunile asistentului** | Activat implicit: cum funcționează proiectele Therion și ThIDE, apoi notele dumneavoastră (`assistant-skills.md`, lângă fișierul de setări) și cele ale proiectului (`assistant.md`, în dosarul de configurare al proiectului), câte 8 KB fiecare. |
| **Ce poate trimite asistentul modelului** | **Tot** (implicit; pentru un model care rulează pe acest calculator) · **Nu trimite modelului datele proiectului** (doar instrumente despre Therion și despre program, fără context, conversația nu se salvează) · **Ascunde valorile din rezultatele instrumentelor** (în măsura posibilului; răspunsurile devin mult mai slabe). Panoul întreabă o dată, înainte de prima întrebare. |

**CLI**-ul (`therion-cli`) și **LSP**-ul (`therion-lsp`) sunt executabile separate, nu setări de aici.

## Depanare

Comutatoare de nivel scăzut pentru **vizualizările web încorporate** ([vizualizatorul 3D](11-viewers.md#vizualizatorul-3d)
și diagramele structurale). **Modificările intră în vigoare după repornirea ThIDE.** Aveți nevoie de
această secțiune doar dacă un panou web nu funcționează corect — vedeți
[Depanare și întrebări frecvente](22-troubleshooting-and-faq.md).

**Motor web încorporat (Linux / WebKitGTK):**

| Setare | Note |
|---|---|
| **Dezactivează redarea DMA-BUF** | Rezolvă un **panou web gol (alb/negru)** pe multe sisteme Linux (drivere NVIDIA, Wayland). Doar Linux; **activat implicit**. Ignorat dacă `WEBKIT_DISABLE_DMABUF_RENDERER` este deja setat în mediul dumneavoastră. |
| **Dezactivează compunerea accelerată (ultimă soluție)** | Forțează randarea software când panoul rămâne *în continuare* gol. **Dezactivează WebGL, deci vizualizatorul 3D nu mai poate randa** — este destinat diagramelor structurale 2D. Doar Linux; dezactivat implicit. |
| **Vizualizare web offscreen (experimental)** | Randează conținutul web prin compozitorul aplicației, în loc să încorporeze o fereastră nativă. Poate rezolva probleme de încorporare, dar poate afecta gestionarea intrărilor de la tastatură și mouse. Doar Linux; dezactivat implicit. |

**Toate platformele:**

- **Activează instrumentele de dezvoltare web** — afișează butoanele **DevTools** și *Deschide o adresă
  web* în bara de instrumente a vizualizatorului 3D și permite inspectorul motorului prin clic dreapta ▸
  **Inspectează** / F12. Pentru dezvoltatori; dezactivat implicit.

## Scurtături tastatură

Reatribuiți orice comandă: faceți clic pe câmpul ei **Combinație** și apăsați combinația de taste (Esc
sau Delete o golește). Coloanele afișează **Comandă · Combinație · Implicit**; **Resetează** un rând sau
**Resetează tot la valorile implicite** (care listează scurtăturile modificate de dumneavoastră și cere
mai întâi confirmarea). Nimic nu se salvează până nu apăsați **OK** — **Renunță** readuce toate
combinațiile la starea anterioară. O combinație pe care o are deja altă comandă nu este preluată pe
tăcute: rândul afișează *Deja folosită de …* și vă întreabă dacă o **Înlocuiește** sau
**Păstrează-o pe cea veche**. Comenzile livrate **fără** o combinație implicită sunt listate și ele aici,
deci aici le atribuiți. Valorile implicite curente sunt listate în
[Scurtături tastatură](21-keyboard-shortcuts.md).

---

Următorul: [Extensibilitate (CLI, LSP, pluginuri, hook-uri) →](20-extensibility.md)
