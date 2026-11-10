# 10. Compilare și rezultate

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

La compilare, ThIDE predă proiectul dumneavoastră programului **Therion** propriu-zis, care produce
hărțile și modelele efective. ThIDE coordonează rularea și vă ușurează lucrul cu rezultatele.

> Pentru aceasta aveți nevoie de Therion instalat și detectat — consultați [Instalare și configurare](02-installation-and-setup.md).

## Rularea unei compilări

| Comandă | Unde | Ce face |
|---|---|---|
| **Compilează** | **Compilare → Compilează** (F5) · ciocanul din bara de instrumente · Paletă de comenzi | Compilează **thconfig-ul activ** (cel selectat în lista derulantă din Exploratorul spațiului de lucru), indiferent de fila care are focusul. Indicația sa îl numește. |
| **Compilează acest thconfig (fila activă)** | **Compilare → Compilează acest thconfig (fila activă)** · Paletă de comenzi | Compilează thconfig-ul deschis în fila care are focusul, atunci când nu este cel activ, fără să-l facă activ. |
| **Recompilează** | **Compilare → Recompilează** (Ctrl+F5) | Rulează din nou compilarea thconfig-ului activ (Therion procesează întotdeauna întregul thconfig; nu există un mod incremental de golit). |
| **Oprește compilarea** | **Compilare → Oprește compilarea** (Shift+F5) · bara de instrumente | Oprește o compilare în curs. |
| **Compilează ținta** | **Compilare → Compilează ținta** | Compilează o singură țintă de export a thconfig-ului activ. *Build target* („țintă de compilare”) este termenul folosit de Therion pentru o linie `export`, de aceea își păstrează numele. |
| **Compilează un anumit thconfig…** | Paletă de comenzi | Compilează o configurație diferită de cea activă. |

### Fișiere nesalvate

