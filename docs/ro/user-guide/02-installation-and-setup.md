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
- **Mapiah** — un editor vizual de schițe/scrap-uri `.th2` căruia ThIDE îi poate preda fișiere.

## 2. Obținerea ThIDE

- **Dintr-o versiune publicată:** despachetați/instalați-o și lansați executabilul `ThIDE`.
- **Din sursă:**

  ```sh
  git clone <your-fork-url> ThIDE
  cd ThIDE
  dotnet restore ThIDE.sln
  # -m:1 este obligatoriu: soluția completă poate epuiza memoria la build-uri paralele.
  dotnet build ThIDE.sln -m:1 -c Release
  dotnet run --project ThIDE
  ```

Puteți de asemenea să-i dați lui ThIDE fișiere de deschis la pornire, de ex.
`ThIDE ~/caves/mycave/mycave.thconfig` — căi relative sau absolute, pe Windows, Linux și macOS.

## 3. Prima lansare

La prima lansare veți vedea pagina **Bun venit**. De aici puteți crea un fișier nou, deschide un
fișier/`thconfig`/folder existent, sări la elementele recente, ajunge la setările uneltelor externe sau
deschide Cartea Therion. (O puteți redeschide oricând cu **Vizualizare → Bun venit** și puteți dezactiva
„Afișează această pagină la pornire”.)

## 4. Indicați-i lui ThIDE uneltele externe

ThIDE detectează automat uneltele din `PATH` și din locațiile obișnuite de instalare, dar puteți seta căi
explicite:

1. Deschideți **Setări** (bara de meniu) → **Unelte externe**.
2. Fiecare rând arată unealta, dacă a fost detectată (**Detectat**), versiunea ei (**Versiune**) și sursa
   detectării (**Sursă**). Folosiți
   coloana **Suprascriere** pentru a indica un anumit executabil și **Testează** pentru a confirma că
   rulează.

Dacă o unealtă lipsește când încercați să o folosiți, ThIDE vă spune care este și unde o setați (de
exemplu, dialogul „Mapiah nu a fost găsit” trimite direct la această pagină de setări).

> ThIDE lansează Therion, Loch, Aven și Mapiah ca **programe separate** prin linia de comandă — nu
> încorporează și nu leagă codul lor. Vezi [Unelte externe și licențiere](../usage.md#unelte-externe).

## 5. Opțional: asocieri de fișiere

Pentru ca fișierele Therion să se deschidă în ThIDE când faceți dublu-clic pe ele în managerul de fișiere,
folosiți **Setări → Asocieri de fișiere** și apăsați **Asociază** (sau **Asociază toate**). Setarea este per
utilizator și nu necesită drepturi de administrator. Detalii și note despre platforme:
[docs/file-association.md](../file-association.md).

## 6. Unde își stochează ThIDE setările

Setările utilizatorului și starea per proiect se află în folderul de date ale aplicațiilor al platformei
(`%AppData%/ThIDE` pe Windows; echivalentele XDG / `~/Library` pe Linux/macOS): preferințe, scurtături de
tastatură, temă și limbă, indexul de simboluri și cache-ul de analiză, bufferele de recuperare după
blocare, plugin-uri și fișierele auxiliare per proiect (piste, metadate). Rareori aveți nevoie să umblați
direct în acest folder, dar aici vă cer câteva funcționalități (modelul de declinație, plugin-urile) să
puneți fișiere.

---

Următoarea: [Primii pași: prima sesiune →](03-getting-started.md)
