# Despre acest ghid

> [← Înapoi la pagina principală a ghidului](README.md)

Această pagină explică **cum este structurat ghidul utilizatorului**, astfel încât atât oamenii, cât și
asistenții AI să-l poată menține corect și să-l poată extinde pe măsură ce ThIDE crește. Dacă doriți doar
să *folosiți* ThIDE, puteți sări peste această pagină.

## Ce este acest ghid

- **Public:** utilizatori finali și utilizatori avansați (speologi, topografi) — nu dezvoltatori.
- **Domeniu:** ce face aplicația și cum o folosiți. Implementarea și arhitectura sunt descrise în altă
  parte (vezi [docs/architecture.md](../../architecture.md)); acest ghid menționează detaliile interne
  doar când influențează ce trebuie să *faceți*.
- **Format sursă:** **Markdown** simplu, câte un fișier pe subiect, în
  [`docs/user-guide/`](.). Este gândit intenționat să poată fi editat atât manual, cât și de unelte.

## Formatul și distribuirea

Ghidul este scris în Markdown și poate fi **distribuit ca un singur PDF navigabil** în ThIDE
(cu un cuprins de semne de carte și legături interne pe care se poate face clic). ThIDE afișează deja
PDF-uri în [Vizualizator hărți](11-viewers.md#vizualizatorul-de-hărți), așa că un `ThIDE-User-Guide.pdf` inclus se
deschide firesc în aplicație, iar același Markdown poate fi servit și într-un viitor panou de ajutor din
aplicație.

**De ce Markdown ca sursă (și nu redactare direct în PDF/Word):**

| Nevoie | Cum o satisface Markdown |
|---|---|
| Editabil de oameni | Oricine poate edita un fișier `.md` în orice editor de text — inclusiv în ThIDE. |
| Editabil și extensibil de agenți | Asistenții AI pot adăuga sau rescrie pagini în mod fiabil, pornind de la text simplu. |
| Sub control de versiuni | Diferențele sunt lizibile; modificările pot fi revizuite în Git. |
| Ieșiri multiple | O singură sursă → PDF (pentru distribuire), HTML (pentru un site web) sau Markdown în aplicație. |
| Consecvent cu depozitul | Restul `docs/` este deja în Markdown. |

### Generarea PDF-ului

Un script de build adună aceste pagini (în ordinea numelor de fișier) și rulează [Pandoc](https://pandoc.org/)
pentru a obține un singur PDF cu cuprins de semne de carte și tablă de materii:

```sh
# Windows
pwsh build/build-user-guide.ps1
# Linux / macOS
build/build-user-guide.sh
# Nu aveți un motor PDF instalat? Generați în schimb un HTML autonom:
pwsh build/build-user-guide.ps1 -Format html      #  (sau:  FORMAT=html build/build-user-guide.sh)
```

Rezultatul implicit este `ThIDE/Assets/ThIDE-User-Guide.pdf`, pe care aplicația îl include automat prin
`<AvaloniaResource Include="Assets\**" />`, care folosește un model glob. **Ajutor → Ghid de utilizare**
îl deschide apoi **în vizualizatorul PDF propriu al aplicației** ([Vizualizator hărți](11-viewers.md#vizualizatorul-de-hărți))
când acesta este activat, altfel în vizualizatorul implicit al sistemului de operare — vezi
[`UserGuideService`](../../../ThIDE/Services/UserGuideService.cs) și comanda `OpenUserGuide` din
`MainWindowViewModel`.

**Cerințe:** întotdeauna `pandoc`, plus **`xelatex`** pentru formatul PDF — o distribuție TeX
(MiKTeX / TeX Live, cu `texlive-xetex`). xelatex este necesar pentru că ghidul folosește glife Unicode
(săgeți, simboluri de casete) pe care `pdflatex` nu le poate culege. Nu aveți TeX? Folosiți `-Format html`.

PDF-ul generat este un **artefact de build** — este trecut în `.gitignore`, nu este pus în commit. Fluxul
de lansare ([.github/workflows/release.yml](../../../.github/workflows/release.yml)) îl generează o
singură dată, într-un job `guide`, **îl atașează la GitHub Release** ca descărcare separată și îl transmite
joburilor `publish` pentru fiecare sistem de operare, care îl pun în `ThIDE/Assets` înainte de
`dotnet publish`, astfel încât să fie inclus în fiecare pachet. Când PDF-ul lipsește (de exemplu la un
build local obișnuit de dezvoltare), **Ajutor → Ghid de utilizare** deschide în schimb această sursă
Markdown de pe disc, iar dacă nici ea nu există, documentația online, așa că elementul de meniu
funcționează întotdeauna.

**Legăturile dintre pagini** sunt corectate la combinarea paginilor, de un filtru Lua pentru Pandoc
([`build/user-guide-links.lua`](../../../build/user-guide-links.lua)): o legătură către altă pagină a
ghidului sare în interiorul documentului, iar o legătură către orice alt fișier din depozit (un document de
referință, un fișier sursă) deschide copia acestuia de pe GitHub într-un browser. O limitare cunoscută: o
legătură către o *anumită subsecțiune* a altei pagini a ghidului sare deocamdată la începutul acelei
pagini — maparea fiecărei ancore de subsecțiune este o posibilă îmbunătățire ulterioară.

## Ștampila de versiune (actualizați-o ori de câte ori modificați ghidul)

Prima pagină ([`README.md`](README.md)) poartă o ștampilă care numește **versiunea ThIDE față de care a
fost verificat ultima dată conținutul acestui ghid**:

```markdown
> **For ThIDE 0.4.0-alpha.1** — guide content last updated 2026-11-04.
```

**Este un fapt despre conținut, nu despre build.** Intenționat *nu* este versiunea care se întâmpla să
fie în `build/version.props` când cineva a regenerat PDF-ul — o regenerare fără modificări de conținut
nu trebuie să o schimbe. Ea răspunde la întrebarea reală a cititorului: *„descrie acest ghid aplicația
pe care o rulez?”*

Reguli:

- **Actualizați-o când actualizați conținutul ghidului** ca să corespundă unei versiuni mai noi a
  aplicației — și numai atunci. Folosiți versiunea SemVer a aplicației așa cum o raportează
  **Ajutor → Despre ThIDE** (`build/version.props`: `TherionVersionMajor.Minor.Patch` + `TherionPrerelease`).
- **Lăsați-o neschimbată** la editările care nu schimbă ce descrie ghidul (greșeli de tipar, formulări,
  corecturi de legături) — ștampila spune ce versiune a aplicației urmărește *conținutul*, nu când a fost
  atins fișierul.
- **Păstrați forma `**For ThIDE <version>**`.** Scripturile de build citesc această linie și o transmit
  lui Pandoc ca `subtitle` al documentului, astfel încât versiunea apare și pe **pagina de titlu a
  PDF-ului** (pagina 1), și în build-ul HTML. Restul liniei îl puteți reformula liber; păstrați acel
  prefix. Dacă ștampila lipsește, build-ul reușește totuși — afișează un avertisment, iar pagina de titlu
  pur și simplu nu mai conține versiunea.

## Convențiile paginilor

Păstrați paginile noi consecvente cu cele existente:

- **Denumirea fișierelor:** `NN-topic-name.md`, cu un prefix din două cifre pentru ordinea de citire.
  Paginile meta nenumerotate (ca aceasta) au nume descriptive.
- **Începutul fiecărei pagini:** un titlu `H1`, apoi o linie cu legătura înapoi: `> [← Back to the User Guide home](README.md)`.
- **Legături între pagini:** puneți legături din belșug între pagini. Folosiți legături relative
  (`07-editing.md`) și trimiteți la documentele de referință cu un nivel mai sus (`../diagnostics.md`).
- **Meniuri și controale:** numiți-le exact cum apar în aplicație, de exemplu
  **Vizualizare → Vizualizator hărți**, panoul **Diagnostice**, meniul **Compilare**. Scrieți îngroșat
  eticheta literală, cu majusculele ei (în engleză, aplicația folosește Title Case pentru elementele de
  meniu) și fără sublinierea tastei de acces.
- **Căi de meniu:** scrieți o cale într-un singur fragment îngroșat, cu săgeți (`Menu → Item → Subitem`). Primul cuvânt este un meniu din bara de meniuri (Fișier, Vizualizare, Editare, Căutare,
  Unelte, Compilare, Setări, Ajutor); un „…” final și coada din paranteză a unei etichete lungi
  („Prezentare generală (Tablou de bord / Topografii / Audit)”) pot fi omise. Două feluri de căi nu sunt
  elemente de meniu și se scriu la fel:
  - O cale care începe cu *Setări* și numește o pagină deschide fereastra Setări
    (**Setări → Preferințe…**, Ctrl+,) la acea pagină: **Setări → Unelte externe**. Încă o săgeată
    numește un control de pe pagină: **Setări → Vizualizare → Vizualizator 3D încorporat**.
  - O cale care începe cu un panou din meniul Vizualizare numește o filă din interiorul lui:
    **Prezentare generală → Audit**. Verificarea nu se uită în interiorul panourilor.

  Nu puneți săgeți în text îngroșat pentru nimic altceva — verificarea de mai jos le citește drept căi.
- **Scurtături:** scrieți tastele sub forma `Ctrl+Shift+P`. Lista de referință este
  [Scurtături de tastatură](21-keyboard-shortcuts.md) — trimiteți acolo în loc să o duplicați.
- **Ton:** scurt, ușor de parcurs, orientat pe sarcini. Preferați tabelele și listele în locul
  paragrafelor lungi.
- **Disciplina domeniului:** un subiect pe pagină; dacă o pagină crește peste câteva ecrane, împărțiți-o
  și adăugați ambele jumătăți în [Cuprinsul](README.md#cuprins) paginii principale.

## Verificarea căilor de meniu

Un script confruntă fiecare cale de meniu îngroșată din ghid cu meniurile pe care aplicația le are cu
adevărat, astfel încât un element de meniu redenumit sau mutat apare ca o verificare eșuată, și nu ca un
cititor care nu-l găsește:

```sh
python3 tests/ci/check-guide-menu-paths.py
```

Scriptul citește arborele de meniuri din `ThIDE/Views/MainWindow.axaml`, textele elementelor din valorile
în engleză din `ThIDE/Resources/Strings.resx` (și din proprietățile de etichetă ale `MainWindowViewModel`
pentru anteturile legate prin binding), iar paginile de setări din `PreferencesViewModel`. Afișează
fiecare cale care nu există, cu fișierul și linia ei și cu cel mai apropiat element real, și se încheie cu
codul 1. Elementele care se completează la rulare (Fișiere recente, Macrouri, Sarcini, Compilează ținta)
acceptă orice text sub ele. Aceeași verificare rulează în CI (jobul **Test** din
`.github/workflows/ci.yml`), iar `tests/ci/test_check_guide_menu_paths.py` testează scriptul însuși.
Rulați-o după ce schimbați o etichetă de meniu și după ce editați ghidul.

## Adăugarea unei pagini noi

1. Creați `docs/user-guide/NN-your-topic.md` urmând convențiile de mai sus.
2. Adăugați o linie pentru ea în secțiunea potrivită a Cuprinsului din [`README.md`](README.md).
3. Puneți legături către ea din paginile înrudite.
4. Dacă există un build PDF, fișierul nou este preluat automat de modelul glob ordonat după nume.
5. Rulați verificarea căilor de meniu de mai sus.

## Traduceri

Partea documentației destinată utilizatorilor există și în **limba română**, în `docs/ro/`: acest ghid
în [`docs/ro/user-guide/`](README.md), plus `docs/ro/features.md` și `docs/ro/screenshots.md`. Arborele
îl oglindește pe cel englezesc fișier cu fișier
(`docs/user-guide/07-editing.md` ↔ `docs/ro/user-guide/07-editing.md`). Sursa este engleza; documentația
tehnică (arhitectură, LSP, MCP, pluginuri…) există doar în engleză.

- **Modificați-le pe amândouă.** Când modificați o pagină tradusă, faceți aceeași modificare și în geamăna
  ei românească, ideal în același commit, și actualizați ștampila de versiune în ambele fișiere
  `README.md` deodată.
- **Folosiți cuvintele aplicației.** Numiți meniurile și controalele așa cum le afișează interfața în
  română — etichetele provin din `ThIDE/Resources/Strings.ro.resx` —, astfel încât cititorul să le
  găsească pe ecran.
- **Nu atingeți codul.** Comenzile, numele de fișiere, cuvintele cheie Therion, codurile de diagnostic,
  cheile de setări și id-urile de automatizare rămân neschimbate.
- **Țineți titlurile în pas.** O pagină românească are aceleași titluri, în aceeași ordine și la aceleași
  niveluri, ca geamăna ei englezească; o legătură către o secțiune folosește ancora titlului românesc.
- **Legăturile care ies din setul tradus au nevoie de încă un `../`** în arborele românesc, deoarece
  acesta se află cu un nivel mai adânc (`../diagnostics.md` din ghidul englezesc devine
  `../../diagnostics.md` în cel românesc).
- **Capturile de ecran sunt comune**: galeria românească afișează imaginile din arborele englezesc.

Build-ul PDF adună deocamdată doar paginile în engleză.

## Lacune cunoscute / primele editări potrivite

Aceasta este o **primă iterație**. Capturile de ecran ar ajuta mult și nu sunt incluse încă — o
contribuție firească ar fi adăugarea unui dosar `images/` și includerea unor capturi adnotate în paginile
despre interfață și despre vizualizatoare. Alte lucruri de făcut sunt semnalate în text, acolo unde este
cazul, printr-un comentariu `<!-- TODO -->`.
