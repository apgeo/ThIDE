# 13. Prezentarea generală a topografiei și analize

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Panoul **Prezentare generală** (**Vizualizare → Prezentare generală**, afișat în meniu ca *Prezentare
generală (Tablou de bord / Topografii / Audit)*) este cabina de comandă a proiectului dumneavoastră: un set
de file care rezumă întreaga peșteră, calculate în timp real din model. Aici își petrece mult timp un
coordonator.

> Toate cifrele de aici au **calitate de previzualizare**, fiind calculate în aplicație fără compensarea
> buclelor din Therion — excelente pentru urmărire și verificări de bun-simț, dar **Therion rămâne sursa de
> referință** pentru lungimile finale compensate. Cifrele urmează modificările dumneavoastră, inclusiv pe
> cele nesalvate, și renunță la ele când închideți o filă fără să salvați. Analizele pot fi dezactivate în
> spațiile de lucru foarte mari (*Analiză topografică* în
> [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare)).

## Filele

| Filă | Ce obțineți |
|---|---|
| **Tablou de bord** | Carduri de ansamblu: configurația de intrare, numărul de fișiere, **lungimea totală**, **adâncimea**, **ultima compilare** (cu numele thconfig-ului); **Acțiuni rapide** (Compilează, Deschide dosarul de ieșire). |
| **Topografii** | Fiecare topografie, cu lungimea și totalurile cumulate. |
| **Statistici** | Defalcarea lungimii (suprafață / subteran / duplicat / radiale), denivelarea cu **stațiile cea mai înaltă/cea mai joasă**, extinderea orizontală, punctele fixe. |
| **Grafice** | **Lungime pe topografie** (primele 30) și **lungime pe dată**. |
| **Echipă** | Membrii echipei și contribuția lor; **Ieșiri (după dată)**; copiere ca CSV. |
| **Intrări** | Intrările / punctele fixe, cu coordonate. |
| **Calitate** | Verificări ale calității datelor, prezentate ca listă. Ele numără **tronsoanele**, nu radialele (radialele au propriul rând), și oferă *Arată-le în Object Browser*. |
| **Audit** | Goluri structurale, calculate la cerere — vedeți mai jos. |
| **Piste** | Registrul pistelor de explorare — consultați [Piste de explorare, TODO-uri și metadate](14-leads-notes-and-metadata.md). |
| **Sarcini (todo)** | Comentariile TODO/FIXME/QM adunate la un loc — aceeași pagină. |
| **Metadate** | Note pentru fiecare spațiu de lucru, păstrate într-un fișier alăturat — aceeași pagină. |
| **Media** | Fișierele `.xvi` la care se face referire și fișierele media de pe disc, inclusiv cele orfane — aceeași pagină. |

## Interpretarea cifrelor

- **Defalcarea lungimii** separă galeriile reale ale peșterii de vizele de **suprafață**, **duplicate** și
  **radiale**, astfel încât „lungimea peșterii” să însemne ceea ce vă așteptați.
- **Denivelarea** numește **stațiile cea mai înaltă și cea mai joasă**, nu doar valoarea — util când
  descrieți adâncimea unei peșteri.
- **Lungimea pe dată** și **Ieșiri** transformă liniile `date`/`team` într-o cronologie a topografierii.

## Fila Audit

Fila **Audit** funcționează **la cerere**: apăsați **Calculează** (apoi **Recalculează** pentru a
reîmprospăta). Ea caută capetele structurale rămase nelegate:

- **Fișiere orfane** — fișiere Therion care nu sunt legate de *niciun* thconfig din spațiul de lucru (deci
  nu ar fi compilate niciodată).
- **Schițe nereferențiate** — schițe care nu sunt incluse în nicio hartă.
- **Hărți neexportate** — hărți declarate, dar neselectate pentru export.

Fila este condiționată de butonul Calculează pentru că această căutare este mai costisitoare decât celelalte
file; rulați-o înaintea unei lansări pentru a prinde lucrurile care ar dispărea fără niciun avertisment din
rezultatul compilat.

## Extragerea datelor

Majoritatea tabelelor oferă **Copiază (CSV)** / **Copiază ca Markdown** și, pentru fiecare tabel,
**Export CSV…**. Pentru rezultate mai bogate, folosiți exportatoarele dedicate și raportul HTML — consultați
[Import, export și GIS](17-import-export-and-gis.md) —, care acoperă și exportul **intrărilor/punctelor
fixe** în KML/GeoJSON/GPX/CSV și exportul **tabelelor de stații/vize**.

---

Următorul: [Piste de explorare, TODO-uri și metadate →](14-leads-notes-and-metadata.md)