Dacă fișierele implicate în compilare au modificări nesalvate, ThIDE vă întreabă: **Salvează și
compilează** sau **Compilează fără salvare** (numind fișierele afectate). Preferați prima variantă. Puteți
face acest lucru automat cu *Salvează automat fișierele modificate înainte de compilare*
([Setări → Compilare și ieșire](19-settings-and-preferences.md#compilare-și-ieșire)).

### Compilare la salvare (opțional)

Activați *Compilează la salvare* (**Setări → Compilare și ieșire**) pentru o compilare amânată, în
fundal, a thconfig-ului activ la fiecare salvare — urmăriți indicatorul de activitate și panoul **Jurnal**.
Dezactivată implicit.

### Dosare de ieșire lipsă

Implicit, ThIDE **creează dosarele de ieșire lipsă** înainte de compilare (altfel Therion dă eroare la un
dosar inexistent); când face acest lucru, se scrie o linie în jurnal. Opțiunea se comută din **Setări →
Compilare și ieșire**.

## Urmărirea rulării: panoul Ieșire compilator

Panoul **Ieșire compilator** afișează în timp real mesajele lui Therion.

- Fila **Ieșire** — mesaje analizate, cu referințe `file:line` pe care se poate face clic și care sar direct
  la cauză.
- Fila **Brut** — textul nefiltrat al consolei.
- O **coloană de timp** pe care o puteți comuta între *De la ultima linie*, *Marcaj temporal* și *De la
  începutul compilării* — utilă pentru a vedea care etapă este lentă.
- **Deschide jurnalul** deschide în editor fișierul jurnal propriu al lui Therion.

Prima linie de ieșire numește thconfig-ul care se compilează (faceți clic pe ea pentru a deschide
fișierul). Bara de stare arată **Se compilează *nume*…** și apoi rezultatul; linia **Ultima compilare** din
tabloul de bord al Prezentării generale numește thconfig-ul compilat (după o repornire afișează *Nicio
compilare în această sesiune* până când compilați din nou). O notificare rezumă fișierele produse și
avertismentele (cu **Arată ieșirea**).

## Rezultatele: panoul Fișiere generate

**Fișiere generate** listează rezultatele unui singur thconfig, numit în antetul panoului (**Fișiere
generate de *nume* · compilat la *ora***). Panoul urmează thconfig-ul activ: treceți la altul și lista arată
ultimele rezultate ale acelui thconfig sau *„… nu a fost compilat încă.”*, cu un buton **Compilează**. După
**Compilează acest thconfig (fila activă)**, arată rezultatele acelei compilări până când se schimbă
thconfig-ul activ. Dacă o compilare eșuează, rezultatele ultimei compilări reușite rămân în listă și sunt
marcate ca **învechite**, și după o repornire.

Fiecare rezultat are coloanele **Nume fișier · Deschidere automată · Stare · Dimensiune · Modificat** și,
pe fiecare rând, butoane de acțiune:

- **Deschide extern** (Loch / Aven pentru modele; altfel aplicația implicită).
- **Vizualizează în vizualizatorul 3D intern** (pentru `.lox`/`.3d`) — consultați [Vizualizatorul 3D](11-viewers.md#vizualizatorul-3d).
- **Afișează în managerul de fișiere**.
- **Salt la definiție în thconfig** — sare la linia `export` care l-a produs.
- **Deschidere automată** — un control cu 3 stări pentru fiecare fișier: **▣ implicit** (folosește setarea
  globală) · **✓ întotdeauna** · **✗ niciodată**. Alegerea se păstrează de la o compilare la alta.

Dacă o intrare din spațiul de lucru este mai nouă decât un rezultat — și după o repornire, pentru că se
compară orele fișierelor —, o bandă de avertizare **învechit** vă anunță că rezultatele nu mai sunt la zi;
recompilați pentru a le reîmprospăta.

### Ce se deschide automat după o compilare

În **Setări → Compilare și ieșire → După o compilare reușită, deschide:** bifați oricare dintre **`.lox`**
(Loch 3D), **`.3d`** (Survex/Aven), **`.pdf`** (export de hartă). *Deschide fiecare ieșire corespunzătoare*
le deschide pe toate, nu doar pe prima. Opțiunile de Deschidere automată pentru fiecare fișier (de mai sus)
au prioritate față de aceste valori implicite. Lăsați totul nebifat ca să nu se deschidă nimic.

## Compilarea mai multor thconfig-uri deodată

**Compilare → Compilează mai multe…** listează fiecare thconfig al spațiului de lucru împreună cu liniile
sale `export`. Bifați thconfig-urile de compilat și, sub fiecare, exporturile de produs; ele sunt compilate
unul după altul (un thconfig cu unele exporturi nebifate este compilat dintr-o copie temporară în care
celelalte sunt transformate în comentarii). *Oprește la primul eșec* încheie rularea mai devreme;
**Renunță** o oprește. Un rezumat — rezultat, erori, avertismente, durată și fișiere produse pentru fiecare
thconfig — ajunge în panoul **Automatizare**, iar selecția dumneavoastră este reținută pentru proiect.
Din terminal: `therion-cli build caves --all` (adăugați `--json` pentru un rezumat ușor de prelucrat
automat).

## Exporturi punctuale: Export rapid

**Compilare → Export rapid…** compune un singur export fără să vă modifice thconfig-ul: alegeți un
**format**, opțional o **topografie** (gol = tot spațiul de lucru), o **scară** și un **nume de ieșire**.
ThIDE generează un thconfig temporar din spațiul de lucru activ, îl compilează și deschide rezultatul.
Excelent pentru un PDF rapid al unei singure ramuri.

## Deschiderea rezultatelor în aplicații externe

- **Compilare → Deschide în Loch** / **Deschide în Aven** pornește vizualizatorul extern 3D/2D pe rezultat.
- **Compilare → Deschide ultimul dosar de ieșire** afișează dosarul în managerul de fișiere.

## Unelte suplimentare din lanțul de compilare

Dacă Survex/Therion le pun la dispoziție, meniul Compilare oferă:

- **Unelte Survex (.3d):** **dump3d** (conținutul unui `.3d` ca text) și **extend** (profil desfășurat).
- **Informații Therion:** **Afișează versiunea** și **Afișează mediul** (și în Paleta de comenzi). Utile
  când vă verificați instalarea uneltelor sau raportați o eroare.

---

Următorul: [Vizualizatoarele: hărți, 3D și previzualizarea principală →](11-viewers.md)
