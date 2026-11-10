# 17. Import, export și GIS

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE face legătura între Therion și celelalte formate din trusa unui speolog — alte programe de
topografie, GIS, GPS și date despre teren. Majoritatea se află în meniul **Unelte**.

## Import în Therion

Cele trei comenzi de **import** din meniul **Unelte** convertesc date externe în construcții Therion
pe care le puteți apoi lega în proiect:

| Import | Format sursă | Devine |
|---|---|---|
| **Unelte → Importă topografie (Survex / Compass)…** | `.svx` (Survex), `.dat` (Compass), `.csv`/`.tsv` (un tabel de vize) | O topografie Therion `.th` |
| **Unelte → Importă DEM ca suprafață (.asc)…** | Grilă ESRI ASCII | Un bloc Therion `surface` |
| **Unelte → Importă puncte GPX (→ fix)…** | `.gpx` | Linii `fix` (stații georeferențiate) |

După import, adăugați fișierul nou în thconfig (`input`/`source`) și compilați. Dacă un import
eșuează, o notificare vă spune de ce.

### Tabele de vize (foi de calcul, CSV, PocketTopo, DistoX)

**Editare → Lipește ca date de vize…** (disponibilă și în meniul contextual al editorului, la clic
dreapta) transformă un tabel de vize într-un bloc Therion `data`: selecția, dacă există una, altfel
conținutul clipboardului. Dialogul arată tabelul și blocul unul lângă altul; modificați setările
oricărei părți, iar blocul se actualizează.

- **Coloanele** se iau dintr-un rând de antet (`From`, `To`, `Length (m)`, `Azimuth`, `Inclination`,
  `Left`… și sinonimele obișnuite) sau, în lipsa lui, din numărul de coloane: 3 = length compass
  clino, 5 = from to length compass clino, 9 = la fel, plus left right up down. Scrieți propria listă
  (`to from tape bearing gradient -`, unde `-` sare o coloană) când presupunerea este greșită.
- **Separatori** — taburi, punct și virgulă, virgule (sunt permise celule între ghilimele) sau
  spații; virgula zecimală (`3,25`) este recunoscută.
- **Unități** — `ft` / `feet` în antetul lungimii scrie `units length feet`; valorile de azimut peste
  360 (și niciuna peste 400) sunt citite ca grade centesimale. Puteți schimba oricare dintre ele în
  dialog.
- **Un tabel fără stații** (o descărcare din instrument) primește numele stațiilor pornind de la
  *Prima stație* — sugerată fiind ultima stație dinaintea cursorului —, numărând în sus. Trei vize
  aproape identice la rând (obiceiul DistoX) devin un singur tronson mediat, iar vizele singure devin
  radiale din stația curentă; setați *Vize pe segment* la 0 pentru ca fiecare viză să devină tronson.
- **Verificări** — lungimi negative sau foarte mari, azimut sau înclinare în afara domeniului, un
  tronson de la o stație la ea însăși, celule care nu sunt numere: fiecare este listată cu linia ei.
  Un rând căruia îi lipsește o valoare este citit ca radială doar într-un tabel în care niciun rând nu
  are lungimea completă (PocketTopo); în orice alt tabel este raportat ca tronson cu o valoare lipsă
  și este omis. Marcați radialele cu `-` în coloana *to*.
- **Unde ajunge** — dialogul numește locul deasupra blocului (*Se inserează în: fișier, centreline de
  la linia N*). Cu cursorul (sau selecția) într-un `centreline`, blocul merge acolo; în altă parte
  dintr-o topografie, merge în ultimul `centreline` al topografiei respective, înainte de
  `endcentreline`, sau într-unul nou la sfârșitul topografiei. În afara oricărei topografii, dialogul
  refuză și spune acest lucru, în loc să scrie blocul deasupra liniei `encoding`.

Același cititor importă un fișier `.csv`/`.tsv` ca topografie nouă (**Importă topografie…**,
`import_survey` sau `therion-cli import`).

### Dosarul de intrare

Activați **Unelte → Urmărește dosarul de intrare**, iar ThIDE urmărește dosarul `incoming/` din proiect
(creează dosarul; setarea este păstrată în fișierul `.thide/project.json` al proiectului, unde
`incoming.folder` poate numi alt dosar). Direcționați acolo dosarul de sincronizare al telefonului sau
dosarul de descărcări: când un export de topografie a sosit complet, o notificare oferă **Importă…** —
un `.th` deschide [Leagă un fișier de topografie](24-linking-surveys.md) pentru el, un `.svx` Survex
sau un `.dat` Compass este mai întâi convertit și apoi legat, iar un `.gpx` devine un fișier de stații
fixe. O schiță `.th2` sosită singură este doar anunțată: legați mai întâi topografia ei și copiați
schița împreună cu ea. Un `.svx`, `.dat` sau `.gpx` tras în fereastră urmează același drum.
Urmărirea este dezactivată până o activați.

### Un proiect în jurul unei topografii izolate (TopoDroid)

