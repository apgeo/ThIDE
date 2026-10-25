# 15. Geologie structurală

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md) · [Pagina principală a proiectului ThIDE](../../../README.md)

Modulul **Geologie structurală** transformă grupurile de vizări care se află pe un plan geologic (o
falie, un plan de stratificație, o diaclază…) în măsurători de **direcție / înclinare** și reprezintă
planele rezultate în 3D. Deschideți-l din **Vizualizare → Geologie structurală**.

> **Dezactivat implicit.** Activați *Modul Geologie structurală* în
> [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare). Pentru metodă și calcule
> matematice, vedeți documentul de referință **[docs/structural-geology.md](../structural-geology.md)**.

Panoul este un **flux de lucru în cinci pași**, pe file:

## 1 · Detectare

ThIDE găsește vizările pe care le-ați făcut *de-a lungul* unui plan, pe baza unor **semnale de
detectare** pe care le puteți adapta la convenția ridicării dumneavoastră. O vizare este considerată
structurală dacă se potrivește cu **oricare semnal activat**:

- **Numele stației de plecare conține cuvânt(e)-cheie** — de exemplu, denumiți stațiile de plan
  `fault1`, `bed2`.
- **Comentariul vizei conține marcaj(e)** — de exemplu, un comentariu `; bedding`.
- **Stația de la/până la poartă indicator(i)** — un indicator de stație pe care îl folosiți pentru
  punctele structurale.

Setați **Domeniu** (fișierul activ sau întregul spațiu de lucru) și apăsați **Detectează ↻**.

## 2 · Măsurători

Punctele detectate, grupate în plane candidate. Bifați punctele de inclus în ajustarea fiecărui plan —
**comutarea recalculează planul respectiv în timp real**. Opțiuni:

- **Grupează un plan după** — cum sunt grupate punctele într-un plan (de exemplu, după stație).
- **Radieri** — include splay-urile.
- **Include punctul stației de plecare (origine) în ajustare** — doar când stația însăși se află pe
  plan; urmăriți coloana **RMS**.
- **Declinație magnetică** — opțional, rotește direcția / direcția de înclinare spre nordul geografic,
  pornind de la o **sursă** și o valoare alese (înclinarea nu este afectată). Dezactivată implicit,
  ca să nu contabilizați de două ori declinația proprie a unui survey.

Ajutoare pentru selecția în bloc: **☑ Toate**, **☐ Niciuna**, **⇄ Inversează**, grupare după stație.
Dublu clic pe un rând pentru a sări la vizarea sursă. **Exportă** grila în CSV.

## 3 · Plane rezultate

Planele ajustate, câte unul pe rând: **Înclinare °**, **Direcție °**, **Dir. înclinare °**,
**Ref. nord**, **Puncte** și **RMS** (calitatea ajustării — cu cât mai mic, cu atât mai bine). Comutați
indicatorul **Vizibil** al fiecărui plan pentru a afișa/ascunde discul său în graficul 3D; **Exportă**
grila în CSV.

## 4 · Grafic 3D

Planele desenate ca **discuri** în 3D, peste peșteră.

- **Încadrează vederea** reîncadrează; **Dimensiune disc** mărește discurile (sunt minuscule lângă o
  peșteră); **Fundal alb** pentru tipărire/export.
- **Exportă imaginea…** salvează vederea curentă ca PNG.
- **Stereogramă în colț** afișează o mică „cupă” de stereogramă în colț, care **se rotește împreună cu
  modelul peșterii** — cercul mare și polul fiecărui plan vizibil, în culoarea de reprezentare a
  planului.
- **Scoate în panou separat** mută graficul într-un panou propriu, andocabil/flotant.

> Graficul 3D necesită resursele three.js incluse și un motor web de sistem (WebView2 / WebKit) —
> aceeași cerință ca pentru [Vizualizatorul 3D](11-viewers.md#vizualizator-3d). Dacă nu sunt disponibile,
> fila afișează o notă în loc.

## 5 · Stereogramă

O **proiecție stereografică** clasică pe emisfera inferioară a planelor ajustate — fiecare plan ca
**cerc mare** (și, opțional, **polul** său), pe o rețea de 10°. Este randată nativ, deci funcționează
chiar și acolo unde motorul web al graficului 3D nu este disponibil.

- **Proiecție** — **Wulff (unghiuri egale)**, rețeaua tradițională pentru lucrul cu unghiuri, sau
  **Schmidt (arii egale)**, standard pentru densitate/statistică.
- **Poli** — reprezintă punctul polului fiecărui plan; **Măsurători** — reprezintă fiecare direcție de
  vizare inclusă ca un punct mic (în culoarea planului său).
- **Grilă** / **Etichete** / **Fundal alb** — comutatoare de prezentare; **Exportă imaginea…** salvează
  un PNG clar.
- Treceți cu mouse-ul peste rețea pentru o **citire live a azimutului/plonjului** poziției cursorului;
  **faceți clic pe un cerc mare sau pe un pol** pentru a selecta acel plan — selecția este sincronizată
  cu grila Plane rezultate, cu graficul 3D și cu suprapunerea din Previzualizare principală.
- **Scoate în panou separat** mută stereograma într-un panou propriu, andocabil/flotant (poate pluti pe
  un al doilea monitor, ca celelalte panouri de previzualizare).

Când este activă o sursă de **declinație magnetică**, stereograma reprezintă direcția/înclinarea
corectate (față de nordul geografic) — aceleași valori afișate în grila Plane rezultate. Stereograma
din colțul graficului 3D rămâne în schimb în sistemul de referință propriu al survey-ului, ca să se
potrivească mereu cu liniile peșterii împreună cu care se rotește.

---

Următorul: [Harta relațională →](16-relational-map.md)
