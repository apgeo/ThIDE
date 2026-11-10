# 16. Harta relațională

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

**Harta relațională** este o diagramă interactivă a relațiilor dintre obiectele proiectului —
topografii, schițe și hărți și (opțional) fișierele care le găzduiesc. Acolo unde
[Navigatorul de obiecte](12-object-browser-and-outline.md) vă oferă tabele, aceasta vă arată *forma*
proiectului. Deschideți-o din **Vizualizare → Hartă relațională…** (sau din bara de instrumente).

## Ce arată

- **Noduri** pentru fiecare **topografie**, **hartă** și **schiță** (colorate după tip).
- **Legături** pentru relațiile dintre ele (o hartă care reunește schițe, o topografie care include
  o alta etc.).
- Activați **Include fișiere** pentru a adăuga ca noduri și fișierele gazdă `.thconfig` / `.th` /
  `.th2` — util pentru a vedea ce fișier definește ce.

## Interacțiune

| Faceți asta | Pentru a… |
|---|---|
| **Trageți** un nod | Rearanja diagrama manual. |
| **Dublu-clic** pe un nod | Deschide definiția lui în editor. |
| **Treceți cu mouse-ul** peste o legătură | Vedea ce relație reprezintă. |
| **Dublu-clic** pe o legătură | Sări la locul unde este definită relația. |
| **Ctrl+derulare** / butoanele de zoom | Mări sau micșora; **Resetează zoomul** / **Încadrează** pentru a reîncadra. |

## Opțiuni de aranjare

- **Aranjamentul diagramei** alege modul de dispunere a diagramei.
- **Dimensiune după nivel** face nodurile apropiate de rădăcină mai mari și frunzele mai mici — se
  potrivește mai bine spațiilor de lucru mari, unde o dispunere plată devine aglomerată.
- **Reîmprospătează** reconstruiește diagrama din spațiul de lucru curent, după ce ați editat fișiere.

## Când este utilă

- Pentru a înțelege un **proiect necunoscut** pe care tocmai l-ați deschis.
- Pentru a observa o **schiță pe care nu o include nicio hartă** sau o **hartă fără schițe** — deși
  fila [Prezentare generală → Audit](13-survey-overview-and-analytics.md#fila-audit) le raportează
  explicit.
- Pentru a explica structura proiectului unui colaborator.

---

Următorul: [Import, export și GIS →](17-import-export-and-gis.md)
