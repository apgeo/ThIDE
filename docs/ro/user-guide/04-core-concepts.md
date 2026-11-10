# 4. Concepte de bază și vocabular

> [← Înapoi la pagina principală a ghidului](README.md)

Câteva idei explică modul în care se leagă totul în ThIDE. Învățați-le pe acestea cinci și restul
aplicației va avea sens.

## Spațiul de lucru

Un **spațiu de lucru** este dosarul (împreună cu subdosarele lui) pe care ThIDE îl tratează drept
proiectul dumneavoastră. Când folosiți **Deschide dosar** (sau deschideți un `.thconfig`), ThIDE îl
scanează, indexează fiecare fișier Therion și ține acest index la zi. Spațiul de lucru este domeniul de
aplicare al funcțiilor la nivel de proiect: navigarea între fișiere, diagnosticele „la nivelul spațiului
de lucru”, analizele din Prezentare generală, căutarea/înlocuirea în fișiere și căutarea de simboluri.

> ThIDE folosește termenul **spațiu de lucru** pentru dosarul proiectului în toată interfața. Veți mai
> întâlni ocazional cuvântul „proiect” în contextul analizelor — înseamnă același lucru.

## Thconfig-ul activ

Un `.thconfig` este fișierul de **configurare** al Therion: spune ce surse se citesc (`source` /
`input`), ce se selectează (`select`) și ce se exportă (`export`: hărți, modele, liste). Un spațiu de
lucru poate conține mai multe. ThIDE are întotdeauna **un singur thconfig activ** — cel pe care îl va
compila și cel care definește „proiectul” pentru analize și vizualizatoare. Îl alegeți dumneavoastră
(lista derulantă din partea de sus a Exploratorului spațiului de lucru, clic dreapta →
*Setează ca thconfig activ al spațiului de lucru* sau antetul unui fișier), iar el este afișat în calea de
navigare din bara de stare.

## Cele patru tipuri de fișiere

ThIDE înțelege nativ fiecare tip de fișier Therion:

| Tip | Ce conține | Ce oferă ThIDE |
|---|---|---|
| **`.th`** | Topografii: poligonale, stații, vize, puncte fixe, echivalări, hărți | Modelul complet al poligonalei, grila de măsurători, navigarea între stații |
| **`.th2`** | Desene: schițe alcătuite din puncte, linii și suprafețe | Modelul de puncte/linii/suprafețe, navigarea între schițe, predarea către Mapiah |
| **`.thconfig`** | Configurația proiectului: source/select/export/layout | Ținte de export, „setează ca activ”, comandă compilarea |
| **`.xvi`** | Imagini de fundal pentru desen, la care fac trimitere schițele | Modelul de grilă/scară, legat de schițele care le folosesc |

În interiorul unui bloc `layout`, ThIDE evidențiază chiar și codul **MetaPost și TeX încorporat** — vezi
[docs/layout-and-embedded-code.md](../../layout-and-embedded-code.md).

## Graful de obiecte („modelul semantic”)

Aceasta este inima ThIDE. Pe măsură ce deschideți și editați fișiere, ThIDE le analizează și construiește
în memorie un **graf de obiecte**: fiecare topografie, stație, viză, schiță, hartă, punct fix și
echivalare, plus relațiile dintre ele (cine pe cine include, care stații sunt același punct prin
`equate`, ce schițe reunește o hartă). Graful se actualizează pe măsură ce tastați.

Aproape fiecare funcție „inteligentă” citește din acest graf:

- **Salt la definiție / găsirea referințelor / redenumirea** între fișiere.
- **Diagnostice** — stații nerezolvate, puncte fixe duplicate, topografii deconectate, erori de închidere
  a buclelor…
- **Analize** — lungimi, adâncimi, echipe, intrări, calitatea datelor.
- **Previzualizări** — Previzualizare principală și tabelele din Navigator obiecte.
- **Harta relațională** — o diagramă a grafului însuși.

Pentru că este calculat în aplicație, este instantaneu — dar are **calitate de previzualizare** (fără
compensarea buclelor). Pentru valorile finale compensate și pentru hărțile randate, **compilați cu
Therion**.

## Diagnostice: mod permisiv și mod strict

ThIDE vă validează continuu fișierele și raportează **diagnostice** (erori, avertismente, informații) cu
coduri stabile. Implicit, rulează în **mod permisiv** — multe probleme sunt avertismente, ca să puteți
lucra în continuare. Activați **Vizualizare → Mod parser strict** pentru a transforma problemele tolerate
în modul permisiv în erori (util pentru o ultimă trecere de curățenie). Fiecare cod este catalogat în
[docs/diagnostics.md](../../diagnostics.md); pagina
[Diagnostice și validare](09-diagnostics-and-validation.md) explică panoul.

## Vocabularul unei peșteri (termenii Therion pe care îi veți întâlni)

Acestea sunt conceptele proprii ale Therion, prezente peste tot în ThIDE (de exemplu ca file ale
Navigatorului de obiecte):

| Termen | Semnificație |
|---|---|
| **Stație** | Un punct de topografie cu nume (de ex. `1.2` sau `entrance.0`). |
| **Viză / tronson** | O legătură măsurată între două stații (lungime, azimut, înclinare). |
| **Radială** | O viză de la o stație către perete (folosită pentru forma galeriei, nu pentru poligonala principală). |
| **Topografie** (`survey`) | Un bloc cu nume `survey … endsurvey` care grupează stații și vize. |
| **Punct fix** (`fix`) | O stație fixată la coordonate reale (georeferențiere). |
| **Echivalare** (`equate`) | O declarație că două nume de stații reprezintă același punct (leagă topografiile între ele). |
| **Schiță** (`scrap`) | O unitate de desen dintr-un fișier `.th2` (puncte/linii/suprafețe care alcătuiesc desenul galeriei). |
| **Hartă** (`map`) | O construcție din `.thconfig`/`.th` care reunește schițe sau alte hărți într-un rezultat. |
| **Componentă** | Termenul ThIDE pentru o *porțiune conexă* a rețelei topografice. O peșteră legată în întregime este o singură componentă; o topografie care plutește separat este o componentă de sine stătătoare. |

Înțelegerea **componentelor** vă ajută să citiți previzualizările și avertismentul „topografie
deconectată”: porțiunile care nu sunt unite prin stații comune sau echivalări (și nu sunt georeferențiate
printr-un `fix`) plutesc separat.

---

Următoarea: [Un tur al interfeței →](05-interface-tour.md)
