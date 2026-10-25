# 12. Navigator obiecte și Contur

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Aceste două panouri transformă [graful de obiecte](04-core-concepts.md#graful-de-obiecte-modelul-semantic)
al proiectului dumneavoastră în tabele navigabile și într-un arbore structural — o modalitate foarte bună
de a *găsi* lucruri și de a verifica ce conține peștera.

## Conturul

**Vizualizare → Contur** afișează **structura de survey-uri/scrap-uri a fișierului curent** ca arbore
pliabil: survey-uri în survey-uri, scrap-uri și (opțional) stațiile din ele.

- **Filtrează conturul…** restrânge arborele pe măsură ce tastați.
- **Include stațiile în contur** adaugă stațiile ca frunze sub fiecare linie centrală.
- Faceți clic pe un element pentru a sări la el în editor.

Dacă un fișier nu are structură de survey-uri/scrap-uri, conturul vă spune acest lucru. Este per fișier
și ușor — gândiți-vă la el ca la cuprinsul documentului pe care îl editați.

## Navigatorul de obiecte

**Vizualizare → Navigator obiecte** acoperă întregul proiect. Prezintă întreaga peșteră sub formă de
**tabele**, câte unul pentru fiecare tip de entitate, pe file:

| Filă | Listează | Coloane tipice |
|---|---|---|
| **Stații** | Fiecare stație | Nume calificat, Tip, Topografie, Linie |
| **Vizări** | Fiecare vizare/splay | De la, Până la, Lungime, Azimut, Înclinare, Topografie |
| **Puncte fixe** | Stații georeferențiate | Stație, Coordonate, CRS |
| **Echivalări** | Perechi de stații unite | Numele echivalate |
| **Hărți** | Construcții `map` | Nume, membri |
| **Schițe** | Scrap-uri `.th2` | Nume, proiecție, georeferențiere |
| **Puncte / Linii / Suprafețe** | Obiecte de desen `.th2` | Tip, locație |

Comune tuturor tabelelor:

- **Filtrează…** pentru a restrânge rândurile; **Coloane ▾** pentru a afișa/ascunde coloane;
  **Potrivește** pentru dimensionare automată după conținut.
- Clic dreapta pe un rând: **Salt la sursă / Salt la definiție**, **Găsește referințe**,
  **Redenumește simbolul…**, **Copiază numele calificat** și ajutoare de copiere (celulă / rând / tot,
  simplu sau formatat).
- Dublu clic pentru a sări la declarația din sursă.
- **Export CSV…** pentru a scoate un tabel din aplicație.

Deoarece aceste tabele provin din modelul live, ele sunt cea mai rapidă cale de a răspunde la întrebări
precum „există această stație / câte splay-uri are acest survey / care scrap-uri nu sunt georeferențiate”.

> Filele de entități ale Navigatorului de obiecte fac parte din **Analiză topografică** și pot fi
> dezactivate în spațiile de lucru foarte mari — vedeți [Setări → Analiză topografică](19-settings-and-preferences.md#vizualizare).

## Panoul XVI

**XVI** listează fișierele de schiță (`.xvi`) la care fac referire scrap-urile dumneavoastră, cu **grila**
lor și **schița** căreia îi aparțin — util când legați desenele `.th2` de fundalurile scanate/trasate.
Vedeți și [Import, export și GIS](17-import-export-and-gis.md).

## Grila de măsurători (recapitulare)

Fiecare fișier `.th`/`.th2` deschis are și propria filă **Măsurători** (vizări + stații pentru *acel*
fișier), descrisă în [Editorul](07-editing.md#fila-măsurători). Folosiți Navigatorul de obiecte
pentru întregul proiect; folosiți fila Măsurători pentru fișierul din fața dumneavoastră.

---

Următorul: [Prezentare generală și analiză →](13-survey-overview-and-analytics.md)
