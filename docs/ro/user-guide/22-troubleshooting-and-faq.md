# 22. Depanare și întrebări frecvente

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Întrebări frecvente și soluții. Dacă ceva de aici nu corespunde cu ce vedeți, aplicația este în
stadiul alpha și este posibil să se fi schimbat — vedeți [Despre acest ghid](about-this-guide.md)
pentru cum să raportați sau să corectați. Pentru un rezumat tehnic al configurației (versiuni,
instrumente detectate, căi), folosiți **Ajutor → Informații de depanare** și **Copiază în clipboard**.

## Configurare și instrumente

**„Therion was not found” când compilez.**
ThIDE nu include Therion. Instalați-l, apoi setați calea în **Setări → Unelte externe** și apăsați
**Testează**. Vedeți [Instalare](02-installation-and-setup.md).

**Vizualizatorul 3D este gol sau afișează „indisponibil”.**
1. Activați-l: **Setări → Vizualizare → Vizualizator 3D încorporat**.
2. Asigurați-vă că există un motor web — Windows: WebView2 (inclus în Windows 11 / Edge modern);
   Linux: `apt install libwebkit2gtk-4.1-0`; macOS: inclus.
3. Pe Linux, actualizați la **0.3.0-alpha.2 sau mai nou**: versiunile anterioare puteau lăsa panoul
   gol sau blocat la *„Se încarcă …”* până la redimensionarea ferestrei. Dacă panoul este încă alb
   sau negru, vedeți opțiunile **Motor web încorporat** din
   [Setări → Depanare](19-settings-and-preferences.md#depanare).
4. Dacă tot nu se afișează, folosiți butonul **Deschide modelul extern (Loch / Aven)** al panoului.
   Referință: [docs/3d-viewer.md](../3d-viewer.md).

**Pe Linux sau macOS, „Open with ThIDE” (sau `ThIDE /path/to/file`) pornește aplicația, dar nu deschide nimic.**
Corectat în 0.3.0-alpha.2: căile absolute erau confundate cu opțiuni în stil Windows. Actualizați
sau transmiteți o cale relativă.

**„File not found” pe o linie `input` al cărei fișier există în mod evident.**
Verificați extensia. `input cave` găsește `cave.th` sau `cave.th2`; `input cave.th` necesită exact
`cave.th`. Versiunile anterioare 0.3.0-alpha.2 nu adăugau deloc extensia implicită.

**„Mapiah not found.”**
Instalați Mapiah și setați calea `mapiah` în **Setări → Unelte externe** (dialogul trimite acolo).

**Loch / Aven nu se deschid.**
Setați căile lor în **Setări → Unelte externe**; ThIDE verifică locațiile uzuale de instalare, dar nu
poate ghici una personalizată.

## Compilare

**Compilarea eșuează cu o eroare de dosar lipsă.**
Lăsați activată opțiunea **Creează dosarele de ieșire lipsă înainte de compilare**
([Setări → Compilare & ieșire](19-settings-and-preferences.md#compilare-și-ieșire)).

**Îmi tot cere să salvez înainte de compilare.**
Acesta este mesajul pentru fișiere nesalvate. Activați **Salvează automat fișierele modificate înainte
de compilare** ca să îl evitați, sau alegeți pur și simplu **Salvează și compilează**.

**Rezultatele par vechi / a apărut un banner „învechit” (stale).**
O intrare s-a modificat de la ultima compilare. **Recompilează**. Panoul
[Fișiere generate](10-compiling-and-output.md#rezultatele-panoul-fișiere-generate) marchează
rezultatele învechite.

## Cifre și model

**Lungimea/adâncimea din ThIDE nu corespund cu cele din Therion.**
Este de așteptat. Analizele și previzualizările din aplicație sunt de **calitate de previzualizare**
(fără compensarea buclelor), pentru un răspuns instantaneu. **Therion este sursa de adevăr** pentru
valorile compensate — compilați pentru cifrele reale.
Vedeți [Concepte de bază](04-core-concepts.md#graful-de-obiecte-modelul-semantic).

**Avertisment „Disconnected survey” (TH_SEM_015) pe date pe care le consider corecte.**
Bucata nu este legată de rețeaua principală *și* nu este georeferențiată. Fie adăugați `equate`-ul
lipsă pentru a o lega, fie aplicați-i un `fix`. Dacă este ancorată doar printr-un `fix` simplu (fără
sistem de coordonate) și acest lucru vă convine, activați **Un fix local (fără cs) ancorează o
topografie deconectată** în [Setări → Diagnostice](19-settings-and-preferences.md#vizualizare).
Colorați [Previzualizarea principală](11-viewers.md#previzualizare-principală) **după Componentă** pentru a
vedea bucata izolată.

**Calculatorul de declinație afișează „No magnetic model loaded”.**
Puneți un `WMM.COF` (NOAA, domeniu public) în `%AppData%/ThIDE` sau folosiți **Încarcă model…**.
Vedeți [Calculatoare](18-calculators.md#calculatorul-de-declinație).

## Performanță

**Totul merge greu pe un proiect mare.**
- Dezactivați previzualizările/analizele pe care nu le folosiți
  ([Setări → Vizualizare / Analiză topografică](19-settings-and-preferences.md#vizualizare)).
- Lăsați dezactivate **Validează la tastare** și **Compilează la salvare**.
- Reduceți limitele din **Performanță** și **Timp maxim de încărcare la pornire**
  ([Setări → Performanță](19-settings-and-preferences.md#performanță)).
- Restrângeți domeniul pentru **Ctrl+P** la thconfig-ul activ
  ([Setări → Proiect](19-settings-and-preferences.md#proiect)).

**Un singur fișier uriaș nu este evidențiat sau analizat.**
A depășit o limită de mărime. Folosiți bannerul din fișier **Evidențiază oricum / Analizează oricum**
sau măriți limitele în Setări → Performanță.

## Fiabilitate și siguranță (ce faceți când ceva nu merge)

ThIDE face tot posibilul să nu vă piardă munca:

- **Recuperare după blocare / mod sigur.** Dacă aplicația s-a închis neașteptat cu modificări
  nesalvate, la următoarea pornire o notificare — *„Modificări nesalvate recuperate”* — oferă
  **Restaurează** pentru a readuce acele conținuturi.
- **Banner de modificare externă.** Dacă un fișier deschis se modifică pe disc (alt program, o
  operație `git`, Mapiah), ThIDE afișează un banner: **Compară** (diferențe alăturate), **Păstrează
  versiunea mea**, **Păstrează versiunea mea (salvează)** sau **Renunță & reîncarcă**. Nu vă
  suprascrie niciodată modificările pe tăcute. (Reîncărcarea automată a fișierelor modificate extern
  este o opțiune din [Setări → Performanță](19-settings-and-preferences.md#performanță).)
- **Confirmare la ștergere.** Ștergerea unui fișier din Exploratorul de proiect cere mai întâi
  confirmare (coș de gunoi vs. definitiv).
- **Protecții la deschidere.** Fișierele binare sau prea mari cer confirmare înainte de a fi deschise
  ca text (**Deschide oricum**), ca să nu încărcați din greșeală un bloc uriaș de date.

## Aranjament și interfață

**Un panou a dispărut / aranjamentul meu e haotic.**
**Vizualizare → Resetează la aranjamentul implicit**. Comutați orice panou individual din meniul
**Vizualizare** sau din Paleta de comenzi.

**Am închis o filă din greșeală.**
**Fișier → Redeschide fila închisă**.

**Unde sunt stocate setările mele?**
În `%AppData%/ThIDE` (Windows) sau în echivalentele XDG / `~/Library` — preferințe, scurtături,
indexul de simboluri, conținuturile pentru recuperare după blocare, pluginuri și fișierele auxiliare
per proiect. Vedeți [Instalare](02-installation-and-setup.md#6-unde-își-stochează-thide-setările).

## Tot nu ați rezolvat?

- Verificați panoul **Jurnal** (**Vizualizare → Jurnal**) pentru erori din fundal.
- Copiați **Ajutor → Informații de depanare** și includeți-le în orice raport de eroare.
- Consultați documentele de referință din [`docs/`](../README.md) și **Cartea Therion** inclusă
  (**Ajutor → Cartea Therion**).

---

[← Înapoi la pagina principală a Ghidului utilizatorului](README.md)
