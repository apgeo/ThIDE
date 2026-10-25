# 14. Piste de explorare, TODO-uri și metadate

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Trei file înrudite din panoul **Prezentare generală** vă ajută să gestionați părțile *neterminate* ale unei peșteri și
notițele din jurul ei. Toate acoperă întregul proiect și se actualizează din modelul în timp real.

## Piste de explorare

Fila **Piste** este un registru al tuturor locurilor în care peștera ar putea continua — construit automat din
ridicarea topografică. Recunoaște convențiile obișnuite: indicatoare de continuare, comentarii `QM` / lead / `?` și
fundături (capete de galerie).

**Ce arată**

- Fiecare pistă, cu o **stare**: **Deschis**, **Verificat**, **Explorat** sau **Mort**.
- **Arată toate pistele deschise** lărgește căutarea la fiecare capăt liber, nu doar la pistele marcate explicit.
- **Copiază ca Markdown** pentru a pune lista într-un raport de tură sau într-un wiki.

**Pe hartă**

Pistele pot fi **suprapuse peste Previzualizare principală**, astfel încât vedeți *unde* se află pistele promițătoare, nu
doar o listă. Planificarea turelor („care piste sunt aproape unele de altele?”) devine mult mai ușoară.

**Actualizare în timp real**

Implicit, registrul **se recalculează pe măsură ce tastați** (din conținutul nesalvat al editorului, cu temporizare), așa că
este mereu la zi. În spațiile de lucru foarte mari puteți dezactiva acest comportament
([Setări → Compilare & ieșire](19-settings-and-preferences.md#compilare-și-ieșire)) și reîmprospăta manual cu
butonul **Rescanează** — care funcționează întotdeauna, indiferent de setare.

## TODO-uri

Fila **Sarcini (todo)** adună comentariile **TODO / FIXME / QM** din tot proiectul într-o singură listă, ca
notițele rapide pentru dumneavoastră să nu se piardă prin fișiere. Faceți clic pe o intrare pentru a sări la ea. Scanarea face
parte din **Analiză topografică** și poate fi dezactivată
([Setări → Analiză topografică → Scanare comentarii TODO / FIXME / QM](19-settings-and-preferences.md#vizualizare)).

## Metadate

Fila **Metadate** este un **fișier însoțitor per spațiu de lucru** pentru notițe care nu își au locul în sursa `.th` —
de exemplu o descriere a proiectului, licența, regiunea sau notițe libere. Este stocată *alături de*
fișierele dumneavoastră (nu în ele), deci nu interferează niciodată cu Therion. **Salvează** o scrie; **Reîncarcă** o
recitește.

## Media

Fila **Media** caută fișierele media referite de proiect — în principal schițele `.xvi` folosite de scrap-uri
— și semnalează **fișierele orfane de pe disc** (media prezente, dar nereferite). Utilă pentru a face ordine într-un dosar de imagini
înainte de arhivare. Face parte din **Analiză topografică** și poate fi dezactivată
([Setări → Analiză topografică → Scanare manager media](19-settings-and-preferences.md#vizualizare)).

---

Următorul: [Geologie structurală →](15-structural-geology.md)
