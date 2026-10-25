# 10. Compilare și rezultate

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

La compilare, ThIDE predă proiectul programului **Therion** propriu-zis, care produce hărțile și
modelele efective. ThIDE coordonează rularea și face ca rezultatele să fie ușor de folosit.

> Pentru aceasta aveți nevoie ca Therion să fie instalat și detectat — consultați [Instalare și configurare](02-installation-and-setup.md).

## Rularea unei compilări

| Comandă | Unde | Ce face |
|---|---|---|
| **Compilează** | Meniul **Compilare** · ciocanul din bara de instrumente · **Paletă de comenzi** | Compilează **thconfig-ul activ**. |
| **Recompilează** | Meniul **Compilare** | Forțează o recompilare completă, de la zero. |
| **Anulează** | Meniul **Compilare** · bara de instrumente | Oprește o compilare în curs. |
| **Compilează ținta: …** | **Compilare → Compilează ținta** | Compilează o anumită țintă din configurație. |
| **Compilează un anumit thconfig…** | **Paletă de comenzi** | Compilează o altă configurație decât cea activă. |

### Fișiere nesalvate

Dacă fișierele implicate în compilare au modificări nesalvate, ThIDE vă întreabă: **Salvează și compilează** sau **Compilează
fără salvare** (numind fișierele afectate). Preferați prima variantă. Puteți automatiza acest pas cu
*Salvează automat fișierele modificate înainte de compilare* ([Setări → Compilare & ieșire](19-settings-and-preferences.md#compilare-și-ieșire)).

### Compilare la salvare (opțional)

Activați *Compilează la salvare* (**Setări → Compilare & ieșire**) pentru o recompilare în fundal, cu temporizare, a thconfig-ului
activ de fiecare dată când salvați — urmăriți indicatorul de activitate și panoul **Jurnal**. Dezactivată implicit.

### Dosare de ieșire lipsă

Implicit, ThIDE **creează dosarele de ieșire lipsă** înainte de compilare (altfel Therion dă eroare la un
dosar inexistent); când face acest lucru, se înregistrează o linie în jurnal. Opțiunea se comută din **Setări → Compilare & ieșire**.

## Urmărirea rulării: panoul Ieșire compilator

Panoul **Ieșire compilator** afișează în timp real mesajele lui Therion.

- Fila **Ieșire** — mesaje interpretate, cu referințe `file:line` pe care se poate face clic și care duc direct la cauză.
- Fila **Brut** — textul nefiltrat al consolei.
- O **coloană de timp** pe care o puteți comuta între *De la ultima linie*, *Marcaj temporal* și *De la începutul compilării* —
  utilă pentru a observa care etapă este lentă.
- **Deschide jurnalul** deschide în editor fișierul jurnal propriu al lui Therion.

Bara de stare afișează **Compilare în curs…**, apoi **Ultima compilare a reușit/a eșuat**; o notificare
rezumă artefactele și avertismentele (cu **Arată ieșirea**).

## Rezultatele: panoul Fișiere generate

Tot ce a produs compilarea este listat în **Fișiere generate**, cu coloanele **Nume fișier · Deschidere automată
· Stare · Dimensiune · Modificat** și, pe fiecare rând, butoane de acțiune:

- **Deschide extern** (Loch / Aven pentru modele; altfel aplicația implicită).
- **Vizualizează în vizualizatorul 3D intern** (pentru `.lox`/`.3d`) — consultați [Vizualizatorul 3D](11-viewers.md#vizualizator-3d).
- **Afișează în managerul de fișiere**.
- **Salt la definiție în thconfig** — sare la linia `export` care l-a produs.
- **Deschidere automată** — un control cu 3 stări pentru fiecare fișier: **▣ implicit** (folosește setarea globală) · **✓ întotdeauna** ·
  **✗ niciodată**. Alegerea se păstrează de la o compilare la alta.

Dacă o intrare din spațiul de lucru s-a schimbat de la ultima compilare, un banner de **ieșiri învechite** avertizează că rezultatele nu mai sunt
actuale — recompilați pentru a le reîmprospăta.

### Ce se deschide automat după o compilare

În **Setări → Compilare & ieșire → După o compilare reușită, deschide:** bifați oricare dintre **`.lox`** (Loch 3D),
**`.3d`** (Survex/Aven), **`.pdf`** (export de hartă). *Deschide fiecare ieșire corespunzătoare* le deschide pe toate, nu
doar pe prima. Setările de **Deschidere automată** pe fișier (de mai sus) au prioritate față de aceste valori implicite. Lăsați totul nebifat
pentru a nu deschide nimic.

## Exporturi punctuale: Export rapid

**Compilare → Export rapid…** compune un singur export fără să vă editeze thconfig-ul: alegeți un **format**,
opțional o **topografie** (gol = întregul spațiu de lucru), o **scară** și un **nume de ieșire**. ThIDE generează
un thconfig temporar din spațiul de lucru activ, îl compilează și deschide rezultatul. Excelent pentru un PDF rapid
al unei singure ramuri.

## Deschiderea rezultatelor în aplicații externe

- **Compilare → Deschide în Loch** / **Deschide în Aven** pornește vizualizatorul extern 3D/2D pe rezultat.
- **Compilare → Deschide ultimul dosar de ieșire** afișează dosarul în managerul de fișiere.

## Unelte suplimentare din lanțul de instrumente

Dacă Survex/Therion le oferă, meniul **Compilare** pune la dispoziție:

- **Unelte Survex (.3d):** **dump3d** (dump text al unui `.3d`) și **extend** (elevație extinsă).
- **Informații Therion:** **Afișează versiunea** și **Afișează mediul** (și din **Paletă de comenzi**). Utile
  când vă verificați configurarea lanțului de instrumente sau când raportați o eroare.

---

Următorul: [Vizualizatoarele: hărți, 3D și previzualizare principală →](11-viewers.md)
