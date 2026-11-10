# 22. Depanare și întrebări frecvente

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Întrebări frecvente și soluții. Dacă ceva de aici nu corespunde cu ce vedeți, aplicația este în stadiul
alpha și este posibil ca lucrurile să se fi mutat — vedeți [Despre acest ghid](about-this-guide.md)
pentru cum să raportați sau să corectați. Pentru o descriere a configurației generată de aplicație
(versiuni, unelte detectate, căi), folosiți **Ajutor → Informații de depanare** și
**Copiază în clipboard**.

## Configurare și unelte

**„Therion was not found” când compilez.**
ThIDE nu include Therion. Instalați-l, apoi setați-i calea în **Setări → Unelte externe** și apăsați
**Testează**. Vedeți [Instalare](02-installation-and-setup.md).

**Vizualizatorul 3D este gol sau afișează „indisponibil”.**
1. Activați-l: **Setări → Vizualizare → Vizualizator 3D încorporat**.
2. Asigurați-vă că există un motor web — Windows: WebView2 (inclus în Windows 11 / Edge modern);
   Linux: `apt install libwebkit2gtk-4.1-0`; macOS: inclus.
3. Dacă tot nu se afișează, folosiți butonul **Deschide extern (Loch / Aven)** al panoului. Referință:
   [docs/3d-viewer.md](../../3d-viewer.md).

**„Mapiah not found.”**
Instalați Mapiah și setați calea `mapiah` în **Setări → Unelte externe** (dialogul trimite acolo).

**Loch / Aven nu se deschid.**
Setați-le căile în **Setări → Unelte externe**; ThIDE verifică locațiile uzuale de instalare, dar nu
poate ghici una personalizată.

## Compilare

