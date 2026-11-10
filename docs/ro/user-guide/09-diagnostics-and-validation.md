# 9. Diagnostice și validare

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE vă verifică fișierele continuu și raportează problemele sub formă de **diagnostice** — chiar
înainte să compilați. Acesta este unul dintre cele mai importante motive pentru a edita Therion în
ThIDE și nu într-un simplu editor de text.

## Unde apar diagnosticele

- **În editor:** linii ondulate colorate sub textul cu probleme, cu mesajul afișat când treceți cu mouse-ul peste ele.
- **În panoul Diagnostice** (**Vizualizare → Diagnostice**): o listă filtrabilă cu toate problemele.
- **În bara de stare:** un contor actualizat permanent; cu **F8 / Shift+F8** săriți la problema următoare/anterioară.

## Panoul Diagnostice

| Control | Ce face |
|---|---|
| Comutatoarele **Erori / Avertismente / Informații** | Filtrează după severitate. |
| **Tot spațiul de lucru** | Arată problemele din *toate* fișierele legate de thconfig, nu doar din cel deschis (necesită un spațiu de lucru). |
| **Doar referințe** | Arată doar problemele de referințe fără țintă sau nerezolvate (de ex. un `input` lipsă). |
| Coloanele **Cod · Severitate · Mesaj** | Faceți clic pe un rând pentru a sări la locul din sursă (Enter face același lucru de la tastatură). Mesajul și banda **Detalii** de sub tabel sunt afișate în limba aplicației. |
| **Explică / Deschide documentația ↗** | Deschide documentația pentru **Codul** unui diagnostic. |
| **Suprimă acest cod** | Nu mai raportează acel cod (în tot proiectul); **Șterge suprimările** anulează acest lucru. |

## Severitate și moduri

ThIDE este **permisiv în mod implicit**: multe probleme sunt *avertismente*, ca să puteți lucra în
continuare cu fișiere neterminate. Când doriți o verificare mai strictă:

- **Vizualizare → Mod parser strict** transformă problemele „permisive” în **erori**. Folosiți-l pentru
  o curățenie înainte de a partaja proiectul.

Fiecare diagnostic are un **cod stabil** (de ex. `TH0033`, `TH_SEM_015`, `TH2_004`). Catalogul complet
— fiecare cod, cu severitatea, mesajul și sursa sa — se află în documentul de referință
**[docs/diagnostics.md](../../diagnostics.md)**. O orientare rapidă:

| Familie | Acoperă | Exemple |
|---|---|---|
| `TH00xx` | Parserul de bază (`.th` / `.thconfig`) | comandă necunoscută, `fix`/`equate`/`data` greșit formate, sistem de coordonate necunoscut |
| `TH2_xxx` | Desenele `.th2` | tip necunoscut de punct/linie/arie, `line`/`area`/`scrap` neîncheiat |
| `TH_XVI_xxx` | Schițele `.xvi` | `set XVI…` necunoscut, țintă `-sketch` lipsă |
| `TH_SEM_xxx` | Semantica între fișiere | stație nerezolvată, `fix` duplicat, eroare de închidere a buclei, greșeală grosolană, **topografie deconectată** |
| `THIDE_DIR_xxx` | Directivele de aplicație `#@` | `#@region` / `#@endregion` fără pereche |
| `TH_WS_xxx` | Spațiul de lucru | cale negăsită, niciun thconfig în dosar |

## Validare dincolo de un singur fișier

În familia `TH_SEM_*` se vede cel mai bine faptul că ThIDE înțelege întregul proiect. Ea semnalează
lucruri pe care un singur fișier nu le poate dezvălui:

- **Referințe la stații nerezolvate** (cu sugestii „ați vrut să scrieți”), validate în tot proiectul.
- **Puncte fixe duplicate**, **rânduri de date** al căror număr de coloane sau ale căror valori nu se
  potrivesc cu ordinea citirilor.
- **Eroare de închidere a buclei** peste toleranță, vize cu **greșeli grosolane/valori aberante**,
  neconcordanțe între **viza directă și cea inversă**.
- **Fișier negăsit** — o țintă `input`/`source` care nu există pe disc. Un clic pe avertisment sare la
  linia care o numește.
- **Topografie deconectată** (`TH_SEM_015`) — o porțiune a peșterii care nu este nici legată de rețeaua
  principală, nici georeferențiată printr-un `fix`, așa că *plutește*. Sunt numite ambele stații de capăt
  și fișierele sursă, ca să o puteți găsi și îmbina.

## Validare în timp ce tastați (opțional)

