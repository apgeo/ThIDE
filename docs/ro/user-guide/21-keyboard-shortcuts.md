# 21. Scurtături tastatură

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Scurtăturile de mai jos sunt cele **implicite**. Aproape fiecare poate fi **reatribuită** în
**Setări → Scurtături tastatură** — faceți clic în câmpul de combinație al unei comenzi și apăsați
combinația dorită. Acea pagină de setări este lista autoritară și mereu actuală; pagina de față este o
referință rapidă.

> Pe macOS, citiți **Ctrl** ca **⌘** acolo unde combinațiile dumneavoastră îl mapează astfel.

**Nu toate comenzile vin cu o combinație de taste.** Multe dintre ele — *Deschide fișier*, *Deschide
dosar*, *Împarte editorul*, *Export rapid*, *Duplică linia (liniile)*, comutatoarele de panouri și
altele — sunt în mod deliberat **neatribuite implicit** și se pot accesa din meniuri sau din Paleta de
comenzi. Ele apar totuși ca rânduri în **Setări → Scurtături tastatură**, deci le puteți atribui ce
doriți. Dacă o scurtătură pe care o așteptați de la alt editor nu face nimic, acesta este motivul —
atribuiți-o acolo.

## Fișiere și navigare

| Scurtătură | Comandă |
|---|---|
| `Ctrl+S` | Salvează |
| `Ctrl+P` | Salt la fișier (deschidere rapidă) |
| `Ctrl+Shift+P` | Paletă de comenzi (Salt la acțiune) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Parcurge documentele și panourile deschise, cele mai recente întâi (țineți `Ctrl`, apăsați `Tab`) |
| `Ctrl+Shift+T` | Redeschide fila închisă |
| `Alt+Left` / `Alt+Right` | Navighează Înapoi / Înainte |

## Editare

| Scurtătură | Comandă |
|---|---|
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | Decupează / Copiază / Lipește |
| `Ctrl+/` | Comutare comentariu |
| `Ctrl+Shift+R` | Include în regiune |
| `Shift+Alt+F` | Formatează documentul |
| `Ctrl+.` | Remediere rapidă |
| `Ctrl+Space` | Declanșează completarea |
| `Ctrl+G` | Salt la linie |

## Inteligența codului

| Scurtătură | Comandă |
|---|---|
| `F12` | Salt la definiție |
| `Shift+F12` | Găsește referințe |
| `F2` | Redenumește simbolul (în tot proiectul) |
| `Alt+F12` | Vezi definiția |
| `Ctrl+]` | Salt la blocul pereche |
| `Alt+↓` / `Alt+↑` | Intră în fișierul inclus / Ieși din fișierul inclus |

## Căutare și diagnostice

| Scurtătură | Comandă |
|---|---|
| `Ctrl+Shift+F` | Caută în fișiere |
| `Ctrl+Shift+H` | Înlocuiește în fișiere |
| `F8` / `Shift+F8` | Diagnosticul următor / anterior |
| `Ctrl+Shift+D` | Comută panoul Diagnostice |

## Compilare și vizualizatoare externe

| Scurtătură | Comandă |
|---|---|
| `F5` | Compilează thconfig-ul activ |
| `Ctrl+F5` | Recompilează (forțează o recompilare completă) |
| `Shift+F5` | Anulează compilarea în curs |
| `F9` | Deschide modelul compilat în **Loch** |
| `F10` | Deschide modelul compilat în **Aven** |

Vedeți [Compilare și rezultate](10-compiling-and-output.md).

## Panouri și fereastră

| Scurtătură | Comandă |
|---|---|
| `Ctrl+Alt+L` | Comută Exploratorul de proiect |
| `Ctrl+Shift+D` | Comută panoul Diagnostice |
| `F11` | Comută ecranul complet |

Celelalte comutatoare de panouri (Navigator obiecte, Contur, Proiect, Jurnal, Previzualizare
principală, Vizualizator hărți, Vizualizator 3D, Geologie structurală, Animație Blender) vin
**neatribuite** — folosiți meniul **Vizualizare** sau atribuiți-le singur în **Setări → Scurtături
tastatură**.

## Prefixele Paletei de comenzi

În paletă (`Ctrl+Shift+P`):

| Tastați | Sare la |
|---|---|
| `@name` | Un simbol din documentul curent |
| `#name` | Un simbol din întregul spațiu de lucru |
| `:42` | Linia 42 din fișierul curent |

## Convenții pentru mouse

| Acțiune | Efect |
|---|---|
| **Ctrl+clic** pe un identificator | Salt la definiție (întotdeauna, indiferent de setarea de navigare la clic) |
| **Dublu-clic** pe un identificator | Salt la definiție (sau plasarea cursorului — vedeți [Setări → Editor](19-settings-and-preferences.md#editor)) |
| **Ctrl+derulare** într-un vizualizator | Zoom (Vizualizator hărți, Hartă relațională) |
| **Dublu-clic** pe un rând de grilă/tabel | Salt la sursă |

---

Următorul: [Depanare și întrebări frecvente →](22-troubleshooting-and-faq.md)