**Compilarea eșuează cu o eroare de director lipsă.**
Lăsați activată opțiunea **Creează dosarele de ieșire lipsă înainte de compilare**
([Setări → Compilare și ieșire](19-settings-and-preferences.md#compilare-și-ieșire)).

**Îmi tot cere să salvez înainte de compilare.**
Aceasta este întrebarea pentru fișierele nesalvate. Activați **Salvează automat fișierele modificate
înainte de compilare** ca să o evitați, sau alegeți pur și simplu **Salvează și compilează**.

**Rezultatele par vechi / a apărut un banner „învechit”.**
O intrare este mai nouă decât rezultatele (acest lucru rămâne valabil și după o repornire, pentru că se
bazează pe datele fișierelor) sau ultima compilare a eșuat. **Compilare → Recompilează**. Panoul
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
sistem de coordonate) și acest lucru vă convine, activați **Un fix local (fără cs) ancorează o topografie
deconectată** în [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare) (grupul
*Diagnostice*). Colorați [Previzualizarea principală](11-viewers.md#previzualizarea-principală) **după Componentă**
pentru a vedea bucata izolată.

**Calculatorul de declinație afișează „No magnetic model loaded”.**
Puneți un `WMM.COF` (NOAA, domeniu public) în dosarul de setări al ThIDE (panoul indică exact calea) sau
folosiți **Încarcă model…**. Vedeți
[Calculatoare](18-calculators.md#calculator-de-declinație).

## Performanță

**Totul merge greu pe un proiect mare.**
- Dezactivați previzualizările/analizele pe care nu le folosiți
  ([Setări → Vizualizare](19-settings-and-preferences.md#vizualizare), inclusiv grupul *Analiză
  topografică*).
- Lăsați dezactivate **Validează la tastare** și **Compilează la salvare**.
- Reduceți limitele din **Performanță** și **Timp maxim de încărcare la pornire**
  ([Setări → Performanță](19-settings-and-preferences.md#performanță)).
- Restrângeți domeniul pentru **Ctrl+P** la thconfig-ul activ
  ([Setări → Spațiu de lucru](19-settings-and-preferences.md#spațiu-de-lucru)).

**Un singur fișier uriaș nu este evidențiat sau analizat.**
A depășit o limită de mărime. Folosiți bannerul din fișier **Evidențiază oricum / Analizează oricum**
sau măriți limitele în Setări → Performanță.

## Fiabilitate și siguranță (ce faceți când ceva nu merge)

ThIDE face tot posibilul să nu vă piardă munca:

- **Recuperare după blocare / mod sigur.** Dacă aplicația s-a închis neașteptat cu modificări
  nesalvate, la următoarea pornire apare, fixat deasupra editorului, un **banner de recuperare** care
  numește fișierele ale căror modificări nesalvate au fost păstrate, cu **Restaurează** pentru a le
  readuce conținutul și **Șterge copiile** pentru a renunța la ele (întreabă înainte de a le șterge).
  Rămâne afișat până răspundeți, așa că nu poate fi ratat nici pe un ecran mic.
- **Banner de modificare externă.** Dacă un fișier deschis se modifică pe disc (alt program, o operație
  `git`, Mapiah), ThIDE afișează un banner: **Reîncarcă** (preia versiunea de pe disc), **Compară**
  (diferențe alăturate), **Păstrează versiunea mea (salvează)** (scrie versiunea dumneavoastră peste cea
  de pe disc) sau **Închide**. Reîncărcarea unui fișier cu editări nesalvate cere mai întâi confirmare
  (*Reîncărcarea va renunța la modificările nesalvate*). Nu vă suprascrie niciodată modificările pe
  tăcute. O modificare care atinge doar permisiunile fișierului nu afișează niciun banner.
  (Reîncărcarea automată a fișierelor modificate extern este o opțiune din
  [Setări → Performanță](19-settings-and-preferences.md#performanță).)
- **Confirmare la ștergere.** Ștergerea unui fișier din Exploratorul spațiului de lucru cere mai întâi
  confirmare (coș de gunoi sau ștergere definitivă).
- **Uneltele care modifică mai multe fișiere pot fi anulate.** Redenumirea, Înlocuiește în fișiere,
  Ieșire nouă, Leagă un fișier de topografie, Renumerotează stațiile, Formatează toate fișierele,
  Normalizează proiectul și *Corectează toate* scriu direct pe disc fișierele care nu sunt deschise;
  **Editare → Anulează modificarea din mai multe fișiere** le readuce pe toate
  ([detalii](05-interface-tour.md#anularea-unei-unelte-care-a-scris-în-mai-multe-fișiere)).
- **Protecții la deschidere.** Fișierele binare sau prea mari cer confirmare înainte de a fi deschise ca
  text (**Deschide oricum**), ca să nu încărcați din greșeală un bloc uriaș de date.

## Aranjament și interfață

**Un panou a dispărut / aranjamentul meu e haotic.**
**Vizualizare → Aspect → Restaurează aspectul implicit**. Comutați orice panou individual din meniul
**Vizualizare** sau din Paleta de comenzi. Editările nesalvate sunt scrise în datele de recuperare după
blocare înainte ca aranjamentul să fie reconstruit.

**Am închis o filă din greșeală.**
**Fișier → Redeschide fila închisă**.

**Unde sunt stocate setările mele?**
În dosarul de setări al ThIDE (`%AppData%\ThIDE` pe Windows, `~/.config/ThIDE` pe Linux) — preferințe,
scurtături de tastatură, indexul de simboluri, conținuturile pentru recuperare după blocare, pluginuri și
fișierele auxiliare per proiect. Vedeți
[Instalare](02-installation-and-setup.md#6-unde-își-stochează-thide-setările).

## Tot nu ați rezolvat?

- Verificați panoul **Jurnal** (**Vizualizare → Jurnal**) pentru erori apărute în fundal.
- Copiați **Ajutor → Informații de depanare** și includeți-le în orice raport de eroare.
- Consultați documentele de referință din [`docs/`](../../README.md) și **Cartea Therion** inclusă
  (**Ajutor → Cartea Therion**).

---

[← Înapoi la pagina principală a Ghidului utilizatorului](README.md)
