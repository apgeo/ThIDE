# 9. Diagnostice și validare

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE vă verifică fișierele continuu și raportează problemele sub formă de **diagnostice** — înainte să
compilați. Acesta este unul dintre cele mai importante motive pentru a edita Therion în ThIDE și nu într-un simplu editor de text.

## Unde apar diagnosticele

- **În editor:** linii ondulate colorate sub textul problematic, cu mesajul afișat la hover.
- **În panoul Diagnostice** (**Vizualizare → Diagnostice**): o listă filtrabilă cu toate problemele.
- **În bara de stare:** un contor actualizat permanent, cu **F8 / Shift+F8** pentru salt la problema următoare/anterioară.

## Panoul Diagnostice

| Control | Ce face |
|---|---|
| Comutatoarele **Erori / Avertismente / Informații** | Filtrează după severitate. |
| **Domeniu proiect** | Arată problemele din *toate* fișierele conectate la thconfig, nu doar din cel deschis (necesită un spațiu de lucru). |
| **Doar referințe** | Arată doar problemele de referințe nerezolvate (de ex. un `input` lipsă). |
| Coloanele **Cod · Severitate · Mesaj** | Faceți clic pe un rând pentru a sări la locul din sursă. |
| **Explică / Deschide documentația ↗** | Deschide documentația pentru **Codul** unui diagnostic. |
| **Suprimă acest cod** | Nu mai raportează acel cod (în tot proiectul); **Șterge suprimările** pentru a anula. |

## Severitate și moduri

ThIDE este **permisiv în mod implicit**: multe probleme sunt *avertismente*, ca să puteți lucra în continuare cu
fișiere neterminate. Când doriți o verificare mai strictă:

- **Vizualizare → Mod parser strict** transformă problemele „permisive” în **erori**. Folosiți-l pentru o curățenie
  înainte de a partaja proiectul.

Fiecare diagnostic are un **cod stabil** (de ex. `TH0033`, `TH_SEM_015`, `TH2_004`). Catalogul complet
— fiecare cod, cu severitatea, mesajul și sursa sa — se află în documentul de referință
**[docs/diagnostics.md](../diagnostics.md)**. O orientare rapidă:

| Familie | Acoperă | Exemple |
|---|---|---|
| `TH00xx` | Parserul de bază (`.th` / `.thconfig`) | comandă necunoscută, `fix`/`equate`/`data` malformat, sistem de coordonate necunoscut |
| `TH2_xxx` | Desene `.th2` | tip necunoscut de punct/linie/arie, `line`/`area`/`scrap` neterminat |
| `TH_XVI_xxx` | Schițe `.xvi` | `set XVI…` necunoscut, țintă `-sketch` lipsă |
| `TH_SEM_xxx` | Semantică între fișiere | stație nerezolvată, fix duplicat, eroare de închidere a buclei, greșeală grosolană, **survey deconectat** |
| `THIDE_DIR_xxx` | Directive de aplicație `#@` | `#@region` / `#@endregion` fără pereche |
| `TH_WS_xxx` | Spațiul de lucru | cale inexistentă, niciun thconfig în dosar |

## Validare dincolo de un singur fișier

Familia `TH_SEM_*` este locul în care înțelegerea întregului proiect de către ThIDE dă roade. Ea semnalează lucruri pe care un singur
fișier nu le poate dezvălui:

- **Referințe la stații nerezolvate** (cu sugestii „ați vrut să spuneți”), validate în tot proiectul.
- **Fix-uri duplicate**, **rânduri de date** al căror număr de coloane sau ale căror valori nu se potrivesc cu ordinea citirilor.
- **Eroare de închidere a buclei** peste toleranță, vizări cu **greșeli grosolane/valori aberante**, neconcordanțe între
  **vizarea directă și cea inversă**.
- **Fișier negăsit** — o țintă `input`/`source` care nu există pe disc. Un clic pe avertisment sare la
  linia care o numește. La fel ca Therion, ThIDE încearcă numele așa cum este scris, apoi cu `.th`, apoi cu
  `.th2` — astfel `input cave` este satisfăcut de `cave.th` sau `cave.th2`, dar `input cave.th` nu este
  satisfăcut de `cave.th2`. Corectura rapidă **creează fișierul lipsă (create missing file)** creează `cave.th`.
- **Survey deconectat** (`TH_SEM_015`) — o porțiune a peșterii care nu este nici legată de rețeaua
  principală, nici georeferențiată printr-un `fix`, așa că *plutește*. Sunt numite ambele stații de capăt și fișierele sursă,
  ca să o puteți găsi și îmbina.

## Validare în timp ce tastați (opțional)

Implicit, revalidarea aprofundată a întregului thconfig rulează la **salvare**. Activați **Validează la tastare**
(comutator în bara de instrumente sau [Setări → Editor](19-settings-and-preferences.md#editor)) pentru a reanaliza
întregul arbore de fișiere la o scurtă pauză în tastare. Opțiunea este dezactivată implicit pentru că poate fi solicitantă în spațiile de lucru
mari — lăsați-o dezactivată dacă observați întârzieri și bazați-vă pe validarea la salvare.

## Corecturi rapide

Acolo unde ThIDE poate propune o reparație, **Remediere rapidă** (**Ctrl+.**) o oferă (și din **Paletă de comenzi**).
Când nu există nimic de corectat la poziția cursorului, vă anunță.

## Ajustarea anumitor verificări

Câteva verificări se pot configura din **Setări → Diagnostice** — de exemplu, dacă un `fix` local simplu
(fără sistem de coordonate) se consideră „ancorare” pentru o porțiune deconectată și, prin urmare, suprimă
`TH_SEM_015`. Consultați [Setări → Diagnostice](19-settings-and-preferences.md#vizualizare).

---

Următorul: [Compilare și rezultate →](10-compiling-and-output.md)
