# 4. Concepte de bază și vocabular

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Câteva idei explică modul în care se leagă totul în ThIDE. Învățați-le pe acestea cinci și restul
aplicației va avea sens.

## Spațiul de lucru

Un **spațiu de lucru** este dosarul (împreună cu subdosarele sale) pe care ThIDE îl tratează drept
proiectul dumneavoastră. Când folosiți **Deschide dosar** (sau deschideți un `.thconfig`), ThIDE îl
scanează, indexează fiecare fișier Therion și menține acest index actualizat. Spațiul de lucru este
domeniul funcțiilor la nivel de proiect: navigarea între fișiere, diagnosticele cu „domeniu proiect”,
analizele din Prezentare generală, căutarea/înlocuirea în fișiere și căutarea de simboluri.

> ThIDE folosește termenul **spațiu de lucru** pentru dosarul proiectului în toată interfața. Veți
> mai întâlni ocazional cuvântul „proiect” în contextul analizelor — înseamnă același lucru.

## thconfig-ul activ

Un `.thconfig` este fișierul de **configurare** Therion: precizează ce surse se citesc (`source` /
`input`), ce se selectează (`select`) și ce se exportă (`export`: hărți, modele, liste). Un spațiu de
lucru poate conține mai multe. ThIDE are întotdeauna **un singur thconfig activ** — cel pe care îl va
compila și cel care definește „proiectul” pentru analize și vizualizatoare. Îl alegeți dumneavoastră
(clic dreapta în **Explorator proiect** → *Setează ca thconfig activ al proiectului*, sau din antetul
unui fișier), iar acesta este afișat în calea de navigare (breadcrumb) din bara de stare.

## Cele patru tipuri de fișiere

ThIDE înțelege nativ fiecare tip de fișier Therion:

| Tip | Ce conține | Puncte forte în ThIDE |
|---|---|---|
| **`.th`** | Survey-uri (ridicări topografice): linii centrale, stații, vizări, fix-uri, equate-uri, hărți | Model complet al liniei centrale, grilă de măsurători, navigare între stații |
| **`.th2`** | Desene: scrap-uri alcătuite din puncte, linii și suprafețe | Model de puncte/linii/suprafețe, navigare între scrap-uri, predare către Mapiah |
| **`.thconfig`** | Configurarea proiectului: source/select/export/layout | Ținte de export, „setare ca activ”, comandă compilarea |
| **`.xvi`** | Fundaluri de schiță referite de scrap-uri | Model de grilă/scară, legat de scrap-urile care le folosesc |

În interiorul unui bloc `layout`, ThIDE evidențiază chiar și codul **MetaPost și TeX încorporat** —
vedeți [docs/layout-and-embedded-code.md](../layout-and-embedded-code.md).

## Graful de obiecte („modelul semantic”)

Aceasta este inima ThIDE. Pe măsură ce deschideți și editați fișiere, ThIDE le analizează și
construiește în memorie un **graf de obiecte**: fiecare survey, stație, vizare, scrap, hartă, fix și
equate, plus relațiile dintre ele (cine pe cine include, care stații sunt același punct prin
`equate`, ce scrap-uri agregă o hartă). Graful se actualizează pe măsură ce tastați.

Aproape fiecare funcție „inteligentă” citește din acest graf:

- **Salt la definiție / găsire referințe / redenumire** între fișiere.
- **Diagnostice** — stații nerezolvate, fix-uri duplicate, survey-uri deconectate, erori de închidere
  a buclelor…
- **Analize** — lungimi, adâncimi, echipe, intrări, calitatea datelor.
- **Previzualizări** — **Previzualizare principală** și tabelele din **Navigator obiecte**.
- **Harta relațională** — o diagramă a grafului însuși.

Deoarece este calculat în aplicație, este instantaneu — dar are **calitate de previzualizare** (fără
compensarea buclelor). Pentru valorile finale compensate și pentru hărțile randate, **compilați cu
Therion**.

## Diagnostice: mod permisiv vs. strict

ThIDE validează continuu fișierele și raportează **diagnostice** (erori, avertismente, informații) cu
coduri stabile. Implicit rulează în **mod permisiv** — multe probleme sunt avertismente, ca să puteți
lucra în continuare. Activați **Vizualizare → Mod parser strict** pentru a transforma problemele
permisive în erori (util pentru o trecere finală de curățare). Fiecare cod este catalogat în
[docs/diagnostics.md](../diagnostics.md); pagina [Diagnostice și validare](09-diagnostics-and-validation.md)
explică panoul.

## Vocabularul unei peșteri (termeni Therion pe care îi veți întâlni)

Acestea sunt conceptele proprii Therion, prezente în tot ThIDE (de ex. ca file în **Navigator
obiecte**):

| Termen | Semnificație |
|---|---|
| **Stație** | Un punct de ridicare topografică cu nume (de ex. `1.2` sau `entrance.0`). |
| **Vizare** | O legătură măsurată între două stații (lungime, azimut, înclinare). |
| **Splay** | O vizare către perete dintr-o stație (folosită pentru forma galeriei, nu pentru linia principală). |
| **Survey** | Un bloc cu nume `survey … endsurvey` care grupează stații și vizări. |
| **Fix** | O stație fixată la coordonate reale (georeferențiere). |
| **Equate** | O declarație că două nume de stații reprezintă același punct (leagă survey-urile între ele). |
| **Scrap** | O unitate de desen dintr-un fișier `.th2` (puncte/linii/suprafețe care alcătuiesc desenul galeriei). |
| **Hartă** | O construcție `.thconfig`/`.th` care agregă scrap-uri/alte hărți într-un rezultat. |
| **Componentă** | Termenul ThIDE pentru o *porțiune conexă* a rețelei topografice. O peșteră legată complet este o singură componentă; un survey izolat este o componentă proprie. |

Înțelegerea **componentelor** ajută la citirea previzualizărilor și a avertismentului „survey
deconectat”: porțiunile care nu sunt unite prin stații comune/equate-uri (și nu sunt georeferențiate
printr-un `fix`) plutesc separat.

---

Următorul: [Un tur al interfeței →](05-interface-tour.md)
