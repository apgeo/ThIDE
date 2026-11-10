# 12. Navigator obiecte și Contur

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Aceste două panouri transformă [graful de obiecte](04-core-concepts.md#graful-de-obiecte-modelul-semantic)
al proiectului în tabele pe care le puteți parcurge și într-un arbore al structurii — o modalitate
excelentă de a *găsi* lucruri și de a verifica ce conține peștera.

## Conturul

**Vizualizare → Contur** arată **structura de topografii/schițe a fișierului curent** sub forma unui arbore
pliabil: topografii în topografii, schițe și (opțional) stațiile din ele.

- **Filtrează conturul…** restrânge arborele pe măsură ce tastați.
- **Include stațiile în contur** adaugă stațiile ca frunze sub fiecare poligonală.
- Faceți clic pe un element pentru a sări la el în editor.

Dacă un fișier nu are o structură de topografii/schițe, conturul vă spune acest lucru. Este specific
fiecărui fișier și ușor — gândiți-vă la el ca la cuprinsul documentului pe care îl editați.

## Navigatorul de obiecte

**Vizualizare → Navigator obiecte** acoperă tot proiectul. Prezintă întreaga peșteră sub formă de
**tabele**, câte unul pentru fiecare tip de entitate, pe file:

| Filă | Listează | Coloane tipice |
|---|---|---|
| **Stații** | Fiecare stație | Nume calificat, Tip, Topografie, Linie |
| **Vizări** | Fiecare tronson/radială | De la, Până la, Lungime, Azimut, Înclinare, Topografie |
| **Puncte fixe** | Stațiile georeferențiate | Stație, Coordonate, CRS |
| **Echivalări** | Perechile de stații îmbinate | Numele echivalate |
| **Hărți** | Construcțiile `map` | Nume, membri |
| **Schițe** | Schițele (`scrap`) din `.th2` | Nume, proiecție, georeferențiere |
| **Puncte / Linii / Suprafețe** | Obiectele de desen din `.th2` | Tip, locație |

Comune tuturor tabelelor:

- **Filtrează…** pentru a restrânge rândurile; **Coloane ▾** pentru a afișa/ascunde coloane; **Potrivește**
  pentru a ajusta automat lățimea la conținut.
- Clic dreapta pe un rând: **Salt la sursă / Salt la definiție**, **Găsește referințe**, **Redenumește
  simbolul…**, **Copiază numele calificat** și comenzi de copiere (celulă / rând / tot, simplu sau
  formatat).
- Dublu-clic pentru a sări la declarația din sursă.
- **Export CSV…** pentru a scoate un tabel din aplicație.

Pentru că aceste tabele provin din modelul în timp real, ele sunt cea mai rapidă cale de a răspunde la
întrebări precum „există această stație / câte radiale are această topografie / care schițe nu sunt
georeferențiate”.

> Filele de entități ale Navigatorului de obiecte fac parte din **Analiză topografică** și pot fi
> dezactivate în spațiile de lucru foarte mari — consultați grupul *Analiză topografică* din
> [Setări → Vizualizare](19-settings-and-preferences.md#vizualizare).

## Panoul XVI

**XVI** listează fișierele de schiță (`.xvi`) la care fac referire schițele (`scrap`) dumneavoastră,
împreună cu **grila** lor și **schița** căreia îi aparțin — util când legați desenele `.th2` de fundalurile
lor scanate/trasate. Consultați și [Import, export și GIS](17-import-export-and-gis.md).

## Tabelul Măsurători (recapitulare)

Fiecare fișier `.th`/`.th2` deschis are și propria filă **Măsurători** (vize + stații pentru *acel*
fișier), descrisă în [Editorul](07-editing.md#fila-măsurători). Folosiți Navigatorul de obiecte pentru
întregul proiect; folosiți fila Măsurători pentru fișierul pe care îl aveți în față.

---

Următorul: [Prezentarea generală a topografiei și analize →](13-survey-overview-and-analytics.md)
