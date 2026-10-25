# 1. Introducere — ce este și ce nu este ThIDE

> [← Înapoi la pagina principală a ghidului](README.md)

## Ce este ThIDE

**ThIDE** este un **atelier / IDE desktop multiplatformă pentru proiecte de topografie speologică Therion**.
Gândiți-vă la el ca la unealta pe care o țineți deschisă în timp ce transformați datele brute de teren în
hărți de peșteră finalizate: *înțelege* limbajele Therion și *pilotează* lanțul de unelte Therion, astfel
încât să pierdeți mai puțin timp luptându-vă cu sintaxa și căile de foldere și mai mult timp topografiind
și desenând.

Vă oferă, într-o singură fereastră:

- Un **editor inteligent** pentru cele patru tipuri de fișiere Therion (`.th`, `.th2`, `.thconfig`, `.xvi`),
  cu colorare sintactică, completare automată, ajutor la trecerea cu mouse-ul, pliere și verificare a
  erorilor în timp real.
- O **înțelegere între fișiere** a întregului proiect — astfel încât poate sări la locul unde este definită
  o stație sau un survey (ridicare topografică), poate găsi fiecare referință la ea și o poate redenumi
  peste tot dintr-odată.
- **Compilare** dintr-un clic cu programul Therion real, cu erori pe care se poate face clic și o listă a
  fișierelor produse.
- **Vizualizatoare** integrate — un vizualizator de hărți 2D, un vizualizator de modele 3D încorporat și o
  previzualizare rapidă a liniei centrale — plus **analize ale topografiei** (lungimi, adâncimi, echipe,
  calitatea datelor) și un registru de **piste de explorare**.
- Ajutoare de **import/export** (Survex, Compass, DEM, GPX, KML/GeoJSON/GPX/CSV) și **calculatoare** utile
  (unități, coordonate, declinație magnetică).

## Ce *nu* este ThIDE

ThIDE **nu înlocuiește Therion.** Nu compilează singur peșterile — calculul propriu-zis al peșterii
(închiderea buclelor, randarea hărților, generarea modelului 3D) este delegat *programului `therion`
instalat de dumneavoastră*. ThIDE înțelege **formatele** fișierelor și orchestrează lanțul de unelte din
jurul lor.

Acest lucru contează în practică:

- Aveți în continuare nevoie de Therion instalat pentru a **compila** (vezi [Instalare și configurare](02-installation-and-setup.md)).
- Lungimile, adâncimile și previzualizările pe care vi le arată ThIDE sunt de **calitate de previzualizare**,
  calculate în aplicație pentru feedback instantaneu. Ele *nu* includ ajustarea buclelor făcută de Therion.
  **Therion rămâne sursa de adevăr** pentru cifrele și hărțile finale ajustate.
- ThIDE este un proiect independent și **nu este afiliat cu proiectul Therion și nici aprobat de acesta**.

## Cui se adresează

- **Speologilor și topografilor** care își țin deja datele în Therion și doresc o experiență de editare și
  compilare mai prietenoasă decât un simplu editor de text.
- **Coordonatorilor de proiect** care gestionează un proiect cu mai multe peșteri și mai mulți topografi și
  au nevoie de o imagine de ansamblu: ce este conectat, ce este „plutitor”, unde sunt pistele, cât de lungă
  este peștera.
- **Utilizatorilor avansați** care doresc hook-uri de scripting, o unealtă de linie de comandă fără interfață
  grafică sau integrări cu editoare — vezi [Extensibilitate](20-extensibility.md).

## Modelul mental într-un paragraf

Îndreptați ThIDE către un **spațiu de lucru** (un folder cu fișiere Therion) și alegeți un **`.thconfig`
activ** (fișierul de configurare care spune ce se compilează). ThIDE analizează totul într-un **graf de
obiecte** în memorie — survey-uri, stații, scrap-uri, hărți și modul în care se conectează — și îl ține
actualizat pe măsură ce tastați. Din acest graf pilotează tot restul: navigarea, verificarea erorilor,
analizele, previzualizările și compilarea. Pagina următoare vă ajută cu configurarea;
[Concepte de bază](04-core-concepts.md) explică pe larg acest vocabular.

## O notă despre stadiul alpha

ThIDE se află în **dezvoltare alpha activă**. Așteptați-vă la unele asperități ocazionale și la o interfață
care continuă să evolueze. Testarea a fost cea mai intensă pe Windows și mai redusă pe Linux și macOS.
Datele dumneavoastră sunt tot timpul fișiere text Therion simple — ThIDE nu le blochează niciodată într-un
format proprietar — așa că puteți reveni oricând direct la Therion.

---

Următoarea: [Instalare și configurare →](02-installation-and-setup.md)
