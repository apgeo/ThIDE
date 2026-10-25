# 17. Import, export și GIS

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

ThIDE face legătura între Therion și celelalte formate din trusa unui speolog — alte programe de
topografie, GIS, GPS și date de teren. Majoritatea se află în meniul **Unelte**.

## Import în Therion

**Unelte → Importă… (Import…)** convertește date externe în construcții Therion pe care le puteți apoi
lega în proiect:

| Import | Format sursă | Devine |
|---|---|---|
| **Importă topografie (Survex / Compass)…** | `.svx` (Survex), `.dat` (Compass) | Un survey Therion `.th` |
| **Importă DEM ca suprafață (.asc)…** | Grilă ESRI ASCII | Un bloc Therion `surface` |
| **Importă puncte GPX (→ fix)…** | `.gpx` | Linii `fix` (stații georeferențiate) |

După import, adăugați fișierul nou în thconfig (`input`/`source`) și compilați. Dacă un import
eșuează, o notificare toast vă spune de ce.

## Export din Therion

### Intrări și puncte fixe → GIS

**Unelte → Exportă intrări / puncte fixe** scrie punctele georeferențiate într-un format GIS/GPS:

- **KML** (Google Earth), **GeoJSON** (GIS web), **GPX** (GPS de mână) sau **CSV** (foi de calcul).

Excelent pentru a partaja locațiile intrărilor, a le reprezenta pe o hartă de bază sau a le încărca
într-un GPS înainte de o ieșire.

### Tabele de date → CSV

**Unelte → Exportă tabel de date** produce exporturi tabelare:

- **Tabel stații** — fiecare stație și datele ei.
- **Tabel radieri** — fiecare vizare/splay.
- **Intrări / puncte fixe** — aceleași puncte ca mai sus, sub formă de tabel.

(Puteți folosi și **Export CSV…** direct din majoritatea grilelor din Navigatorul de obiecte și din
filele de analiză.)

### Raport topografic → HTML

**Unelte → Generează raport topografic (HTML)…** construiește un **raport HTML** autonom, ușor de
partajat, al ridicării topografice — o cale rapidă de a publica o prezentare generală fără a compila
hărți. Exporturile de tabele pot fi produse și în HTML/LaTeX, pentru includere în documente.

## Desenare cu Mapiah

Pentru desenul galeriilor în fișierele `.th2`, ThIDE integrează editorul vizual extern **Mapiah**:

1. Deschideți un `.th2` și faceți clic pe **Editează cu Mapiah** în antetul fișierului (sau generați
   mai întâi unul cu **Unelte → Schiță nouă (.th2)…**).
2. Desenați în Mapiah; când **salvați** acolo, ThIDE **reîncarcă** automat fișierul.
3. Dacă Mapiah nu este instalat, ThIDE vă indică pagina de descărcare și
   **Setări → Unelte externe** pentru a seta calea.

Scrap-urile noi pot fi generate **goale** sau **dintr-o imagine / schiță `.xvi`**, ca să puteți trasa
peste un fundal scanat sau georeferențiat. [Panoul XVI](12-object-browser-and-outline.md#panoul-xvi)
arată la ce schițe fac referire scrap-urile dumneavoastră.

## Sisteme de coordonate

Georeferențierea în Therion folosește sisteme de coordonate (coduri EPSG / sisteme denumite). ThIDE le
validează pe cele pe care le folosiți și poate converti pentru dumneavoastră între lat/long WGS84 și
UTM — vedeți [Calculatoare și convertoare](18-calculators.md). Sistemele de coordonate necunoscute sunt
semnalate ca diagnostice (`TH0043`).

---

Următorul: [Calculatoare și convertoare →](18-calculators.md)