Implicit, revalidarea aprofundată a întregului thconfig rulează la **salvare**. Activați **Validează la
tastare** (comutator în bara de instrumente sau [Setări → Editor](19-settings-and-preferences.md#editor))
pentru a reanaliza întregul arbore de fișiere la o scurtă pauză în tastare. Opțiunea este dezactivată
implicit pentru că poate solicita mult resursele în spațiile de lucru mari — lăsați-o dezactivată dacă
observați întârzieri și bazați-vă pe validarea la salvare.

## Remedieri rapide

Acolo unde ThIDE poate propune o reparație, **Remediere rapidă** (**Ctrl+.**) o oferă (și din Paleta de
comenzi). Când nu există nimic de remediat la poziția cursorului, vă anunță. O reparație care ar elimina
date nu apare niciodată singură și spune ce costă: pentru un tronson de lungime zero (`TH_SEM_012`) prima
propunere este un `equate` între cele două stații, iar *Transformă linia în comentariu* indică stația pe
care ar deconecta-o. O cale `export … -output` scrisă cu `\` (`TH0059`) se corectează rescriind
separatoarele ca `/`.

În **panoul Diagnostice**, faceți clic dreapta pe un rând pentru **Corectează** (prima sa remediere) și,
pentru problemele cu o remediere sigură peste tot, **Corectează toate „<code>” din spațiul de lucru…**:
un bloc fără cuvântul de închidere (`endsurvey` și altele asemenea, inserat după corpul blocului) și o
stație necunoscută cu o singură sugestie „ați vrut să scrieți” (redenumită) într-un fișier verificat
separat. O previzualizare listează fiecare modificare — fișier, linie, înainte și după — și debifați ce
nu doriți; **Aplică** le scrie, iar proiectul este reverificat. Cazurile ambigue sunt numărate și lăsate
neatinse. Într-un fișier deschis, remedierea este o modificare în editor; un fișier care nu este deschis
este **rescris pe disc**, iar întreaga rulare este o singură intrare a
[anulării la nivelul spațiului de lucru](05-interface-tour.md#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere)
(**Editare → Anulează modificarea din mai multe fișiere**).

Într-un proiect, un `equate` care numește o stație pe care niciun fișier nu o definește (`TH_SEM_001`,
verificat în tot proiectul) primește și el o sugestie „ați vrut să scrieți” — cea mai apropiată stație
din topografia proprie a echivalării, comparată după numele simplu — iar **Ctrl+.** sau **Corectează**
oferă redenumirea pentru acea referință. „Corectează toate” le lasă deoparte: redenumirea fiecărei
referințe nerezolvate cu cel mai apropiat nume ar putea lega stațiile greșite, așa că verificați-le pe
fiecare în parte.

## Curățenie în tot proiectul (meniul Unelte)

Aceste unelte modifică multe fișiere deodată. Fiecare arată mai întâi o previzualizare, iar când o
aplicați, fișierele care nu sunt deschise sunt scrise pe disc — o singură comandă **Editare → Anulează
modificarea din mai multe fișiere** (care poartă numele uneltei) le readuce pe toate la loc. Un fișier
deschis, cu modificări nesalvate, primește schimbarea în editorul său și rămâne nesalvat.

- **Adaugă scrap-urile nereferite în hărți…** — listează schițele desenate pe care nicio hartă nu le
  include (deci lipsesc din toate hărțile exportate), propune o hartă pentru fiecare (una din fișierul care
  include schița prin `input`, altfel cea cu numele cel mai apropiat) și le adaugă după ultimul membru al
  hărții, scrise `scrap@survey` când este nevoie.
- **Sugerează echivalări…** — stații mai apropiate decât o distanță aleasă de dumneavoastră, dar aflate în
  porțiuni diferite ale peșterii, georeferențiate separat: probabil `equate`-uri lipsă. Pozițiile sunt
  estimări, așa că fiecare pereche este un candidat de verificat; **Leagă…** deschide
  [Leagă un fișier de topografie](24-linking-surveys.md) pe cele două stații și scrie doar `equate`-ul,
  acolo unde ambele pot fi numite.
- **Formatează toate fișierele…** — trece fiecare fișier al proiectului prin formatator, cu previzualizare
  în prealabil (fiecare fișier modificat arată ce se schimbă, de exemplu *1 linie: spații de la sfârșitul
  liniei eliminate*); fișierele care nu se analizează fără erori sunt lăsate neatinse, iar terminațiile de
  linie sunt păstrate. Când nu este nimic de schimbat, dialogul vă spune (*Nimic de schimbat în 91 de
  fișiere*).
- **Normalizează proiectul…** — Formatează toate fișierele plus întreținerea de care are nevoie un
  depozit partajat: un singur stil de terminație de linie (LF sau CRLF), fișierele care declară o
  codificare veche (`encoding iso8859-2`, de exemplu) convertite în UTF-8 și — dacă bifați opțiunea —
  fiecare șir de linii `input` consecutive sortat natural (`trip2` înainte de `trip10`). Previzualizare ca
  la Formatează toate fișierele.
- **Renumerotează stațiile…** — un prefix nou (`1, 2, 3` → `A1, A2, A3`), un prefix înlocuit sau eliminat
  ori numerele decalate (`+100`, cu păstrarea zerourilor din față) pentru toate stațiile unei topografii,
  opțional și pentru subtopografiile ei. Stațiile ale căror nume nu se potrivesc (niciun număr de decalat,
  un alt prefix) sunt lăsate neatinse și numărate în rezumat. Toate locurile în care este scrisă stația se
  schimbă împreună — vize, `equate`, `fix`, `station` și referințele `@` din alte fișiere. Previzualizarea
  listează liniile modificate așa cum vor arăta după aceea, iar unealta refuză un plan care ar da același
  nume la două stații sau ar refolosi un nume care rămâne. Previzualizarea se aplică în totalitate sau
  deloc. Etichetele stațiilor din schițe (`-name` din `.th2`) nu sunt redenumite: verificați-le după aceea.

## Ajustarea anumitor verificări

Câteva verificări se pot configura pe pagina **Vizualizare** din Setări (**Setări → Vizualizare**, grupul
*Diagnostice*) — de exemplu, dacă un `fix` local simplu (fără sistem de coordonate) se consideră
„ancorare” pentru o porțiune deconectată și, prin urmare, suprimă `TH_SEM_015`. Consultați
[Setări → Vizualizare](19-settings-and-preferences.md#vizualizare).

---

Următorul: [Compilare și rezultate →](10-compiling-and-output.md)
