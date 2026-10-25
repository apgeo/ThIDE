# 16. Harta relațională

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

**Harta relațională** este o diagramă interactivă a relațiilor dintre obiectele proiectului — survey-uri,
scrap-uri și hărți și (opțional) fișierele care le conțin. Acolo unde [Navigator obiecte](12-object-browser-and-outline.md)
vă oferă tabele, harta vă arată *forma* proiectului. Deschideți-o din **Vizualizare → Hartă relațională…**
(sau din bara de instrumente).

## Ce arată

- **Noduri** pentru fiecare **survey**, **hartă** și **scrap** (cu culori diferite după tip).
- **Legături** pentru relațiile dintre ele (o hartă care agregă scrap-uri, un survey care include un altul
  și așa mai departe).
- Activați **Include fișiere** pentru a adăuga ca noduri și fișierele gazdă `.thconfig` / `.th` / `.th2` — util
  pentru a vedea ce fișier definește ce.

## Interacțiune

| Faceți asta | Pentru a… |
|---|---|
| **Trageți** un nod | Rearanja diagrama manual. |
| **Dublu-clic** pe un nod | Deschide definiția lui în editor. |
| **Treceți cu mouse-ul** peste o legătură | Vedea ce relație reprezintă. |
| **Dublu-clic** pe o legătură | Sări la locul unde este definită relația. |
| **Ctrl+derulare** / butoanele de zoom | Mări/micșora; **Resetează zoomul** / **Încadrează** pentru reîncadrare. |

## Opțiuni de aranjare

- **Aranjament (Layout)** alege modul de aranjare a diagramei.
- **Dimensiune după nivel** face nodurile apropiate de rădăcină mai mari și frunzele mai mici — o variantă care se scalează mai bine în spațiile de lucru
  mari, unde un aranjament plat devine aglomerat.
- **Reîmprospătează** reconstruiește diagrama din spațiul de lucru curent după ce ați editat fișiere.

## Când este utilă

- Pentru a înțelege un **proiect necunoscut** pe care tocmai l-ați deschis.
- Pentru a depista un **scrap pe care nicio hartă nu îl include** sau o **hartă fără scrap-uri** — deși fila
  [Prezentare generală → Audit](13-survey-overview-and-analytics.md#fila-audit) le raportează explicit.
- Pentru a explica structura proiectului unui colaborator.

---

Următorul: [Import, export și GIS →](17-import-export-and-gis.md)
