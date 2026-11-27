# 14. Piste de explorare, TODO-uri și metadate

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md)

Trei file înrudite din panoul **Prezentare generală** vă ajută să gestionați părțile *neterminate* ale
unei peșteri și notițele din jurul lor. Toate acoperă întregul proiect și se actualizează din modelul
în timp real.

## Piste de explorare

Fila **Piste** este un registru al tuturor locurilor în care peștera ar putea continua — construit
automat din topografie. Recunoaște convențiile obișnuite: indicatorii de continuare, comentariile
`QM` / lead / `?` și capetele închise (capetele de galerie). Cuvintele-marcaj (`QM`, `lead` sau `leads`,
`continues`, `dig`, scrise cu litere mari sau mici) sunt recunoscute doar ca cuvinte întregi: le poate
urma o cifră, deci `QM12` este o pistă, dar „digital disto” și „misleading” nu sunt. Un `?` oriunde
într-un comentariu de stație marchează în continuare o pistă.

**Ce arată**

- Fiecare pistă, cu o **stare**: **Deschis**, **Verificat**, **Explorat** sau **Mort**.
- **Arată toate pistele deschise** lărgește căutarea la toate capetele libere, nu doar la pistele
  marcate explicit.
- **Copiază ca Markdown** pentru a pune lista într-un raport de ieșire sau într-un wiki.

**Pe hartă**

Pistele pot fi **suprapuse peste Previzualizarea principală**, așa încât să vedeți *unde* sunt pistele
promițătoare, nu doar o listă. Planificarea ieșirilor („ce piste sunt aproape una de alta?”) devine
astfel mult mai ușoară.

**Mereu la zi**

Implicit, registrul **se recalculează pe măsură ce tastați** (din conținutul nesalvat al editoarelor,
cu o scurtă întârziere), deci este mereu actual. În spațiile de lucru foarte mari puteți dezactiva
această recalculare (*Recalculează registrul de indicii pe măsură ce tastați* în
[Setări → Vizualizare](19-settings-and-preferences.md#vizualizare)) și puteți reîmprospăta registrul
manual cu butonul **Rescanează** — care funcționează întotdeauna, indiferent de setare.

## TODO-uri

Fila **Sarcini (todo)** adună comentariile **TODO / FIXME / QM** din tot proiectul într-o singură
listă, ca notițele lăsate pentru mai târziu să nu se piardă prin fișiere. Faceți clic pe o intrare
pentru a sări la ea. Scanarea face parte din *Analiză topografică* și poate fi dezactivată
([Setări → Vizualizare → Scanare comentarii TODO / FIXME / QM](19-settings-and-preferences.md#vizualizare)).

## Metadate

Fila **Metadate** este un **fișier însoțitor pentru fiecare spațiu de lucru**, pentru notițele care
nu își au locul în sursa `.th` — de pildă o descriere a proiectului, licența, regiunea sau notițe
libere. Este păstrat *alături de* fișierele dumneavoastră (nu în ele), deci nu interferează niciodată
cu Therion. **Salvează** îl scrie; **Reîncarcă** îl recitește.

## Media

Fila **Media** caută fișierele media la care face referire proiectul — în principal schițele `.xvi`
folosite de schițe (`scrap`) — și semnalează **fișierele orfane de pe disc** (fișiere media prezente,
dar nereferențiate). Utilă pentru a face ordine într-un dosar de imagini înainte de arhivare. Face parte
din *Analiză topografică* și poate fi dezactivată
([Setări → Vizualizare → Scanare manager media](19-settings-and-preferences.md#vizualizare)).

---

Următorul: [Geologie structurală →](15-structural-geology.md)
