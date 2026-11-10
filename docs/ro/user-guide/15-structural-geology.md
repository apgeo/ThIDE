# 15. Geologie structurală

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md) · [Pagina principală a proiectului ThIDE](../../../README.md)

Modulul **Geologie structurală** transformă grupurile de vize care se află pe un plan geologic (o
falie, un plan de stratificație, o diaclază…) în măsurători de **direcție / înclinare** și reprezintă
planele rezultate în 3D. Deschideți-l din **Vizualizare → Geologie structurală**.

> **Dezactivat implicit.** Activați *Modul Geologie structurală* în
> [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare). Pentru metodă și calcule,
> vedeți documentul de referință **[docs/structural-geology.md](../../structural-geology.md)**.

Panoul este un **flux de lucru în cinci pași**, pe file:

## 1 · Detectare

ThIDE găsește vizele pe care le-ați făcut *de-a lungul* unui plan, pe baza unor **semnale de
detectare** pe care le puteți adapta la convenția topografiei dumneavoastră. O viză este considerată
structurală dacă se potrivește cu **oricare semnal activat**:

- **Numele stației de plecare conține unul dintre cuvintele-cheie** — de exemplu, denumiți stațiile
  de pe plan `fault1`, `bed2`.
- **Comentariul vizei conține unul dintre marcaje** — de exemplu, un comentariu `; bedding`.
- **Stația de plecare sau de sosire poartă unul dintre indicatori** — un indicator de stație pe care
  îl folosiți pentru punctele structurale.

Alegeți **Domeniu** (fișierul activ sau tot spațiul de lucru) și apăsați **Detectează ↻**.

## 2 · Măsurători

Punctele detectate, grupate în plane candidate. Bifați punctele de inclus în ajustarea fiecărui plan
— **bifarea sau debifarea recalculează planul respectiv în timp real**. Opțiuni:

- **Grupează un plan după** — cum sunt grupate punctele într-un plan (de exemplu, după stație).
- **Radieri** — include radialele.
- **Include punctul stației de plecare (origine) în ajustare** — doar când stația însăși se află pe
  plan; urmăriți coloana **RMS**.
- **Declinație magnetică** — opțional, rotește direcția / direcția de înclinare spre nordul adevărat,
  pe baza unei **surse** și a unei valori alese (înclinarea nu este afectată). Este dezactivată
  implicit, ca să nu adunați de două ori declinația proprie a unei topografii. Pe întregul proiect,
  sursa *declarată în topografie* folosește declinația din fișierul fiecărui plan, iar *WMM* data
  topografiei fiecărui plan, așa că linia de stare poate afișa un interval.

Ajutoare pentru selecția în bloc: **☑ Toate**, **☐ Niciuna**, **⇄ Inversează**, grupare după stație.
Dublu-clic pe un rând pentru a sări la viza sursă. **Export CSV…** salvează grila într-un fișier CSV.

## 3 · Plane rezultate

Planele ajustate, câte unul pe rând: **Înclinare °**, **Direcție °**, **Dir. înclinare °**,
**Ref. nord**, **Puncte** și **RMS** (calitatea ajustării — cu cât mai mică, cu atât mai bine).
Comutați indicatorul **Vizibil** al fiecărui plan pentru a-i arăta sau ascunde discul în graficul 3D;
**Export CSV…** salvează grila într-un fișier CSV.

## 4 · Grafic 3D

Planele, desenate ca **discuri** în 3D, alături de peșteră.

- **Încadrează vederea** reîncadrează imaginea; **Dimensiune disc** mărește discurile (sunt minuscule
  lângă o peșteră); **Fundal alb** pentru tipărire sau export.
- **Exportă imaginea…** salvează vederea curentă ca PNG.
- **Stereogramă în colț** afișează în colț o mică stereogramă „bol” care **se rotește împreună cu
  modelul peșterii** — cercul mare și polul fiecărui plan vizibil, în culoarea planului din grafic.
- **Scoate în panou separat** mută graficul într-un panou propriu, care poate fi andocat sau plutitor.

> Graficul 3D necesită resursele three.js incluse și un motor web de sistem (WebView2 / WebKit) —
> aceeași cerință ca pentru [Vizualizatorul 3D](11-viewers.md#vizualizatorul-3d). Dacă acestea lipsesc, fila
> afișează în schimb o notă.

## 5 · Stereogramă

O **proiecție stereografică** clasică pe emisfera inferioară a planelor ajustate — fiecare plan ca
**cerc mare** (și, opțional, **polul** său), pe o rețea de 10°. Este desenată nativ, deci funcționează
și acolo unde motorul web al graficului 3D nu este disponibil.

- **Proiecție** — **Wulff (unghiuri egale)**, rețeaua tradițională pentru lucrul cu unghiuri, sau
  **Schmidt (arii egale)**, standardul pentru densități și statistici.
- **Poli** — reprezintă polul fiecărui plan ca punct; **Măsurători** — reprezintă fiecare direcție de
  viză inclusă ca un punct mic (în culoarea planului său).
- **Grilă** / **Etichete** / **Fundal alb** — comutatoare de prezentare; **Exportă imaginea…**
  salvează un PNG clar.
- Treceți cu mouse-ul peste rețea pentru o **citire în timp real a azimutului și înclinării
  (trend/plunge)** în poziția cursorului; **faceți clic pe un cerc mare sau pe un pol** pentru a
  selecta planul respectiv — selecția este sincronizată cu grila „Plane rezultate”, cu graficul 3D și
  cu suprapunerea din Previzualizarea principală.
- **Scoate în panou separat** mută stereograma într-un panou propriu, care poate fi andocat sau
  plutitor (poate pluti pe un al doilea monitor, ca și celelalte panouri de previzualizare).

Când este activă o sursă de **declinație magnetică**, stereograma reprezintă direcția/înclinarea
corectată (față de nordul adevărat) — aceleași valori ca în grila „Plane rezultate”. Stereograma din
colțul graficului 3D rămâne în schimb în sistemul de referință propriu al topografiei, ca să
corespundă mereu liniilor peșterii cu care se rotește.

---

Următorul: [Harta relațională →](16-relational-map.md)
