# 13. Prezentare generală și analiză

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Panoul **Prezentare generală** (**Vizualizare → Prezentare generală**, etichetat *Prezentare generală
(Tablou de bord / Topografii / Audit)*) este cabina de comandă a proiectului dumneavoastră: un set de
file care rezumă întreaga peșteră, calculate live din model. Aici își petrece mult timp un coordonator.

> Toate cifrele de aici au **calitate de previzualizare**, fiind calculate în aplicație fără ajustarea
> buclelor din Therion — excelente pentru urmărire și verificări de bun-simț, dar **Therion este sursa
> de adevăr** pentru lungimile finale ajustate. Analiza poate fi dezactivată în spațiile de lucru foarte
> mari ([Setări → Analiză topografică](19-settings-and-preferences.md#vizualizare)).

## Filele

| Filă | Ce obțineți |
|---|---|
| **Tablou de bord** | Carduri de ansamblu: configurația de intrare, numărul de fișiere, **lungimea totală**, **adâncimea**, **ultima compilare**; **Acțiuni rapide** (Compilează, Deschide dosarul de ieșire). |
| **Topografii** | Fiecare survey cu lungimea și totalizările sale. |
| **Statistici** | Defalcarea lungimii (suprafață / subteran / duplicat / splay), amplitudinea verticală cu **stațiile cea mai înaltă/cea mai joasă**, extinderea orizontală, punctele fixe. |
| **Grafice** | **Lungime pe topografie** (primele 30) și **Lungime pe dată**. |
| **Echipă** | Membrii echipei și contribuția lor; **Ieșiri (după dată)**; copiere ca CSV. |
| **Intrări** | Intrări / puncte fixe cu coordonate. |
| **Calitate** | Verificări ale calității datelor, prezentate ca listă. |
| **Audit** | Goluri structurale, la cerere — vedeți mai jos. |
| **Piste** | Registrul pistelor de explorare — vedeți [Piste, TODO-uri și metadate](14-leads-notes-and-metadata.md). |
| **Sarcini (todo)** | Comentarii TODO/FIXME/QM agregate — aceeași pagină. |
| **Metadate** | Note per spațiu de lucru, într-un fișier separat — aceeași pagină. |
| **Media** | Fișiere `.xvi` referite și fișiere media de pe disc, cu cele orfane — aceeași pagină. |

## Interpretarea cifrelor

- **Defalcarea lungimii** separă galeria reală de vizările de **suprafață**, **duplicate** și **splay**,
  astfel încât „lungimea peșterii” înseamnă ceea ce vă așteptați.
- **Amplitudinea verticală** numește **stațiile cea mai înaltă și cea mai joasă**, nu doar cifra — util
  când descrieți adâncimea unei peșteri.
- **Lungime pe dată** și **Ieșiri** transformă liniile `date`/`team` într-o cronologie a istoricului
  ridicării topografice.

## Fila Audit

Fila **Audit** funcționează **la cerere**: faceți clic pe **Calculează** (apoi pe **Recalculează** pentru
reîmprospătare). Caută capete libere structurale:

- **Fișiere orfane** — fișiere Therion nelegate de *niciun* thconfig din spațiul de lucru (deci nu ar fi
  compilate niciodată).
- **Schițe nereferențiate** — scrap-uri care nu sunt incluse în nicio hartă.
- **Hărți neexportate** — hărți declarate, dar neselectate pentru export.

Este condiționată de Calculează deoarece căutarea este mai costisitoare decât celelalte file; rulați-o
înainte de o versiune publicată pentru a prinde lucrurile care ar dispărea în tăcere din rezultatul
compilat.

## Extragerea datelor

Majoritatea grilelor oferă **Copiază (CSV)** / **Copiază ca Markdown** și, per tabel, **Export CSV…**.
Pentru livrabile mai bogate, folosiți exportatoarele dedicate și raportul HTML — vedeți
[Import, export și GIS](17-import-export-and-gis.md) — care acoperă și exportul **intrărilor/punctelor
fixe** în KML/GeoJSON/GPX/CSV și exportul **tabelelor de stații/vizări**.

---

Următorul: [Piste de explorare, TODO-uri și metadate →](14-leads-notes-and-metadata.md)