O topografie exportată din TopoDroid (sau orice `.th` izolat, cu un singur bloc `survey`) nu se
compilează singură până nu are un thconfig în jurul ei. **Unelte → Generează proiect din
topografie…** construiește acest proiect pentru dumneavoastră; **Generează proiect din fișierul
activ** face același lucru pentru fișierul `.th` deschis în editor.

Dialogul este precompletat din fișier: numele proiectului este numele topografiei fără sufixul final
`_YYYY` (TopoDroid denumește adesea topografiile `cave_2025`), iar titlul și o probabilă stație de
intrare sunt preluate din topografie. Dumneavoastră alegeți:

- **Georeferențiere (opțional)** — stația de intrare, sistemul de coordonate (`lat-long`, `UTM35N`,
  `EPSG:4326`, …), cele două coordonate în ordinea sistemului respectiv și altitudinea. Necesară
  pentru KML, shapefile și alte rezultate GIS. Dacă o lăsați goală, fișierul de legătură primește un
  șablon `fix` comentat, de completat mai târziu.
- **Layout și structură proiect** — un bloc `layout` cu scara hărții și legenda și, opțional, un dosar
  pentru grafică.
- **Ținte de export** — bifați rezultatele pe care trebuie să le producă thconfig-ul: modele 3D (Loch
  `.lox`, Survex `.3d`, KML, shapefile, Compass `.plt`, VRML, DXF, 3DMF), hărți (PDF în plan sau
  profil desfășurat, SVG, XHTML), baza de date SQL sau CSV și o listă a topografiilor.

**Creează proiect** cere apoi dosarul proiectului, copiază topografia în `th/` și scrie un `.th` de
legătură și un thconfig cu exporturile alese. ThIDE refuză, înainte de a scrie ceva, un dosar în care
fișierul de legătură ar suprascrie chiar topografia (dosarul propriu al topografiei) sau unul care
conține deja oricare dintre fișierele pe care le-ar scrie — alegeți un dosar gol.

## Export din Therion

### Intrări și puncte fixe → GIS

**Unelte → Exportă intrări / puncte fixe** scrie punctele georeferențiate într-un format GIS/GPS:

- **KML** (Google Earth), **GeoJSON** (GIS web), **GPX** (GPS de mână) sau **CSV** (foi de calcul).
  Coordonatele GPX sunt scrise cu 7 zecimale, așa cum le citește importul GPX, deci un drum dus-întors
  păstrează precizia completă; KML, GeoJSON și CSV folosesc 6 (aproximativ 0,1 m).

Excelent pentru a partaja pozițiile intrărilor, a le reprezenta pe o hartă de bază sau a le încărca
într-un GPS înainte de o ieșire.

### Tabele de date → CSV

**Unelte → Exportă tabel de date** produce exporturi tabelare:

- **Tabel stații** — fiecare stație și datele ei.
- **Tabel radieri** — fiecare tronson/radială.
- **Intrări / puncte fixe** — aceleași puncte ca mai sus, sub formă de tabel.

(Puteți folosi și **Export CSV…** direct din majoritatea grilelor din Navigatorul de obiecte și din
filele de analiză.)

### Raport topografic → HTML

**Unelte → Generează raport topografic (HTML)…** construiește un **raport HTML** autonom, ușor de
partajat, al topografiei — o cale rapidă de a publica o prezentare generală fără a compila hărți.
Exporturile de tabele pot fi produse și în HTML/LaTeX, pentru includere în documente. Dintr-un
terminal: `therion-cli report project.thconfig --out
report.html`.

## Desenare cu Mapiah

Pentru desenul galeriilor din fișierele `.th2`, ThIDE integrează editorul vizual extern **Mapiah**:

1. Deschideți un `.th2` și faceți clic pe **Editează cu Mapiah** în antetul fișierului (sau generați
   mai întâi unul cu **Unelte → Schiță nouă (.th2)…**).
2. Desenați în Mapiah; când **salvați** acolo, ThIDE **reîncarcă** automat fișierul.
3. Dacă Mapiah nu este instalat, ThIDE vă indică pagina lui de descărcare și
   **Setări → Unelte externe**, unde setați calea.

Schițele noi pot fi generate **goale** sau **dintr-o imagine / schiță `.xvi`**, ca să puteți trasa
peste un fundal scanat sau georeferențiat. [Panoul XVI](12-object-browser-and-outline.md#panoul-xvi)
arată la ce schițe fac referire schițele (`scrap`) dumneavoastră.

## Sisteme de coordonate

Georeferențierea în Therion folosește sisteme de coordonate (coduri EPSG / sisteme denumite). ThIDE le
validează pe cele pe care le folosiți și poate converti pentru dumneavoastră între lat/long WGS84 și
UTM — vedeți [Calculatoare și convertoare](18-calculators.md). Sistemele de coordonate necunoscute
sunt semnalate ca diagnostice (`TH0043`).

---

Următorul: [Calculatoare și convertoare →](18-calculators.md)
