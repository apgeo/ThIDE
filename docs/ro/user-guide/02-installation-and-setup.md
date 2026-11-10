# 2. Instalare și configurare

> [← Înapoi la pagina principală a ghidului](README.md)

Această pagină vă ajută să porniți ThIDE și să-l conectați la uneltele externe pe care le pilotează.

## 1. Cerințe preliminare

| Cerință | De ce | Note |
|---|---|---|
| **Runtime/SDK .NET 8** (`8.0.x`) | ThIDE este o aplicație .NET 8 | SDK-ul este necesar dacă compilați din sursă; o versiune împachetată are nevoie doar de runtime. |
| **Lanțul de unelte Therion** (`therion`) | Compilarea peșterilor | Nu este inclus. Instalați-l de pe [site-ul Therion](https://therion.speleo.sk/). ThIDE vizează **v6.4.0**. |
| **Un motor web al platformei** | Vizualizatorul 3D încorporat și graficul 3D structural | Windows: WebView2 (deja prezent pe Windows 11 / Edge modern). macOS: WKWebView (integrat). Linux: WebKitGTK — `apt install libwebkit2gtk-4.1-0`. |

Programe însoțitoare opționale pe care ThIDE le poate lansa dacă sunt prezente:

- **Loch** și/sau **Aven** — vizualizatoare externe 3D/2D pentru modelele compilate.
- **Survex** — furnizează uneltele `dump3d` și `extend` (vezi [Compilare și rezultate](10-compiling-and-output.md)).
- **Mapiah** — un editor vizual de schițe `.th2` căruia ThIDE îi poate preda fișierele.

## 2. Obținerea ThIDE

- **Dintr-o versiune publicată:** despachetați-o sau instalați-o și lansați executabilul `ThIDE`.
- **Din sursă:**

  ```sh
  git clone <your-fork-url> ThIDE
  cd ThIDE
  dotnet restore ThIDE.sln
  # -m:1 este obligatoriu: soluția completă poate epuiza memoria la build-uri paralele.
  dotnet build ThIDE.sln -m:1 -c Release
  dotnet run --project ThIDE
  ```

## 3. Prima lansare

La prima lansare veți vedea pagina **Bun venit**. De aici puteți crea un fișier nou, puteți deschide un
fișier, un `thconfig` sau un dosar existent, puteți sări la elementele recente, puteți ajunge la setările
uneltelor externe sau puteți deschide Cartea Therion. (O puteți redeschide oricând cu
**Vizualizare → Bun venit** și puteți debifa „Afișează această pagină la pornire”.)

## 4. Indicați-i lui ThIDE uneltele externe

ThIDE detectează automat uneltele aflate în `PATH` și în locațiile obișnuite de instalare, dar puteți seta
și căi explicite:

1. Deschideți fereastra Setări (**Setări → Preferințe…** sau Ctrl+,) și mergeți la pagina
   **Unelte externe**. Acest ghid scrie asta sub forma **Setări → Unelte externe**: *Setări* este
   fereastra, cuvintele de după săgeată sunt paginile ei și, cu încă o săgeată, un control de pe pagină.
2. Fiecare rând arată unealta, dacă a fost detectată (**Detectat**), versiunea ei (**Versiune**) și sursa
   detectării (**Sursă**). Folosiți coloana **Suprascriere** pentru a indica un anumit executabil și
   **Testează** pentru a confirma că acesta rulează.

Dacă o unealtă lipsește când încercați să o folosiți, ThIDE vă spune care este și unde o setați (de
exemplu, dialogul „Mapiah nu a fost găsit” trimite direct la această pagină de setări).

> ThIDE lansează Therion, Loch, Aven și Mapiah ca **programe separate**, prin linia de comandă — nu
> încorporează și nu leagă codul lor. Vezi [Unelte externe și licențiere](../../usage.md#external-tools).

## 5. Opțional: asocieri de fișiere

Pentru ca fișierele Therion să se deschidă în ThIDE când faceți dublu clic pe ele în managerul de fișiere,
folosiți **Setări → Asocieri de fișiere** și apăsați **Asociază** (sau **Asociază toate**). Asocierea se
face per utilizator și nu necesită drepturi de administrator. Detalii și note despre platforme:
[docs/file-association.md](../../file-association.md).

## 6. Unde își stochează ThIDE setările

Setările utilizatorului și starea fiecărui proiect se află în **dosarul de setări al ThIDE**, în dosarul
de date ale aplicațiilor pe care platforma dumneavoastră îl rezervă fiecărui utilizator:

| Platformă | Dosarul de setări |
|---|---|
| Windows | `%AppData%\ThIDE` |
| Linux | `~/.config/ThIDE` (sub `$XDG_CONFIG_HOME`, când această variabilă este setată) |
| macOS | dosarul de date ale aplicațiilor pe care .NET îl raportează pentru utilizatorul dumneavoastră — panoul care vă cere să puneți un fișier acolo (modelul de declinație, pluginurile) indică exact calea |

Acesta conține preferințele, scurtăturile de tastatură, tema și limba, indexul de simboluri și cache-ul de
analiză, copiile de recuperare după o închidere neașteptată, pluginurile și fișierele auxiliare ale
fiecărui proiect (piste, metadate). Rareori aveți nevoie să umblați direct în acest dosar, dar aici vă cer
câteva funcționalități (modelul de declinație, pluginurile) să puneți fișiere. Celelalte pagini ale
acestui ghid îl numesc *dosarul de setări*.

---

Următoarea: [Primii pași: prima sesiune →](03-getting-started.md)
