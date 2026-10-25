# Despre acest ghid

> [← Înapoi la pagina principală a ghidului](README.md)

Această pagină explică **cum este structurat ghidul utilizatorului**, astfel încât atât oamenii, cât și
asistenții AI să-l poată menține corect și extinde pe măsură ce ThIDE crește. Dacă doriți doar să
*folosiți* ThIDE, puteți sări peste această pagină.

## Ce este acest ghid

- **Public:** utilizatori finali și utilizatori avansați (speologi, topografi) — nu dezvoltatori.
- **Domeniu:** ce face aplicația și cum o folosiți. Implementarea/arhitectura sunt descrise în altă parte
  (vezi [docs/architecture.md](../architecture.md)); acest ghid menționează detaliile interne doar când
  influențează ce trebuie să *faceți*.
- **Format sursă:** **Markdown** simplu, câte un fișier pe subiect, în
  [`docs/user-guide/`](.). Este gândit intenționat să poată fi editat manual și de unelte.

## Formatul și distribuirea

Ghidul este scris în Markdown și poate fi **distribuit ca un singur PDF navigabil** în ThIDE
(cu un cuprins de semne de carte și legături interne pe care se poate face clic). ThIDE afișează deja
PDF-uri în [Vizualizatorul de hărți](11-viewers.md#vizualizator-hărți), așa că un `ThIDE-User-Guide.pdf` inclus se
deschide firesc în aplicație, iar același Markdown poate fi servit și într-un viitor panou de Ajutor în aplicație.

**De ce Markdown ca sursă (și nu redactare direct în PDF/Word):**

| Nevoie | Cum o satisface Markdown |
|---|---|
| Editabil de oameni | Oricine poate edita un fișier `.md` în orice editor de text — inclusiv în ThIDE. |
| Editabil și extensibil de agenți | Asistenții AI pot adăuga/rescrie pagini în mod fiabil din text simplu. |
| Sub control de versiuni | Diferențele sunt lizibile; modificările pot fi revizuite în Git. |
| Ieșiri multiple | O singură sursă → PDF (pentru distribuire), HTML (pentru un site web) sau Markdown în aplicație. |
| Consecvent cu depozitul | Restul `docs/` este deja în Markdown. |

### Construirea PDF-ului

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
`<AvaloniaResource Include="Assets\**" />` cu glob. **Ajutor → Ghid de utilizare** îl deschide apoi **în
vizualizatorul PDF propriu al aplicației** ([Vizualizatorul de hărți](11-viewers.md#vizualizator-hărți)) când acesta
este activat, altfel în vizualizatorul implicit al sistemului de operare — vezi
[`UserGuideService`](../../../ThIDE/Services/UserGuideService.cs) și comanda `OpenUserGuide` din
`MainWindowViewModel`.

**Cerințe:** întotdeauna `pandoc`, plus **`xelatex`** pentru formatul PDF — o distribuție TeX
(MiKTeX / TeX Live, cu `texlive-xetex`). xelatex este necesar deoarece ghidul folosește glife Unicode
(săgeți, simboluri de casete) pe care `pdflatex` nu le poate culege. Nu aveți TeX? Folosiți `-Format html`.

PDF-ul generat este un **artefact de build** — este în `.gitignore`, nu este pus în commit. Fluxul de
release ([.github/workflows/release.yml](../../../.github/workflows/release.yml)) îl construiește o singură
dată într-un job `guide`, **îl atașează la GitHub Release** ca descărcare separată și îl transmite job-urilor
`publish` per sistem de operare, care îl pun în `ThIDE/Assets` înainte de `dotnet publish`, astfel încât să
fie inclus în fiecare pachet. Când PDF-ul lipsește (de ex. la un build local simplu de dezvoltare),
**Ajutor → Ghid de utilizare** revine la deschiderea acestei surse Markdown de pe disc, apoi a documentației
online, așa încât elementul de meniu funcționează întotdeauna.

**Legăturile între pagini** sunt corectate la combinarea paginilor, de un filtru Lua Pandoc
([`build/user-guide-links.lua`](../../../build/user-guide-links.lua)): o legătură către altă pagină a ghidului
sare în interiorul documentului, iar o legătură către orice alt fișier din depozit (un document de referință,
un fișier sursă) deschide copia sa de pe GitHub într-un browser. O limitare cunoscută: o legătură către o
*anumită subsecțiune* a altei pagini a ghidului sare deocamdată la începutul acelei pagini — maparea fiecărei
sub-ancore este o posibilă continuare.

## Ștampila de versiune (actualizați-o ori de câte ori modificați ghidul)

Prima pagină ([`README.md`](README.md)) poartă o ștampilă care numește **versiunea ThIDE față de care a
fost verificat ultima dată conținutul acestui ghid**:

```markdown
> **For ThIDE 0.3.0-alpha.2** — guide content last updated 2026-10-25.
```

**Este un fapt despre conținut, nu despre build.** Intenționat *nu* este versiunea care se întâmpla să
fie în `build/version.props` când cineva a regenerat PDF-ul — o recompilare fără modificări de conținut
nu trebuie să o schimbe. Ea răspunde la întrebarea reală a cititorului: *„descrie acest ghid aplicația pe
care o rulez?”*

Reguli:

- **Actualizați-o când actualizați conținutul ghidului** pentru a corespunde unei versiuni mai noi a
  aplicației — și numai atunci. Folosiți versiunea SemVer a aplicației așa cum o raportează
  **Ajutor → Despre ThIDE** (`build/version.props`: `TherionVersionMajor.Minor.Patch` + `TherionPrerelease`).
- **Lăsați-o neschimbată** la editări care nu schimbă ce descrie ghidul (greșeli de tipar, formulări,
  corecturi de legături) — ștampila spune ce versiune a aplicației urmărește *conținutul*, nu când a fost
  atins fișierul.
- **Păstrați forma `**For ThIDE <version>**`.** Scripturile de build citesc această linie și o transmit
  lui Pandoc ca `subtitle` al documentului, astfel încât versiunea apare și pe **pagina de titlu a PDF-ului**
  (pagina 1) și în build-ul HTML. Restul liniei îl puteți reformula liber; păstrați acel prefix. Dacă
  ștampila lipsește, build-ul reușește totuși — afișează un avertisment, iar pagina de titlu omite pur și
  simplu versiunea.

## Convențiile paginilor

Păstrați paginile noi consecvente cu cele existente:

- **Denumirea fișierelor:** `NN-topic-name.md`, prefix din două cifre pentru ordinea de citire. Paginile
  meta nenumerotate (ca aceasta) au nume descriptive.
- **Începutul fiecărei pagini:** un titlu `H1`, apoi o linie cu legătură înapoi: `> [← Back to the User Guide home](README.md)`.
- **Legături încrucișate:** puneți legături generos între pagini. Folosiți legături relative (`07-editing.md`)
  și legați documentele de referință cu un nivel mai sus (`../diagnostics.md`).
- **Meniuri și controale:** numiți-le exact cum apar în aplicație, de ex. **Vizualizare → Vizualizator hărți**,
  panoul **Diagnostice**, meniul **Compilare**. Scrieți eticheta literală îngroșat.
- **Scurtături:** scrieți tastele ca `Ctrl+Shift+P`. Lista canonică este
  [Scurtături de tastatură](21-keyboard-shortcuts.md) — puneți o legătură acolo în loc să o duplicați.
- **Ton:** scurt, ușor de parcurs, orientat pe sarcini. Preferați tabelele și listele în locul paragrafelor lungi.
- **Disciplina domeniului:** un subiect pe pagină; dacă o pagină crește peste câteva ecrane, împărțiți-o și
  adăugați ambele jumătăți în [Cuprinsul](README.md#cuprins) paginii principale.

## Adăugarea unei pagini noi

1. Creați `docs/user-guide/NN-your-topic.md` urmând convențiile de mai sus.
2. Adăugați o linie pentru ea în secțiunea potrivită din Cuprinsul din [`README.md`](README.md).
3. Puneți legături către ea din paginile înrudite.
4. Dacă există un build PDF, fișierul nou este preluat automat de glob-ul ordonat după nume.

## Traduceri

Întregul `docs/` — inclusiv acest ghid — există și în **limba română**, în
[`docs/ro/`](../README.md), care oglindește arborele englez fișier cu fișier
(`docs/user-guide/07-editing.md` ↔ `docs/ro/user-guide/07-editing.md`). Sursa este engleza.

- **Modificați-le pe amândouă.** Când modificați o pagină, faceți aceeași modificare și în geamăna ei
  românească, în același commit, și actualizați ștampila de versiune în ambele fișiere `README.md` deodată.
- **Folosiți cuvintele aplicației.** Numiți meniurile și controalele așa cum le afișează interfața în română —
  etichetele provin din `ThIDE/Resources/Strings.ro.resx` — astfel încât cititorul să găsească pe ecran
  **Fișier → Deschide dosar…**.
- **Nu atingeți codul.** Comenzile, numele de fișiere, cuvintele cheie Therion, codurile de diagnostic,
  cheile de setări și id-urile de automatizare rămân neschimbate.
- **Ancorele urmează titlul.** O legătură către o secțiune folosește ancora titlului românesc, așa că după
  redenumirea unui titlu corectați legăturile care trimit la el.
- **Legăturile care ies din `docs/` au nevoie de încă un `../`** în arborele românesc, deoarece acesta se
  află cu un nivel mai adânc.

Build-ul PDF adună deocamdată doar paginile în engleză.

## Lacune cunoscute / primele editări potrivite

Aceasta este o **primă iterație**. Capturile de ecran ar ajuta mult și nu sunt incluse încă — o contribuție
firească ar fi adăugarea unui folder `images/` și includerea de capturi adnotate în paginile despre interfață
și vizualizatoare. Alte sarcini TODO sunt semnalate în text printr-un comentariu `<!-- TODO -->` acolo unde
este cazul.
