# 23. Randări de animații în Blender

> [← Înapoi la pagina principală a Ghidului utilizatorului](README.md) · [Pagina principală a proiectului ThIDE](../../../README.md)

> **Experimental — în lucru.** Acest modul este recent și încă se schimbă. Așteptați-vă la
> asperități și la setări sau comportamente care se modifică de la o versiune la alta.

Panoul **Animație Blender** transformă modelul 3D pe care proiectul dumneavoastră îl produce deja
într-o prezentare pe care o puteți distribui: un videoclip cu rotire în jurul peșterii, o coborâre
elicoidală, un zbor prin galeria principală, un set de imagini statice pentru documentare sau o
trecere de sus, în stil hartă. ThIDE convertește modelul într-o rețea de poligoane (mesh) pregătită
pentru Blender, descrie scena pentru Blender după alegerile dumneavoastră și
(dacă doriți) rulează Blender în fundal pentru a produce fișierul final — totul fără să atingeți
interfața Blender.

> **Aveți nevoie de Blender instalat** — versiunea **4.2 LTS sau mai nouă**. ThIDE îl găsește
> automat (*Program Files* pe Windows, `PATH`, snap/flatpak pe Linux, `/Applications` pe macOS);
> dacă nu reușește, indicați-i executabilul în **Setări → Unelte externe**. ThIDE nu include
> Blender și nu modifică niciodată instalarea dumneavoastră de Blender.

---

## Deschiderea panoului

**Unelte → Animație Blender…** deschide panoul în zona centrală. Tot ce setați acolo descrie o
singură *lucrare de randare*; nimic nu rulează până nu apăsați **Randează** sau **Previzualizare**.

## Fluxul de lucru, pas cu pas

1. **Alegeți o sursă.** Implicit, panoul listează modelele `.lox`/`.3d` găsite la ultima
   compilare a proiectului (cele mai noi întâi) — alegeți unul din lista derulantă. Sau bifați
   **Folosește un fișier extern** și indicați calea către orice `.lox` sau `.3d`. Dacă lista
   derulantă este goală, compilați mai întâi proiectul (**Compilare → Compilează**) sau folosiți un
   fișier extern.

   > Un model `.lox` (Therion *loch*) conține **pereți**, deci se randează ca galerie solidă. Un
   > model `.3d` (Survex) conține doar poligonala, deci ThIDE generează tuburi din datele LRUD.

2. **Alegeți o presetare** (recomandat). Banda de presetări oferă o galerie:
   - **Prezentare orbitală (Orbit showcase)** — o rotire completă, ca pe un platou, cu iluminare de
     studio.
   - **Coborâre elicoidală (Helix descent)** — o spirală din partea de sus a peșterii până jos.
   - **Zbor complet (Full flythrough)** — un zbor în lumina frontalei prin cea mai lungă galerie.
   - **Dezvăluirea hărții (Map reveal)** — o trecere văzută aproape de sus, în care peștera apare
     puțin câte puțin, de la intrări spre interior (vezi
     [Dezvăluirea peșterii, parte cu parte](#dezvăluirea-peșterii-parte-cu-parte)).
   - **Imagini pentru documentare (Documentation stills)** — imagini încadrate de sus / din față /
     din lateral / izometrice.

   O presetare completează dintr-odată camera, materialele, iluminarea, ieșirea și textele.
   Puteți apoi ajusta orice setare de sub ea.

   Fiecare presetare integrată **scrie numele peșterii peste imagine**: *Orbit showcase*, *Map
   reveal* și *Documentation stills* sus, tot timpul, iar *Helix descent* și *Full flythrough*
   pe un cadru de titlu cu care începe filmul, timp de trei secunde. Numele este cel pe care îl
   are modelul; peste un model fără nume nu se scrie nimic. Îl schimbați sau îl ștergeți în
   câmpul **Titlu** al panoului: vedeți [Textele din panou](#textele-din-panou).

3. **Ajustați setările** după nevoie:
   - **Cameră** — șablonul de mișcare.
   - **Teren** — suprafața terenului pe care o poate avea un model *.lox*, desenată deasupra
     peșterii: *Off* (implicit), *Solid* (o singură culoare) sau *Textured* (cu imaginea suprafeței
     așezată pe el; o singură culoare când modelul nu are imagine). Văzut de sus e transparent,
     văzut de jos nu se vede, nu aruncă umbre, iar camera încadrează tot peștera. **Opacitatea
     terenului**, alături, alege cât din peșteră se vede prin el de sus: la 0 % terenul nu se
     vede, la 100 % ascunde peștera de sub el; implicit 40 %. O grilă foarte mare e rărită la cel
     mult 512 × 512 puncte.
   - **Lumină** — *sistemul de lumini*: o lampă frontală pe cameră, un soare și un cer, trei
     lumini de studio sau o imagine de jur împrejurul scenei (*HdriFile*), cu **intensitatea** ei.
     Vedeți [Iluminarea cu o imagine (HDRI)](#iluminarea-cu-o-imagine-hdri).
   - **Motor** — *Cycles* (implicit, funcționează peste tot) sau *EEVEE* (mai rapid, dar necesită
     un afișaj; pe un server Linux fără afișaj folosiți Cycles). **Dispozitiv** alege backend-ul
     GPU pentru Cycles (Auto le încearcă pe toate și revine la CPU).
   - **Animație** — cadre pe secundă și durată; panoul afișează **numărul de cadre** rezultat.
   - **Dezvăluire** — peștera apare parte cu parte cât rulează clipul, după distanță, după
     ordinea ridicării sau după datele ei: vedeți
     [Dezvăluirea peșterii, parte cu parte](#dezvăluirea-peșterii-parte-cu-parte).
   - **Ieșire** — un videoclip (MP4/MKV/WebM), o secvență PNG numerotată sau o singură imagine;
     plus rezoluția și numele fișierului.
   - **Etichete** — activați etichetele stațiilor și marcajele pistelor. *Mărimea marcajelor*
     alege cât de mare e un marcaj: *Auto* îl dimensionează după galerie într-un zbor prin peșteră
     și după toată peștera altfel; *Cave*, *Passage*, *CameraDistance* (cam aceeași mărime în
     imagine la orice distanță, cu tot cu textul lui) și *HiddenInFlythrough* fixează un singur
     fel. *Etichete deasupra*
     alege tavanul deasupra căruia se ridică eticheta unei stații: întâi înălțimea măsurată, întâi
     pereții, doar pereții sau deloc (pe stație).
   - **Adnotări** — etichete, note cu linie, marcaje și fotografii proprii, încărcate dintr-un
     fișier: vedeți [Adnotări](#adnotări-etichete-note-cu-linie-marcaje-și-fotografii).
   - **Texte peste imagine** — **Titlul** și **Subtitlul** lui, **Linia de credite**, **Cadrul de
     final**, **Scara grafică**, **Săgeata nordului**, **Legenda adâncimii**, trecerile din și în
     negru și **Subtitrările**, fiecare în timpul ei: vedeți
     [Textele din panou](#textele-din-panou).
   - **Constructor de scenă (avansat)** — ce construiește scena în Blender, la randare, la export
     și la deschiderea în Blender; lăsați-l pe *Auto*. Vedeți
     [Doi constructori de scenă](#doi-constructori-de-scenă).
   - **Folder randare** — unde se salvează videoclipul, cadrele sau imaginea; implicit
     *Documents/ThIDE/Renders* (*ThIDE/Renders* din dosarul personal, acolo unde nu există un dosar
     Documents). Lăsați-l gol pentru a le păstra în dosarul temporar al lucrării, pe care sistemul de
     operare îl poate șterge.

   Dacă ceva nu este în regulă (o rezoluție video neobișnuită, zero cadre, …) apare la **De
   corectat înainte de randare**, iar butonul **Randează** rămâne dezactivat, cu motivul afișat.

4. **Previzualizați** (opțional, dar merită). Butonul **Previzualizare** randează o schiță rapidă
   a aceleiași scene: la jumătate din mărime, în vizualizarea solidă a Blender (fără lumini, fără
   umbre), o fracțiune de secundă pe cadru acolo unde randarea adevărată poate dura un minut.
   Folosiți-o pentru a verifica mișcarea camerei, încadrarea și textele înainte de randarea lungă.
   Schița se salvează lângă randare, cu `-draft` adăugat la nume. Un teren transparent nu apare
   într-o schiță, iar aceasta o spune.

5. **Randați.** Progresul arată etapa și, cât timp randează Blender, numărul de cadre (sau un
   indicator rotativ pentru etapele fără procentaj). Randările lungi sunt normale; **Renunță**
   oprește Blender în mod curat. La final primiți o notificare, căile rezultatelor apar, iar
   **Deschide folderul** / **Deschide jurnalul** le afișează. Un film cu texte peste imagine se
   face în două etape, iar panoul o arată pe a doua ca *Se compune filmul*.

## Texte peste imagine

O randare poate avea texte: un **titlu**, o **scară grafică**, o **săgeată a nordului**, o
**legendă a adâncimii**, o **linie de credite**, **subtitrări** și un **cadru de final**. Ele sunt
scrise peste cadrele terminate, într-o a doua etapă, în două dimensiuni, așa că nimic din peșteră
nu le poate ascunde: un titlu rămâne lizibil și în cea mai îngustă galerie a unui zbor.

| Text | Unde stă | Ce face |
|---|---|---|
| Titlu | sus, la mijloc | rămâne tot timpul; cu o durată, este un *cadru de titlu* în mijlocul imaginii, care apoi se stinge. Sub el poate sta un subtitlu |
| Scară grafică | stânga jos | o lungime rotundă (50 m, 2 km) la distanța la care privește camera; un set de imagini statice primește câte una pentru fiecare imagine |
| Săgeata nordului | dreapta sus | se rotește odată cu camera, cu **N** la vârf |
| Legenda adâncimii | marginea din dreapta | culorile nuanței de adâncime, partea de sus a peșterii întâi |
| Linie de credite | dreapta jos | mică, rămâne tot timpul |
| Linie de colț | stânga sus | rămâne tot timpul; textele de colț temporizate apar sub ea |
| Subtitrări | jos, la mijloc | fiecare în timpul ei, pe un fundal întunecat, câte una: o subtitrare care începe cât alta încă se vede o înlocuiește |
| Cadru de final | toată imaginea | un text pe negru, timp de secundele lui *după* ultimul cadru al scenei: filmul este cu atât mai lung. Poate lăsa acel cadru să se vadă sau poate veni peste sfârșitul scenei (mai jos) |
| Treceri | toată imaginea | filmul poate ieși din negru la început și intra în negru la sfârșit |

Literele sunt cele ale fontului propriu al Blender (cu diacriticele românești ș, ț, ă, î, â), albe
cu un contur întunecat, și au mărimea dată de înălțimea imaginii, așa că un film se citește la fel
la orice rezoluție. Totul păstrează o margine de 5 % față de marginile imaginii.

Într-o imagine **mai înaltă decât lată** (9:16, ca pe telefon), titlul, subtitlul și săgeata nordului
sunt mai mici, după forma imaginii, iar rândurile titlului se termină înainte să înceapă săgeata: un
titlu de câteva cuvinte rămâne pe un singur rând și nu atinge săgeata. O imagine de 3:2 sau mai
lată este așezată așa cum este descris mai sus.

### Textele din panou

La **Texte peste imagine**, panoul are textele din tabelul de mai sus:

- **Titlu** — titlul. Presetarea stabilește dacă rămâne în partea de sus a imaginii sau este un
  cadru cu care începe filmul; ce scrieți aici este textul. Gol, nu există titlu.
- **Subtitlu** — o linie mai mică sub titlu. Fără titlu, nu se arată.
- **Linie de credite** — o linie mică ce rămâne în colțul din dreapta jos (cine a topografiat,
  cine a desenat, o licență).
- **Cadru de final** — un text pe o imagine neagră cu care se încheie filmul, timp de secundele
  de alături.
- **Scară grafică**, **Săgeata nordului**, **Legenda adâncimii** — bifați-le pe cele pe care
  trebuie să le aibă imaginea.
- **Ieșire din negru** și **Intrare în negru** — cât durează ca filmul să iasă din negru la
  început și să intre în negru la sfârșit, în secunde; 0 înseamnă fără trecere.
- **Subtitrări** — linii în partea de jos a imaginii, fiecare în timpul ei. **Adaugă subtitrare**
  adaugă un rând după ultimul, lung de trei secunde: scrieți-i **textul** și puneți secundele
  **de la** care și **până la** care se vede, numărate de la începutul filmului. **✕** de la
  capătul rândului îl șterge. Un rând fără text este lăsat deoparte; o subtitrare care se termină
  înainte să înceapă apare la **De corectat înainte de randare**.

Sub rânduri, o **bandă** arată subtitrările de-a lungul clipului, fiecare acolo unde cad
secundele ei, cu numărul ei. Partea gri este clipul însuși: o subtitrare care trece de ea ar
continua după ce scena s-a încheiat, iar banda se lungește ca să arate cu cât. Se vede câte o
singură subtitrare, așa că, din două care se suprapun pe bandă, cea care începe mai târziu o
scurtează pe cealaltă.

**Numele peșterii într-un text.** Scrieți `{cave}` acolo unde trebuie să stea numele peșterii:
în titlu, în linia de credite, în cadrul de final sau într-o subtitrare. Este completat din model
când pornește randarea: titlul unui fișier `.3d` sau titlul ridicării din vârful unui fișier
`.lox` (`-title` al celui mai din afară `survey` al proiectului). Presetările integrate îl
folosesc pentru titlurile lor, de aceea câmpul Titlu arată `{cave}` până scrieți altceva. Un text
care nu este altceva decât numele este lăsat deoparte când modelul nu are nume.

**Două numere ale modelului.** `{length}` ține locul lungimii axei topografice a modelului, iar
`{depth}` al întinderii lui pe verticală, fiecare în metri întregi, cu unitatea („1234 m”, „87 m”).
Sunt ceea ce conține modelul însuși: `{length}` este suma vizelor lui, fără cele pe care le are
semnalate ca fiind de suprafață, radiante (splay) sau duplicate, iar `{depth}` este înălțimea de la
stația cea mai de jos la cea mai de sus. Acestea nu sunt întotdeauna dezvoltarea și denivelarea
dintr-un raport (un model poate conține doar o parte a peșterii sau vize de suprafață nesemnalate);
scrieți numerele de mână acolo unde trebuie să fie cele oficiale. Un text care nu este altceva decât
un număr pe care modelul nu îl dă este lăsat deoparte.

**Texte în presetări.** O presetare păstrează textele împreună cu toate celelalte. **Salvează**
stochează titlul și subtitlul lui, linia de credite, cadrul de final, scara grafică, săgeata
nordului, legenda adâncimii, trecerile și subtitrările așa cum sunt în panou, iar alegerea
presetării le aduce înapoi mai târziu, subtitrările ca rânduri pe care le puteți modifica. Folosiți `{cave}` într-o presetare pe care vreți să o folosiți pentru mai multe peșteri.

Ceea ce nu are câmp în panou (linia de colț și textele ei temporizate, secundele unui cadru de
titlu) se setează într-o specificație de randare (JSON-ul pe care îl ține o presetare, la
`labels.overlays`); titlul, linia de credite, cadrul de final și subtitrările se pot da și în
linia de comandă (`--title`, `--credits`, `--end-card`, `--caption`) sau de către un asistent,
prin uneltele MCP. O presetare care le are le păstrează cât timp schimbați textele de mai sus.

**Un cadru de final peste scenă.** Într-o specificație de randare, cadrul de final mai are două
setări, la `labels.overlays.endCard`. `backdrop` spune cât de întunecat este cadrul: 1 (implicit)
este cadrul negru, 0 lasă ultimul cadru al scenei așa cum este și pune textul pe un fundal
întunecat al lui, iar un număr între ele întunecă doar cadrul. `"overScene": true` aduce cadrul de
final peste ultimele secunde ale scenei, care continuă sub el, în loc să țină pe loc ultimul
cadru: filmul nu mai este atunci mai lung din cauza lui.

```json
"endCard": { "text": "Mulțumim", "seconds": 4, "backdrop": 0.5, "overScene": true }
```

**Cât durează apariția și dispariția.** Într-o specificație de randare, `"fade"` la cadrul de
final spune în câte secunde apare (0 înseamnă dintr-odată; fără el, o jumătate de secundă), iar
`"titleFade"` lângă titlu spune cât durează apariția titlului și, când este un cadru de titlu,
dispariția lui (fără el, titlul este prezent de la primul cadru, iar un cadru de titlu dispare în
0,8 s):

```json
"title": "{cave}", "titleSeconds": 3, "titleFade": 1.5,
"endCard": { "text": "Mulțumim", "seconds": 4, "fade": 2 }
```

`"titleFadeIn"` și `"titleFadeOut"` dau celor două timpuri separate: `"titleFadeIn": 0.3,
"titleFadeOut": 1.5` face ca cadrul să apară repede și să dispară încet. Fiecare, când este dat,
are întâietate față de `"titleFade"` pentru jumătatea lui; `titleFadeOut` merge cu `titleSeconds`,
fiindcă numai un cadru de titlu dispare.

**Subtitrările sunt scrise și ca fișiere de subtitrare**: `<nume>.srt` și `<nume>.vtt` lângă film,
pentru un player video sau o pagină web care le poate afișa, ascunde sau traduce.

**Două etape.** O randare cu texte randează întâi scena ca imagini PNG numerotate, apoi face
filmul din acele cadre, cu textele peste ele. Cadrele ocupă spațiu pe disc cât timp rulează
randarea (ThIDE le socotește înainte să pornească) și sunt șterse când filmul este scris. Pentru că
textele se adaugă la sfârșit, schimbarea unuia nu cere randarea din nou a scenei: vedeți
`--compose-only` la [Din linia de comandă](#din-linia-de-comandă).

## Etichete care apar și dispar

Etichetele nu trebuie să fie prezente pe toată durata filmului. O specificație de randare (JSON-ul
pe care îl păstrează o presetare) poate spune când este afișat și când este ascuns un grup de
etichete, la `labels.events`:

```json
"events": [
  { "target": "ComponentLabels", "showFrame": 1, "fadeSeconds": 3 },
  { "target": "StationLabels", "showFrame": 60, "hideFrame": 240 }
]
```

`target` este `StationLabels` (etichetele stațiilor), `ComponentLabels` (etichetele
componentelor), `LeadMarkers` (marcajele pistelor, cu textele lor) sau `Overlays` (titlul, scara
grafică, săgeata nordului și legenda adâncimii). Grupul este ascuns până la `showFrame` și
începând cu `hideFrame`; `fadeSeconds` spune în cât timp apare.

Playerul face etichetele să apară **treptat**: devin din ce în ce mai opace, la mărimea lor
întreagă, iar un marcaj de pistă continuă să pulseze în acest timp. Ascunderea se face dintr-odată.
(Generatorul de scripturi afișează și ascunde pe aceleași cadre, dar, în loc să le facă treptat
opace, mărește etichetele pornind de la nimic.) O previzualizare arată apariția treptată ca pe o
tăietură: etichetele sunt pur și simplu prezente începând cu `showFrame`.

## Dezvăluirea peșterii, parte cu parte

Peștera nu trebuie să fie toată acolo de la primul cadru. Cu o **dezvăluire** (`reveal`) ea apare
parte cu parte cât rulează clipul, într-o ordine pe care o dă chiar ridicarea topografică:

| Dezvăluire | Peștera apare… | Are nevoie de |
|---|---|---|
| `distance` (după distanță) | de la intrări spre interior, fiecare loc atunci când dezvăluirea a ajuns, de-a lungul galeriilor, atât de departe cât se află locul | nimic: orice model o are. O peșteră fără intrare marcată pornește de la prima stație a primei vize; `from` numește altă stație de pornire |
| `order` (după ordine) | viză cu viză, în ordinea în care le are ridicarea, fiecare viză crescând dinspre capătul care este deja acolo | nimic |
| `date` (după dată) | în ordinea **datelor ridicării**: istoria explorării, cu un **contor al anilor** într-un colț al imaginii | datele ridicării |

Presetarea **Map reveal** dezvăluie după distanță. Orice altă presetare primește o dezvăluire în
panou:

- **Dezvăluie peștera după** — *Off* (toată peștera de la primul cadru), *Distance* (după
  distanță), *Order* (după ordine) sau *Date* (după dată). Câmpurile de sub el apar după ce
  alegeți o dezvăluire.
- **De la stația** (după distanță) — stația de la care se măsoară distanțele; gol, se măsoară de
  la intrări.
- **Fișier cu date** (după dată) — fișierul `.3d` care are datele ridicării, scris sau ales cu
  **Răsfoiește…**; gol, se folosește un `.3d` cu același nume de lângă model (vedeți *De unde vin
  datele*, mai jos).
- **Dezvăluirea începe** — secunda la care începe dezvăluirea. **Dezvăluie până la sfârșitul
  clipului** este bifat la început; debifați-l, iar **Dezvăluirea se termină** primește secunda
  la care toată peștera este acolo. Lăsați peșterii o secundă sau două la sfârșit.
- **Marginea dezvăluirii** — cât de moale este marginea (0 înseamnă o tăietură netă) și
  **Strălucire acolo unde apare peștera**.

Un fișier care nu există, o dezvăluire care se termină înainte să înceapă sau care începe după
sfârșitul clipului apar la **De corectat înainte de randare**. **Salvează** păstrează dezvăluirea
în presetare.

O dezvăluire vine și din linia de comandă (`--reveal date`, vezi
[mai jos](#din-linia-de-comandă)), prin uneltele MCP (`reveal`) sau stă chiar în specificația de
randare, care are două setări în plus față de panou (`glowColor` și `counter`; panoul le păstrează
pe cele ale presetării):

```json
"reveal": { "by": "Date", "start": 1, "end": 9, "edge": 0.03, "glow": true, "counter": true }
```

- `start` și `end` spun când începe dezvăluirea și când este acolo toată peștera, în secunde; fără
  `end`, ea se termină odată cu clipul. Lăsați peșterii o secundă sau două la sfârșit.
- `edge` spune cât de moale este marginea, ca parte din toată dezvăluirea: ce apare are nevoie de
  atât din ea ca să devină opac. `0` înseamnă o tăietură netă.
- `glow` face să strălucească ce tocmai a apărut (în culoarea `glowColor`, un portocaliu cald
  dacă nu spuneți altceva), până când dezvăluirea a trecut mai departe.
- `counter` (pentru `date`) scrie deasupra scării grafice anul la care a ajuns dezvăluirea.
  **Map reveal** îl are pornit: treceți presetarea pe `date` și ea numără anii. Cu
  `"counterFormat": "{} m"` și o dezvăluire după `distance` are și ea un contor, al metrilor de la
  intrare, iar una după `order` numără tronsoanele; `{}` este locul numărului, iar
  `counterDecimals` (0 până la 6) câte zecimale are.
- `from` (pentru `distance`) este numele stației de la care se măsoară distanțele.
- `datesPath` (pentru `date`) numește fișierul care are datele.

**De unde vin datele.** Un fișier `.3d` păstrează ziua în care a fost măsurată fiecare viză; un
fișier `.lox` nu. Pentru un model `.3d` datele sunt pur și simplu acolo. Pentru un model `.lox`,
care este cel cu pereții adevărați, ThIDE ia datele din fișierul `.3d` al aceleiași peșteri: un
`.3d` cu același nume aflat lângă `.lox` sau cel pe care îl numiți (`--reveal-dates cave.3d`,
`datesPath`). Vizele celor două fișiere sunt potrivite după locul stațiilor lor, așa că amândouă
trebuie să provină din aceleași date topografice.

- O viză fără dată este arătată de la începutul dezvăluirii; randarea spune câte sunt.
- Un model fără nicio dată este randat întreg, iar randarea spune de ce.
- O peșteră ridicată în trei expediții apare în trei pași. Cu pereți ridicați (un `.lox`), fiecare
  bucată de perete apare odată cu viza de care este cel mai aproape.

**Ce trebuie știut.**

- **Doar playerul desenează o dezvăluire.** O scenă construită de generatorul de scripturi arată
  toată peștera de la început până la sfârșit, iar rezultatul spune acest lucru.
- O imagine statică și un set de imagini statice arată toată peștera.
- O previzualizare arată dezvăluirea ca pe o tăietură de-a lungul fețelor modelului, fără marginea
  moale și fără strălucire, și spune acest lucru.
- Etichetele și adnotările nu sunt dezvăluite odată cu peștera: dați-le timpii lor
  (`labels.events`, câmpul `from` al unei adnotări), așa cum face presetarea **Map reveal** pentru
  etichetele ei.

## Adnotări: etichete, note cu linie, marcaje și fotografii

Pe lângă etichetele pe care ThIDE le face din model (stațiile, pistele), o scenă poate avea
**adnotări** proprii: lucruri care arată ceva, fiecare la o stație sau într-un punct.

| Fel | Ce este |
|---|---|
| `label` | o etichetă: un text pe stația lui, întors spre cameră |
| `callout` | o notă cu linie: un text așezat la distanță de stație, cu o linie de la stație până la el |
| `marker` | un marcaj: o bilă luminoasă, sau un ac, un con, un cub, un inel ori o imagine mică |
| `outline` | un contur: o linie de-a lungul unei liste de puncte, pentru conturul unei galerii sau al unei săli pe suprafața pe care se află |
| `photo` | o fotografie dintr-un fișier PNG sau JPEG, pe stația ei sau deasupra, cu o linie până la ea |

Se scriu ca o listă JSON, într-un fișier al lor:

```json
[
  { "kind": "callout", "station": "main.0", "text": "Intrarea" },
  { "kind": "label", "station": "main.14", "text": "Sala Mare", "from": 4, "to": 9 },
  { "kind": "photo", "station": "main.14", "image": "photos/hall.jpg", "size": 6,
    "offset": [0, 0, 5], "framed": true, "within": 40 },
  { "kind": "marker", "at": [512040.5, 5120010.2, 1180], "color": [1, 0.8, 0] }
]
```

| Cheie | Înțeles |
|---|---|
| `kind` | `label`, `callout`, `marker`, `photo` sau `outline` |
| `station` *sau* `at` | unde: numele unei stații, așa cum îl are modelul, sau un punct `[est, nord, sus]` în coordonatele modelului |
| `text` | ce spune o etichetă sau o notă |
| `image` | fișierul unei fotografii, `.png` sau `.jpg`, numit față de locul în care se află fișierul de adnotări |
| `size` | în metri: înălțimea literelor unui text, raza unui marcaj, lățimea unei fotografii. Dacă lipsește, se potrivește cu peștera: un text cât o etichetă de stație, o fotografie cât o zecime din întinderea peșterii |
| `offset` | `[est, nord, sus]` în metri: unde stă textul unei note sau o fotografie față de stație. O notă fără el stă la patru înălțimi de literă deasupra; o fotografie fără el este pe stația ei, iar cu el are o linie până la ea |
| `color` | `[roșu, verde, albastru]`, fiecare între 0 și 1, pentru un text sau un marcaj |
| `symbol` | la un marcaj: `ball` (implicit), `pin`, `cone`, `cube`, `ring` sau `image`. Acul și conul stau cu vârful pe stație; inelul se întoarce spre cameră; `image` este o imagine mică (`image` îi numește fișierul), lată cât dublul lui `size` |
| `sizeBy`, `sizeFactor`, `minSize`, `maxSize` | la un marcaj: `"sizeBy": "distance"` dă razei `sizeFactor` metri pentru fiecare metru dintre el și cameră, `"screen"` îl face să apară cât `sizeFactor` ori lățimea imaginii, oriunde ar fi camera; amândouă rămân între `minSize` și `maxSize` (un sfert și de patru ori `size`, dacă nu spuneți altceva) |
| `points` | la un contur: punctele `[est, nord, sus]` prin care trece, în coordonatele proprii ale modelului; grosimea lui este `size`, iar el se află la primul punct, dacă nu dați o `station` sau un `at` |
| `framed` | `true` pune un chenar în jurul unei fotografii |
| `facing`, `normal` | `"facing": "fixed"` face dintr-o fotografie un panou care privește de-a lungul lui `normal` (`[est, nord, sus]`) și rămâne așa; fără el, fotografia stă cu fața spre cameră |
| `from`, `to` | când este prezentă, în secunde de la începutul filmului; fără ele, tot timpul |
| `within` | este prezentă doar cât timp camera se află la cel mult atâția metri de stația ei: o fotografie care apare când un zbor prin peșteră ajunge la locul ei |
| `fade` | în cât timp apare și dispare, în secunde (0,5 dacă nu spuneți altfel; 0 este o tăietură) |
| `id`, `role` | numele ei în scenă și numele unui grup, pentru `changes` dintr-un plan de scenă |

O fotografie care stă cu fața spre cameră rămâne paralelă cu imaginea și dreaptă în ea, așa că se
vede mereu ca dreptunghiul care este. Își arată imaginea așa cum ar arăta-o un ecran, oricare ar fi
lumina din peșteră, și nu luminează nimic. Un text și un marcaj strălucesc, ca etichetele.

**Transparență.** Acolo unde un PNG este transparent (o imagine decupată de pe fundalul ei),
fotografia lipsește și se vede peștera din spatele ei; mozaicul unei previzualizări arată tot
dreptunghiul.

**Fotografii.** O fotografie este un PNG sau un JPEG de cel mult 32 MiB, 16 384 de pixeli pe o
latură și 64 de milioane de pixeli; orice altceva este respins înainte ca Blender să pornească. O
imagine lată de 2 000 de pixeli este mai mult decât arată un film: fișierele mai mici dau pachete
mai mici și randări mai rapide. Fișierele rămân unde sunt; ThIDE le copiază pentru randare și
într-un pachet (în dosarul lui `photos`).

**În panou.** La **Adnotări**, scrieți calea fișierului sau alegeți-l cu **Răsfoiește…** și
apăsați **Încarcă**: scena primește adnotările fișierului *în locul* celor pe care le avea, iar
linia de dedesubt spune câte sunt și de ce fel. Ce este greșit la ele apare cu roșu sub ea și la
**De corectat înainte de randare**: o adnotare fără text sau fără stație, o fotografie al cărei
fișier nu există sau nu este PNG ori JPEG. Un fișier care nu este o listă de adnotări nu este
încărcat, iar scena le păstrează pe cele pe care le avea. Panoul citește fișierul când apăsați
**Încarcă**: apăsați din nou după ce modificați fișierul. **Golește** scoate adnotările din scenă
(fișierul rămâne). **Salvează** le păstrează în presetare, iar o presetare care are adnotări le
aduce înapoi; alegerea unei presetări le înlocuiește pe cele ale unui fișier.

**În altă parte.** În linia de comandă, `--annotations note.json` (o dată pentru fiecare fișier);
prin uneltele MCP, `annotations` numește un astfel de fișier din spațiul de lucru; într-o
specificație de randare, aceeași listă stă la `annotations`.

- O stație pe care modelul nu o are oprește randarea, cu numele ei.
- O adnotare care nu este arătată niciodată (timpul ei este după sfârșitul filmului, camera nu
  ajunge atât de aproape) este raportată ca avertisment.
- **Doar playerul desenează adnotările.** O scenă construită de generatorul de scripturi le
  omite, iar rezultatul spune acest lucru.
- O previzualizare arată o fotografie ca pe un mozaic al culorilor ei (vizualizarea solidă a
  Blender nu desenează imagini), iar o apariție treptată ca pe o tăietură.

## Iluminarea cu o imagine (HDRI)

O scenă poate fi luminată de imaginea unui întreg loc: un cer, un studio, o carieră la apus. O
asemenea imagine (un *HDRI*) este așezată de jur împrejurul scenei, este ceea ce vede camera în
spatele peșterii și luminează peștera cu propriile ei culori, din locurile ei luminoase. ThIDE nu
vine cu niciuna; multe se pot descărca gratuit.

**În panou**, la **Lumină**: alegeți *HdriFile* ca **Sistem de lumini** și numiți imaginea la
**Imagine HDRI**, sau apăsați **Răsfoiește…**, care alege fișierul și sistemul de lumini în locul
dumneavoastră. **Intensitatea luminii** spune cât de puternic luminează, iar **Rotire HDRI** o
rotește. O imagine care nu există sau care nu este nici `.hdr`, nici `.exr` apare la **De corectat
înainte de randare**. **Salvează** păstrează în presetare calea imaginii, cu restul.

Este și o setare a specificației de randare, la `lighting`, a liniei de comandă și a uneltelor MCP
(`hdri`, `hdriStrength`, `hdriRotation`, pentru o imagine din spațiul de lucru):

```json
"lighting": { "rig": "HdriFile", "hdriPath": "ceruri/seara.hdr", "strength": 1.5, "hdriRotation": 90 }
```

```
therion-cli blender cave.lox --hdri ceruri/seara.hdr --hdri-rotation 90 --hdri-strength 1.5 --out renders
```

- **Imaginea** este un fișier Radiance **`.hdr`** sau OpenEXR **`.exr`**, formatele obișnuite ale
  unor asemenea imagini: de două ori mai lată decât înaltă, cu tot orizontul de la un capăt la
  altul și cu cerul sus. ThIDE verifică fișierul înainte să pornească Blender, după ce conține,
  nu după nume, și refuză unul care nu este niciunul dintre cele două sau care este mai mare de
  256 MiB, de 16.384 de pixeli pe o latură sau de 16.384 × 8.192 pixeli. O imagine lată de 4.096
  de pixeli luminează o scenă la fel de bine ca una mai mare; una mai mare face doar fundalul mai
  clar.
- **`hdriRotation`** rotește imaginea în jurul verticalei, în grade, în sens invers acelor de
  ceasornic văzut de sus: folosiți-o ca să aduceți soarele din imagine acolo de unde vreți să
  vină lumina. Nerotită, imaginea are mijlocul spre est.
- **`strength`** o face mai luminoasă sau mai slabă: 1 este imaginea așa cum este.
- Fișierul rămâne unde este. ThIDE îl copiază pentru randare și într-un
  [pachet](#un-pachet-de-randare-pentru-randarea-pe-alt-calculator) (în dosarul lui `hdri`), așa
  că un pachet se randează pe alt calculator fără el.
- O previzualizare nu desenează nicio imagine: fundalul ei este un gri simplu, la fel de puternic
  cum este setată imaginea.

## O peșteră privită de departe: bătaia lămpii

Lumina *Headlamp* este o lampă care stă pe cameră, iar lumina ei scade cu pătratul distanței:
luminează bine un perete aflat la trei metri, pe unul aflat la 25 m cam a douăsprezecea parte, iar
pe unul aflat la 140 m deloc. Un zbor prin galerii este mereu aproape de un perete. O cameră care
stă departe de peșteră (o orbită, o imagine statică a întregii peșteri, un zbor care urmărește de la
30–140 m) o vede întunecată.

**Lampa bate până unde privește camera** întărește lampa cu pătratul distanței de la cameră până
la ceea ce privește, dincolo de 3 m, astfel încât peștera este luminată de departe la fel ca de
aproape. Mai aproape de 3 m lampa își păstrează tăria, iar **Intensitatea luminii** o scalează în
continuare.

- **În panou**, la **Lumină**: bifați **Lampa bate până unde privește camera** (se poate bifa cât
  timp lumina este *Headlamp*).
- **Într-o specificație de randare**: `"lighting": { "rig": "Headlamp", "lampReach": true }`.
- **În linia de comandă**: `--lamp-reach`, care alege și lampa de pe cameră ca lumină.

Este desenată doar de player; un script scris de generatorul de scripturi o lasă deoparte și spune
acest lucru. În cifrele unei randări de test: o peșteră inventată, într-o imagine statică de la
148 m, are luminozitatea 0,1 din 255 cu lampa așa cum este și 154 cu bătaia activă.

Cine scrie planuri de scenă cu biblioteca are încă două unelte pentru aceasta (vedeți `CHANGES.md`
al bibliotecii): `LampReach`, care pune chei pe energia lămpii după distanțe proprii, și un
material **neluminat** (`"unlit": true`), care își arată culorile așa cum le arată un ecran,
oricare ar fi lumina, pentru vize topografice și trasee care trebuie să se citească de la orice
distanță.

**Lumina *SunSky* este aceeași în orice Blender.** Blender 5 are un model de cer mai nou pe lângă
cel din Blender 4, mai albastru și mai luminos; playerul cere în ambele cerul din Blender 4, așa că
o scenă arată la fel în Blender 4.2, 4.5 și 5.0. (Până la playerul 1.8 al ThIDE, o scenă sub soare
și cer arăta foarte diferit în Blender 5.)

## Zborul pe un traseu între stații

Șablonul *Flythrough* zboară prin cea mai lungă galerie a peșterii. Șablonul **Route** zboară pe
drumul pe care îl numiți: de la o stație la alta, de-a lungul galeriilor, cu opriri la stațiile de
pe drum. Stațiile sunt unite pe cele mai scurte galerii dintre ele, așa că un traseu până la capătul
unei galerii laterale și înapoi înseamnă trei nume.

- **Viteza după mărimea galeriei.** Camera zboară cu de două ori raza galeriei pe secundă, între
  1,5 și 12 m/s: o târâtoare lată de 1,4 m este parcursă cu 1,5 m/s, o galerie lată de 10 m cu
  10 m/s. Raza se citește din pereții galeriei, așa cum îi dau stânga, dreapta, sus și jos ale
  ridicării; o galerie desenată din pereți reali (scrap-uri) este parcursă cu 4 m/s. Între galerii
  de mărimi diferite viteza se schimbă treptat.
- **Opriri.** La fiecare oprire camera încetinește până se oprește, în aproximativ o secundă,
  stă pe loc (1 s la o oprire de pe drum, dacă nu dați alt timp), își rotește privirea pe loc, cu
  cel mult 100° pe secundă, spre direcția în care merge mai departe, apoi accelerează din nou. La
  capătul unei fundături se întoarce complet, în aproximativ două secunde.
- **Durata clipului.** Un traseu durează cât îi iau deplasarea, opririle și rotirile, iar în linia
  de comandă aceasta este durata clipului. Dacă dați în schimb o durată, deplasarea este grăbită
  sau încetinită ca să încapă în ea; opririle și rotirile își păstrează durata.
- **Două nume țin locul unor stații pe care le dă modelul:** `entrance`, prima stație semnalată
  ca intrare, și `farthest`, stația cea mai îndepărtată de începutul traseului, măsurată de-a
  lungul galeriilor. Un traseu fără stații numite merge de la intrare până la stația cea mai
  îndepărtată. O stație cu același nume în mai multe ridicări se numește împreună cu ridicarea ei,
  `5@upper`.

Un exemplu inventat: intrarea peșterii este `e1`, o galerie laterală pleacă din galeria principală
la `s12` și se termină la `b7`, iar galeria principală continuă până la `s40`. Pentru a intra, a
privi galeria laterală până la capăt, a reveni și a merge mai departe:

```
therion-cli blender pestera.lox --preset "Full flythrough" --route e1:b7:s40
```

Același lucru într-o specificație de randare:

```json
"camera": {
  "template": "Route",
  "route": { "from": "e1", "via": ["b7"], "to": "s40", "holdSeconds": 1.5, "endHoldSeconds": 2, "timing": "Natural" }
}
```

`stations` (`["e1", "s12", "s40"]`) enumeră stațiile de parcurs în locul lui `from`, `via` și `to`;
camera se oprește atunci doar la prima și la ultima. `startHoldSeconds`, `holdSeconds` și
`endHoldSeconds` sunt opririle de la început, de la fiecare oprire de pe drum și de la sfârșit;
`lookAheadMeters`, `clearanceMeters` și `smoothingIterations` sunt cele ale zborului prin galerii.
`speeds` (`[1, 2]`) spune de câte ori viteza proprie a galeriei este parcursă fiecare porțiune
dintre două opriri: la intrare cu viteza galeriei, la ieșire de două ori mai repede.
Cu `"timing": "Fit"` (implicit) traseul este potrivit la durata din specificație. În panou,
șablonul **Route** zboară de la intrare până la stația cea mai îndepărtată, în durata clipului. O
stație pe care modelul nu o are oprește randarea, iar mesajul îi spune numele.

## Doi constructori de scenă

ThIDE are două moduri de a construi scena în Blender, iar **Constructor de scenă (avansat)** alege
între ele:

- **Auto** (implicit) folosește *playerul*, care arată tot ce poate cere o scenă.
- **Player** — constructorul mai nou. ThIDE scrie scena ca date (un *plan de scenă*), iar un
  program fix din Blender o redă. El scrie textele în a doua etapă, randează previzualizările și
  poate continua o randare care a fost oprită.
- **Generator** — constructorul mai vechi, păstrat așa cum era, pentru comparație. Este folosit
  doar când îl alegeți aici. Scrie câte un script Python pentru fiecare scenă. Desenează titlul,
  scara grafică, săgeata nordului și legenda adâncimii ca obiecte în scenă (unde un perete le
  poate ascunde) și nu desenează un cadru de titlu, linia de credite, subtitrările, cadrul de
  final, trecerile, [adnotările](#adnotări-etichete-note-cu-linie-marcaje-și-fotografii) sau o
  [dezvăluire](#dezvăluirea-peșterii-parte-cu-parte) și nu rotește o
  [imagine HDRI](#iluminarea-cu-o-imagine-hdri). Rezultatul spune ce a lăsat deoparte.

O previzualizare este mereu a playerului, orice s-ar alege aici. **Randează**, **Exportă
scriptul…** și **Deschide în Blender…** folosesc toate constructorul ales aici, așa că toate trei
arată aceeași scenă.

## Alte moduri de rulare

- **Exportă scriptul…** — în loc să randeze, ThIDE scrie în **Folder export** scena pe care ar
  arăta-o **Randează**, ca să o randați mai târziu, pe alt calculator, sau să o arhivați și să o
  distribuiți. Este același dosar pe care îl conține un
  [pachet de randare](#un-pachet-de-randare-pentru-randarea-pe-alt-calculator):

  | Fișier | Ce este |
  |---|---|
  | `scene-plan.json` | scena: modele, cameră, aspect, texte, adnotări, ieșire |
  | `render.py`, `thide_player/` | playerul, care construiește în Blender scena din plan |
  | `model.ply`, `photos/`, `hdri/` … | rețelele de poligoane și imaginile numite de plan |
  | `render.sh`, `render.cmd`, `README.txt` | scripturi care pornesc Blender și cum se folosesc |
  | `out/` | unde scrie randarea; subtitrările unui film sunt deja acolo, ca `.srt` și `.vtt` |

  Rulați-l cu `sh render.sh` (`render.cmd` pe Windows) sau cu
  `blender -b --factory-startup --python render.py`. Nimic din dosar nu are nevoie de ThIDE, iar
  dosarul poate fi mutat.

  **Script autonom (un singur fișier)**, de la **Ieșire**, face din export un singur `render.py`
  care conține planul, playerul, rețelele de poligoane și imaginile: un fișier mult mai mare,
  care se randează oriunde, în `out` de lângă el.

  Cu *Generator* ales drept constructor de scenă, exportul este în schimb cel al generatorului de
  scripturi: `render.py` cu `model.ply` și `scene-meta.json`, fără ceea ce acel constructor nu
  desenează (panoul le enumeră). *Script autonom* pune atunci datele scenei în script, iar
  **Include rețelele de poligoane în script** și rețeaua de poligoane; această a doua opțiune
  este doar pentru generatorul de scripturi, fiindcă fișierul unic al playerului își conține
  întotdeauna rețelele.
- **Deschide în Blender…** — deschide scena pe care ar arăta-o **Randează** în fereastra Blender,
  văzută prin camera ei, ca să vă uitați în jur sau să o modificați manual. Nu se randează nimic;
  o randare pe care o porniți de acolo scrie în **Folder randare**. Scena este construită de
  player, așa că totul este acolo: peștera, etichetele, adnotările, dezvăluirea, lumina.
  **Textele de peste imagine nu sunt**: ele sunt scrise peste cadrele terminate, în a doua
  etapă, iar fereastra Blender arată scena dinainte de aceasta. Panoul spune acest lucru când
  deschide o scenă care are texte.
- **Salvează** presetarea — stochează setările curente ca presetare proprie, cu tot cu texte și
  subtitrări; aceasta apare în bandă alături de cele integrate și se păstrează de la o sesiune la
  alta.

## Din linia de comandă

Același motor este disponibil fără interfață grafică prin CLI, pentru scripturi sau CI:

```
therion-cli blender <model.lox|.3d> [--preset "Orbit showcase" | --spec spec.json] \
                    [--out <dir>] [--export <dir>] [--blender <path>] \
                    [--draft] [--builder auto|player|generator] \
                    [--keep-frames] [--compose-only] [--frames <dir>] \
                    [--title <text>] [--credits <text>] [--end-card <text> [--end-card-seconds <n>]] \
                    [--caption "<start>-<end>:<text>"]... [--annotations <file.json>]... \
                    [--reveal date|order|distance [--reveal-from <station>] \
                     [--reveal-dates <file.3d>] [--reveal-seconds <start>-<end>]] \
                    [--hdri <image.hdr|.exr> [--hdri-rotation <degrees>] [--hdri-strength <n>]] \
                    [--leads <file.json>]... [--lamp-reach] \
                    [--route <from>[:<via>...]:<to> [--route-seconds <n>]]
```

`--export` scrie scriptul și resursele fără a rula Blender; altfel randează. Căile fișierelor
produse sunt afișate la ieșirea standard; codul de ieșire este `0` la succes, `1` la eșecul
randării, `2` la o eroare de utilizare sau de specificație.

- `--draft` randează previzualizarea rapidă în locul randării, ca butonul **Previzualizare** al
  panoului.
- `--builder` alege constructorul de scenă; `auto` este implicit și înseamnă playerul.
  `generator` cere constructorul mai vechi.
- `--title`, `--credits`, `--end-card` (cu `--end-card-seconds`) și `--caption` adaugă texte la
  cele ale presetării sau ale specificației. O subtitrare este un singur argument, cu timpii în
  secunde de la începutul filmului: `--caption "2.5-6:Sala Mare"`; dați opțiunea o dată pentru
  fiecare subtitrare. `{cave}` în oricare dintre ele ține locul numelui peșterii. Presetările
  integrate scriu acest nume ca titlu: `--title ""` randează fără titlu.
- `--hdri seara.hdr` luminează scena cu o imagine așezată de jur împrejurul ei, care este și
  fundalul: vedeți [Iluminarea cu o imagine (HDRI)](#iluminarea-cu-o-imagine-hdri).
  `--hdri-rotation 90` o rotește, iar `--hdri-strength 1.5` o face mai luminoasă. Merge și cu
  `--bundle`.
- `--lamp-reach` luminează scena cu lampa de pe cameră și o întărește după distanța la care
  privește camera: vedeți
  [O peșteră privită de departe](#o-peșteră-privită-de-departe-bătaia-lămpii). Nu merge împreună
  cu `--hdri`.
- `--route e1:b7:s40` zboară camera de la `e1`, prin `b7`, până la `s40`, de-a lungul galeriilor,
  oricare ar fi camera presetării: vedeți [Zborul pe un traseu între stații](#zborul-pe-un-traseu-între-stații).
  Traseul dă durata clipului; `--route-seconds 45` îl potrivește în schimb în 45 de secunde. Merge
  și cu `--bundle`.
- **Marcaje pentru continuări.** O presetare sau o specificație care marchează continuările (*Full
  flythrough* o face) marchează stațiile pe care modelul însuși le are semnalate drept continuări,
  lucru pe care îl conține un fișier `.lox` (în Therion: `station <nume> "<comentariu>"
  continuation`); un fișier `.3d` nu are asemenea semnalări. `--leads continuari.json` numește în
  schimb stațiile de marcat (o dată pentru fiecare fișier): o listă JSON ale cărei elemente sunt
  numele unei stații sau `{ "station": "main.12", "note": "curent de aer" }`. O stație pe care
  modelul nu o are este lăsată deoparte. Merge și cu `--bundle`.
- `--annotations note.json` adaugă etichetele, notele cu linie, marcajele și fotografiile dintr-un
  fișier la cele ale presetării sau ale specificației: vezi
  [Adnotări](#adnotări-etichete-note-cu-linie-marcaje-și-fotografii). Merge și cu `--bundle`. Un
  script scris cu `--export` este al generatorului de scripturi și le omite, așa cum omite
  subtitrările; comanda spune acest lucru.
- `--reveal date`, `--reveal order` sau `--reveal distance` arată peștera parte cu parte: vezi
  [Dezvăluirea peșterii, parte cu parte](#dezvăluirea-peșterii-parte-cu-parte). `--reveal-from
  main.12` măsoară o dezvăluire după distanță de la acea stație, în locul intrărilor;
  `--reveal-dates cave.3d` numește fișierul cu datele ridicării pentru un model care nu le are;
  `--reveal-seconds 1-9.5` spune când începe dezvăluirea și când este acolo toată peștera. Peste o
  presetare care dezvăluie deja, `--reveal` schimbă după ce se dezvăluie și îi păstrează timpii.
  Merge și cu `--bundle`; un script scris cu `--export` o omite, iar comanda spune acest lucru.
- **O randare care a fost oprită continuă.** O randare cu texte își ține cadrele în
  `<out>/<nume>-frames` cât timp rulează. Rulați din nou aceeași comandă după ce a fost oprită (sau
  după ce a fost oprit calculatorul): cadrele care există sunt păstrate și se randează doar cele
  care lipsesc. Cadrele unei alte scene (altă cameră, alt model, alt aspect) nu sunt folosite: se
  randează din nou.
- `--keep-frames` păstrează cadrele când filmul este scris (și randează în două etape chiar și
  fără texte). `--frames <dir>` le pune în altă parte.
- `--compose-only` nu randează nimic: scrie filmul din nou din cadrele care există, cu textele așa
  cum sunt acum. Schimbarea unui titlu sau a unei subtitrări costă atunci secunde, nu toată
  randarea:

  ```
  therion-cli blender cave.lox --preset "Full flythrough" --out renders --title "Peștera Mare" --keep-frames
  therion-cli blender cave.lox --preset "Full flythrough" --out renders --title "Peștera Mare, 2026" --compose-only
  ```

  Refuză cadrele care au fost randate dintr-o altă scenă, așa că nu poate pune din greșeală texte
  noi peste o imagine veche.

### Un pachet de randare, pentru randarea pe alt calculator

`--bundle` scrie tot ce îi trebuie lui Blender într-o singură arhivă zip (sau într-un dosar, dacă
calea nu se termină în `.zip`), ca scena să poată fi randată pe orice calculator cu Blender 4.2 sau
mai nou, fără ThIDE:

```
therion-cli blender <model.lox|.3d> [--preset "Full flythrough" | --spec spec.json] \
                    --bundle cave-bundle.zip [--draft] [--render [--render-plan full|draft|both]] \
                    [--out <dir>] [--blender <path>] [--readme-language en|ro] \
                    [--annotations <file.json>]... [--reveal date|order|distance ...]
```

Pachetul conține planul scenei (`scene-plan.json`: mișcarea camerei, aspectul și ieșirea, ca JSON
simplu), rețeaua de poligoane a peșterii (`model.ply`), playerul care construiește scena în
Blender (`render.py` și `thide_player/`), scripturile de rulare `render.sh` (Linux, macOS) și
`render.cmd` (Windows), un fișier `README.txt` care explică cum se randează și licența playerului:
`LICENSE` (Mozilla Public License 2.0) și un `NOTICE` care spune ce este playerul și că sursa lui se
află în depozitul ThIDE de pe GitHub. Arhiva conține un
singur dosar, cu numele ei (`cave-bundle` pentru `cave-bundle.zip`), așa că dezarhivată oriunde
face doar acel dosar. Cine o primește o dezarhivează și rulează `sh render.sh` sau `render.cmd` în
acel dosar; rezultatul ajunge în dosarul `out` al pachetului. Scripturile caută Blender în PATH (și în dosarul obișnuit de instalare pe macOS și
Windows); variabila de mediu `BLENDER` indică altă instalare, de exemplu
`BLENDER=/opt/blender-4.5/blender sh render.sh`.

Un pachet conține suprafața terenului și imaginea ei atunci când scena le desenează (`surface.ply`,
`surface.png`), adnotările cu fotografiile lor (`photos/`), imaginea care luminează scena
(`hdri/`) și textele de peste imagine, la `texts`
în plan. O scenă cu texte se randează și
acolo în două etape, în dosarul `frames` al pachetului și apoi în `out`; pornită din nou după ce a
fost oprită, continuă. `sh render.sh --keep-frames` păstrează cadrele, iar
`sh render.sh --compose` (sau `render.cmd --compose`) scrie filmul din nou din ele după ce un text
din `scene-plan.json` a fost schimbat. Subtitrările sunt deja în pachet ca fișiere de subtitrare
(`out/<nume>.srt`, `.vtt`).

- `--draft` adaugă o previzualizare: un al doilea plan, `scene-plan.draft.json`, cu un aspect
  rapid: vizualizarea solidă a Blender, la jumătate din dimensiunea imaginii, o fracțiune de
  secundă pe cadru.
  `sh render.sh --draft` (sau `render.cmd --draft`) îl randează ca `<nume>-draft`, ca să verificați
  mișcarea camerei înainte de randarea lungă.
- `--render` randează pachetul și aici, cu Blender-ul găsit de ThIDE (sau cel dat cu `--blender`):
  arhiva este dezarhivată într-un dosar temporar, iar ce se randează este mutat lângă arhivă sau în
  `--out`. Fără `--draft` se randează planul complet; cu `--draft` se randează previzualizarea,
  pentru că un pachet făcut cu `--draft` e de obicei făcut pentru o privire rapidă asupra mișcării
  camerei, înainte de randarea lungă. Randarea completă rămâne la o opțiune distanță:
  `--render-plan` alege `full` (planul complet), `draft` (previzualizarea) sau `both` (întâi
  previzualizarea, apoi planul complet). `draft` și `both` cer `--draft`.
- `--readme-language ro` scrie fișierul `README.txt` al pachetului în română; fără această
  opțiune, el este în engleză.
- Același model cu aceleași setări dă întotdeauna, sub același nume, aceeași arhivă, octet cu
  octet.
- Un plan de scenă spune tot ce poate spune o specificație de randare, așa că un pachet nu lasă
  nimic deoparte: afișarea și ascunderea temporizată a etichetelor se află în plan (la
  `changes`), iar imaginea unei iluminări HDRI este printre fișierele lui.

**Ce poate spune un plan de scenă.** `scene-plan.json` este JSON simplu, pe care orice editor de
text îl poate modifica și orice program îl poate scrie, și poate spune mai mult decât folosesc
scenele făcute de ThIDE:

- La `changes`, ce se întâmplă cât rulează clipul, sub formă de chei în secunde: un lucru este
  arătat, ascuns sau estompat (`visible`), este acolo doar în parte (`opacity`), capătă o culoare
  (`tint`); un model este întins (`scale`) și mutat (`offset`); o lumină este întărită sau slăbită
  (`energy` — un soare care se stinge când camera intră în subteran); lumea își schimbă culoarea
  și intensitatea (`worldColor`, `worldStrength`: și intensitatea unei imagini HDRI, așa că un cer
  poate păli când camera intră în peșteră). O cheie poate sta după sfârșitul clipului,
  două chei pe același cadru înseamnă o mișcare care se termină cu un salt, iar o culoare poate
  trece de la o cheie la alta așa cum amestecă un ecran culorile (`"mix": "srgb"`).
- Tot la `changes`, un model sau o linie este arătat parte cu parte (`reveal`): cheile sunt un
  prag, iar în fiecare clipă sunt acolo părțile a căror **valoare de dezvăluire** este sub el.
  Valorile sunt câte un număr pentru fiecare vârf al rețelei modelului: proprietatea de vârf
  `reveal` a unui fișier PLY (ThIDE o scrie când o specificație are o dezvăluire;
  `scene-meta.json` spune, la `reveal`, ce sunt valorile și ce interval acoperă), atributul
  `_REVEAL` al unui fișier GLB sau `reveal` din fâșiile unei linii, câte un număr pentru fiecare
  punct. Ele sunt în unitățile lor (ani, metri, un număr de ordine), la fel și pragul.
  `revealFront` spune cum arată marginea: `edge` (cât de departe în urma pragului este opacă o
  parte), `glow` (`color`, `strength`, `width`) și `attribute` (alt nume pentru valori). Un text cu
  un `counter` arată numărul la care a ajuns pragul, în locul semnului `{}` din el.
- La `annotations`, etichetele, notele cu linie, marcajele și fotografiile, fiecare cu locul și
  mărimea ei în metri. O adnotare poate ține de un model (`"model"`): merge atunci acolo unde merge
  punctul ei din model, cât timp modelul este întins sau mutat. Când este prezentă se spune
  printr-o schimbare a ei, după `id`: cheile ei `visible`, cu un `fade`.
- La `lines`, linii prin puncte date, desenate ca tuburi subțiri: vizele unei ridicări, un traseu.
- O rampă de culori pe înălțime poate fi măsurată în modelul însuși (`"space": "model"`), așa că
  culorile rămân pe rocă atunci când un model este întins sau mutat.
- O poziție a camerei poate avea propriul interval de vizibilitate (`clipStart`, `clipEnd`),
  pentru un zbor care vine de departe până aproape.
- La `look.world`, ce înconjoară scena: o singură culoare, un cer de zi sau o imagine (`"kind":
  "image"`, cu fișierul ei `image`, cu intensitatea `strength` și cu rotirea `rotation`, în
  grade).

O previzualizare arată nuanța unui model, iar cât dintr-un model care se estompează mai este acolo
se vede după partea din fețele lui pe care le desenează; o dezvăluire este o tăietură de-a lungul
fețelor; o fotografie este acolo un mozaic al culorilor ei. Schema care descrie fiecare câmp se află
în depozitul ThIDE (`src/Therion.Blender/Plan/scene-plan.schema.json`).

## Un film din mai multe secvențe: storyboardul

O randare este o singură mișcare a camerei, cu un singur aspect. Un **storyboard** este un film
făcut din mai multe: o listă de **secvențe**, fiecare o mișcare a camerei cu aspectul ei, legate
prin tăieturi, treceri printr-o culoare și suprapuneri, cu texte peste o secvență sau peste tot
filmul. Este un fișier JSON (`*.storyboard.json`) pe care îl scrieți sau îl modificați în orice
editor de text și îl randați din linia de comandă:

```
therion-cli blender <model.lox|.3d> --storyboard film.storyboard.json [--out <dir>] \
                    [--draft] [--shot <n>] [--keep-frames] [--compose-only] [--frames <dir>] \
                    [--bundle film-bundle.zip [--render [--render-plan full|draft|both]]] \
                    [--blender <path>] [--leads <file.json>]...
```

Un exemplu inventat, cu patru secvențe:

```json
{
  "version": 1,
  "name": "{cave}: un scurt film",
  "fps": 30,
  "defaults": {
    "materials": { "rock": "DepthGradient" },
    "labels": { "overlays": { "scaleBar": true, "northArrow": true } }
  },
  "texts": {
    "title": "{cave}", "titleSeconds": 3,
    "credits": "Topografie: Clubul Speologic Inventat",
    "endCard": { "text": "{length} cartați, {depth} adâncime", "seconds": 4 },
    "fadeFromBlack": 1
  },
  "shots": [
    { "name": "Din afară", "preset": "Orbit showcase", "seconds": 12 },
    { "name": "În josul puțului", "preset": "Helix descent", "seconds": 15,
      "in": { "kind": "Dissolve", "seconds": 1 },
      "texts": { "corner": "Puțul de intrare", "northArrow": true } },
    { "name": "Cum a fost descoperită", "preset": "Map reveal", "seconds": 20,
      "in": { "kind": "Dip", "seconds": 0.6 } },
    { "name": "Galeria principală", "preset": "Full flythrough", "seconds": 30,
      "reveal": "hold" }
  ],
  "output": { "kind": "Video", "container": "Mp4", "width": 1920, "height": 1080,
              "baseName": "film-inventat" }
}
```

**Ce spune o secvență.** O secvență (un element din `shots`) are un nume (`name`), durata în
secunde (`seconds`) și oricare dintre secțiunile unei specificații de randare: `camera`,
`materials`, `lighting`, `engine`, `labels`, `annotations`, `reveal`. Ce lipsește se ia din
presetarea pe care o numește (`preset`) sau din valorile implicite ale storyboardului
(`defaults`). O presetare aduce mișcarea camerei, materialele, lumina, motorul de randare,
etichetele din scenă și dezvăluirea, precum și durata, dacă secvența nu o dă; nu aduce și titlul
ei, pentru că un film are textele lui. Cuvintele dintr-o listă (`Dissolve`, `Orbit`,
`DepthGradient`) sunt citite indiferent de majuscule, iar o cheie necunoscută este refuzată cu
numele ei, așa că o cheie scrisă greșit nu este trecută sub tăcere. Camera unei secvențe poate
zbura pe un traseu (vedeți [Zborul pe un traseu între
stații](#zborul-pe-un-traseu-între-stații)): cu `"timing": "Natural"` și fără `seconds`, secvența
durează cât îi ia traseului, iar cu `seconds` traseul este potrivit la acea durată.

**Cum începe o secvență** (`in`): cu o tăietură, dacă nu spune nimic; cu o trecere printr-o
culoare (`Dip`), în care imaginea se duce într-o culoare (`color`, implicit negru) pe ultimele
cadre ale secvenței dinainte și iese din ea pe primele cadre ale acesteia; sau cu o suprapunere
(`Dissolve`), în care imaginea trece treptat de la o secvență la cealaltă. O suprapunere de o
secundă pornește secvența cu o secundă înainte de sfârșitul celeilalte, iar amândouă sunt randate
pentru acea secundă, așa că filmul este cu atât mai scurt decât secvențele lui adunate. `seconds`
spune cât durează trecerea (ambele jumătăți) sau suprapunerea; nu poate fi mai lungă decât
secvențele pe care le leagă. Prima secvență începe filmul; ieșirea din negru este unul dintre
textele filmului.

**Texte.** Textele storyboardului (`texts`) sunt scrise peste tot filmul, cu timpii socotiți de la
începutul filmului: un titlu sau un cadru de titlu, credite, un text de colț, subtitrări, cadrul
de final după ultima secvență, ieșirea din negru și trecerea în negru. Textele unei secvențe
(`texts`) sunt scrise peste acea secvență, cu timpii socotiți de la începutul ei: un titlu,
subtitrări, un text de colț, precum și scara grafică și săgeata nordului, care sunt măsurate prin
camera acelei secvențe și sunt ascunse cât durează o trecere sau o suprapunere. O secvență fără
`texts` le are pe cele implicite (`defaults.labels.overlays`): în exemplu, prima, a treia și a
patra secvență au scară grafică și săgeata nordului, iar a doua, care are propriile `texts`, are
un text de colț și săgeata nordului. Subtitrările filmului și ale secvențelor sunt scrise lângă
film (`.srt`, `.vtt`).

**Dezvăluirea.** O secvență poate arăta peștera parte cu parte (vedeți [Dezvăluirea peșterii,
parte cu parte](#dezvăluirea-peșterii-parte-cu-parte)), cu timpii socotiți de la începutul
secvenței. Secvența următoare spune ce se întâmplă mai departe: `"reveal": "hold"` păstrează
peștera așa cum a lăsat-o secvența dinainte, `"reveal": "none"` o arată pe toată, iar o dezvăluire
proprie o ia de la capăt. Contorul numără mai departe de-a lungul filmului. Toate dezvăluirile
unui storyboard sunt după același lucru (toate după dată, sau toate după distanța de la aceeași
stație), pentru că secvențele folosesc același model.

**Randarea.** Fiecare secvență este randată într-un dosar propriu (`<out>/<nume>-frames/s01`,
`s02`, …), fiecare într-un Blender propriu; apoi filmul este scris din acele cadre, într-o ultimă
trecere, care leagă secvențele și scrie textele.

- Dacă randarea este oprită, rulați din nou aceeași comandă: secvențele terminate sunt păstrate,
  iar randarea continuă în secvența în care s-a oprit.
- Cu `--keep-frames`, schimbați o secvență în storyboard și rulați din nou comanda: doar acea
  secvență este randată din nou. Dacă schimbați un text al filmului sau felul în care se leagă
  două secvențe, nu se mai randează nimic: filmul este scris din cadrele existente.
- `--draft` face o previzualizare a fiecărei secvențe (vizualizarea solidă din Blender, la
  jumătate din mărime): o schiță animată a întregului film, într-o fracțiune din timp, pentru a
  verifica mișcările, duratele și legăturile înainte de randarea lungă.
- `--shot 3` randează doar a treia secvență, ca pe orice scenă, cu textele ei; opțiunile care
  adaugă ceva unei scene (`--title`, `--caption`, `--reveal`, …) merg împreună cu ea.
- `--bundle` scrie filmul ca [pachet de
  randare](#un-pachet-de-randare-pentru-randarea-pe-alt-calculator): planul fiecărei secvențe și
  al filmului, cu o singură copie a rețelei de poligoane pentru toate. `sh render.sh` (sau
  `render.cmd`) îl randează acolo secvență cu secvență, iar `--draft`, `--keep-frames` și
  `--compose` fac pentru film ce fac pentru o scenă.

O fotografie, o imagine HDRI și un fișier cu datele ridicărilor sunt numite relativ la fișierul
storyboardului. Un storyboard este un film: pentru imagini statice sau pentru un fișier Blender al
scenei, randați o singură secvență (`--shot`) sau folosiți o specificație de randare. Schema care
descrie fișierul se află în depozitul ThIDE (`src/Therion.Blender/storyboard.schema.json`).

## Presetări compuse: un film întreg ca șablon

O **presetare compusă** este un storyboard care se potrivește oricărei peșteri: nu numește nicio
stație, doar locuri pe care modelul le găsește (intrarea, stația cea mai îndepărtată, galeria
topografiată ultima) și un tur al galeriilor, iar textele ei spun `{cave}`. La randare, ThIDE
citește modelul și face din ea filmul acelei peșteri. Trei vin cu ThIDE:

| Presetare compusă | Filmul |
|---|---|
| **Showcase** | o orbită în jurul peșterii, apoi galeria principală de la intrare la stația cea mai îndepărtată, apoi o elice în jos |
| **Grand tour** | o orbită, apoi un [tur al galeriilor](#tururi-ale-galeriilor) principale și al galeriilor laterale care merită (cel mult două minute) și un card de final |
| **Exploration history** | peștera apărând în ordinea în care a fost topografiată, cu un contor al anilor, apoi un zbor de-a lungul galeriei topografiate ultima, cu toată peștera la vedere |

Într-un fișier storyboard, simbolurile sunt două chei ale unei camere pe traseu:

```json
{ "name": "Tur", "preset": "Full flythrough", "seconds": 120,
  "camera": { "template": "Route", "route": { "branches": "auto" } } }
{ "name": "Găsită ultima", "preset": "Full flythrough", "seconds": 14, "reveal": "hold",
  "camera": { "template": "Route", "route": { "from": "newest" } } }
```

`"branches": "auto"` face din secvență un tur al galeriilor peșterii, de la `from` al traseului
(intrarea, când lipsește), în secundele secvenței, cu aspectul presetării ei; secvențele turului îi
iau locul. `"from": "newest"` zboară galeria din ultima zi de topografiere, de la capătul ei mai
apropiat de intrare la celălalt; un model fără date de topografiere zboară în schimb galeria
principală. Ce s-a făcut apare în avertismentele randării. Presetările compuse proprii stau lângă
presetările dumneavoastră, ca fișiere `*.composite.json` (un `name`, o `description` și
`storyboard`): **Salvează ca presetare compusă** din [fila Storyboard](#fila-storyboard) scrie una.

Traseul unei secvențe de parcurs poate spune și ce galerii laterale vizitează turul. `include` și
`exclude` primesc ce primesc `--include` și `--exclude` (numărul unei ramificații, ca `B3`, o
stație din ea, numele unei topografii) și au întâietate față de regulile turului; `priority`
listează numere de ramificații, cele mai dorite întâi: când filmul depășește timpul, galeriile
laterale pe care le numește sunt lăsate deoparte după celelalte, de la coada listei în sus.
`language` al unui storyboard (`en` sau `ro`) este limba textelor pe care filmul le scrie singur,
textele din colț ale unui tur și notele pistelor lui. Când lipsește, ele sunt în limba ThIDE când
panoul randează filmul și în engleză din linia de comandă.

```json
{ "version": 1, "name": "{cave}: turul meu", "language": "ro",
  "texts": { "title": "{cave}", "titleSeconds": 3, "endCard": { "text": "Clubul de Speologie Inventat" } },
  "shots": [
    { "name": "Afară", "preset": "Orbit showcase", "seconds": 10 },
    { "name": "Tur", "preset": "Full flythrough", "seconds": 120, "in": { "kind": "Dip", "seconds": 0.6 },
      "camera": { "template": "Route",
                  "route": { "branches": "auto", "exclude": ["B3"], "priority": ["B5", "B1"] } } } ] }
```

**Din linia de comandă**, `--composite` face dintr-o presetare compusă filmul modelului și îl
randează, îl schițează sau îl pune într-un pachet, așa cum face `--storyboard` cu un storyboard.
Primește o presetare compusă inclusă după nume (oricum ar fi scris cu majuscule) sau fișierul
uneia: cele salvate din panou sunt în dosarul presetărilor dumneavoastră
(`%AppData%\ThIDE\blender-presets` pe Windows, `~/.config/ThIDE/blender-presets` pe Linux).
`--tour-language ro` scrie textele tururilor ei în română, orice ar spune presetarea; `--shot N`
randează doar una dintre secvențele ei.

```
therion-cli blender cave.lox --composite "Grand tour" --draft
therion-cli blender cave.lox --composite ~/.config/ThIDE/blender-presets/my-tour.composite.json --bundle film.zip
therion-cli blender cave.lox --composite Showcase --tour-language ro --out renders
```

## Fila Storyboard

Panoul are două file deasupra setărilor. **Scenă** este o singură randare, ca mai sus.
**Storyboard** este un film din mai multe secvențe:

- **Adaugă secvență** adaugă o secvență din presetarea aleasă sus în panou, după secvența
  selectată; **Adaugă secvențele** adaugă toate secvențele presetării compuse alese. Fiecare rând
  arată numele secvenței (tastați ca s-o redenumiți), camera ei (sau *parcurs*), secundele și cum
  începe; săgețile o mută, iar ✕ o elimină. Banda de sub listă arată secvențele de-a lungul
  filmului.
- Butonul cu numărul unui rând selectează secvența: setările panoului de mai jos o arată atunci,
  iar orice schimbare făcută acolo se scrie în ea. **Începe cu** alege o tăietură, o trecere prin
  negru sau o suprapunere și secundele ei. Setările de ieșire (mărimea, cadrele pe secundă,
  numele fișierului) sunt ale filmului.
- **Filmul**, sub secvențe: **Titlu** (cu un **Subtitlu** și secundele **Cadrului de titlu**,
  pentru un titlu care stă în mijlocul imaginii înainte să se stingă; 0 îl ține sus), **Cadru de
  final** (cu un subtitlu și secundele lui; gol înseamnă niciunul) și **Limba textelor**: limba în
  care un parcurs își scrie textele din colț și notele pistelor. *Ca interfața* urmează limba ThIDE
  (engleza, când un film nu poate fi scris în ea). O presetare compusă își aduce titlul, cadrul de
  final și limba când este adăugată unui film gol.
- **Salvează ca presetare compusă** păstrează filmul ca presetare compusă proprie, sub numele și
  descrierea tastate alături; lista presetărilor compuse o oferă de atunci, iar linia de comandă
  îi primește fișierul (`--composite`). Presetările dumneavoastră pe care le numesc secvențele
  sunt scrise în ele, așa că presetarea compusă se randează la fel acolo unde nu sunt cunoscute;
  unde se scrie filmul rămâne pe seama celui care îl randează. Numele unei presetări compuse
  incluse este refuzat; una proprie cu același nume este înlocuită.
- **O secvență de parcurs** (*parcurs* în listă, ca în Grand tour) are un tabel al galeriilor ei
  laterale. **Arată galeriile** citește modelul și calculează turul așa cum îl va calcula
  randarea: fiecare galerie laterală cu numărul ei, dacă este ramificație sau ocolire, ramificația
  de unde pleacă, lungimea și ce face filmul cu ea (parcursă și tăietură înapoi, parcursă
  dus-întors, parcursă de la un capăt la altul, o privire, sau de ce e lăsată deoparte: prea
  scurtă, peste numărul pe care îl ia parcursul, peste timpul filmului). Lista este în ordinea în
  care sunt dorite galeriile, cele mai bine clasate întâi. **Vizită** spune *după reguli*,
  *vizitează* sau *lasă deoparte* pentru fiecare, iar săgețile mută una mai sus sau mai jos: când
  filmul depășește timpul, galeriile de la coadă sunt lăsate deoparte primele. Orice schimbare se
  scrie în traseul secvenței (`include`, `exclude`, `priority`, ca
  [mai sus](#presetări-compuse-un-film-întreg-ca-șablon)), iar tabelul este calculat din nou;
  notele de sub el spun ce a lăsat deoparte sau a scurtat turul.
- **Schiță a tuturor** schițează toate secvențele, **Schiță a secvenței** doar pe cea selectată,
  **Estimare** spune câte cadre, cât timp, cât disc și câtă memorie cere filmul, iar **Randează
  filmul** arată estimarea și randează. Randările își așteaptă rândul în coada de randare, listată
  sub butoane cu câte un **Renunță**; o randare în mijlocul căreia ThIDE a fost închis continuă la
  pornirea următoare și apare ca *reluată*.

## Tururi ale galeriilor

Un **tur** este un film al peșterii făcut după reguli, nu de mână: galeria principală este
parcursă de la intrare spre interior, iar la fiecare joncțiune care merită camera intră în galeria
laterală, îi arată capătul și revine. ThIDE calculează turul din model, îl încadrează în timpul
pe care îl dați și îl scrie ca [storyboard](#un-film-din-mai-multe-secvențe-storyboardul), care se
randează, se previzualizează sau se împachetează ca oricare altul și poate fi mai întâi editat de
mână.

```
therion-cli blender pestera.lox --tour --draft
therion-cli blender pestera.lox --tour --write-storyboard tur.storyboard.json
therion-cli blender pestera.lox --storyboard tur.storyboard.json
```

Prima linie face o previzualizare a întregului tur, de privit înainte de randarea lungă. A doua
scrie storyboardul și nu randează nimic; a treia îl randează, după ce ați schimbat ce ați dorit.
Fiecare comandă `--tour` afișează tabelul galeriilor laterale și ce a hotărât despre fiecare.

**Galeria principală** (`--trunk`). Implicit merge de la prima stație semnalată ca intrare până la
stația cea mai îndepărtată de ea de-a lungul galeriilor (`farthest`); un model fără intrare
primește cea mai lungă galerie, așa cum o parcurge șablonul *Flythrough* (`longest`). `deepest`
merge până la stația cea mai de jos, `through` până la o altă intrare (sau până la `--tour-end`),
iar `via` prin stațiile pe care le numiți (`--tour-via s20 --tour-via s77 --tour-end s140`).
`--tour-start` numește alt început. Galeria principală este parcursă o singură dată și nu trece
de două ori prin aceeași stație.

**Galerii laterale.** După ce se scot vizele galeriei principale, fiecare rest conex al peșterii
este un sistem lateral. Unul care atârnă de galeria principală într-o singură stație este o
**ramură**: este parcursă de la joncțiunea ei până la stația ei cea mai îndepărtată. Unul care
atârnă de ea în două stații este o **ocolire** (o buclă apare ca ocolire): este parcursă de la
prima joncțiune până la a doua, apoi filmul revine la prima și continuă pe galeria principală,
astfel că nimic nu este parcurs de două ori și nimic nu este sărit. Sunt numerotate **B1, B2, …**
de-a lungul galeriei principale; numerele nu se schimbă în funcție de ce este ales. Tabelul spune
pentru fiecare: joncțiunea, lungimea (toate vizele ei), **raza de acțiune** (cât de departe ajunge
de galeria principală), diferența de nivel, lățimea medie (stânga + dreapta din ridicare), pistele,
ridicările și rangul.

**Care sunt vizitate.**

| Regulă | Implicit | Opțiune |
|---|---|---|
| Lungimea minimă | cea mai mare dintre 50 m și 2 % din galeria principală | `--min-length 30` |
| Câte | 12, cele mai bine clasate întâi | `--max-branches 8` |
| Rang | după raza de acțiune, apoi după lungime | |
| Piste | o galerie laterală cu o pistă este vizitată oricât de scurtă ar fi | `--no-lead-branches` |
| De mână | bate orice regulă: numărul unei ramuri, o stație din ea sau numele unei ridicări | `--include B2`, `--exclude side2` |
| Adâncime | 1: ce atârnă de galeria principală; 2 și ce atârnă de ramurile vizitate (B3.1, B3.2) | `--tour-depth 2` |

Pistele sunt stațiile pe care modelul le are semnalate drept continuări, sau cele din `--leads
<fișier.json>`.

**Cum este vizitată o galerie laterală** (`--branch-mode` pentru toate, `--branch B5=return`
pentru una):

| Mod | Ce face camera | Timp de film |
|---|---|---|
| `cut` (implicit) | intră, încetinește, se oprește la o rază de galerie înainte de capăt și stă 1,5 s; apoi filmul taie înapoi la joncțiune, printr-o trecere prin negru de 0,3 s | zborul de intrare |
| `return` | ca la `cut`, apoi se întoarce și zboară înapoi de două ori mai repede | de aproximativ 1,6 ori |
| `peek` | rămâne la joncțiune și privește în galeria laterală 3 s | 3 s |
| `skip` | trece mai departe | deloc |

O ocolire este parcursă întotdeauna până la capăt. `--peek-short` aruncă o privire, de la
joncțiune, în galeriile laterale lăsate deoparte ca prea scurte. Cât timp se vede o galerie
laterală, numărul și lungimea ei stau în colțul imaginii („Galerie laterală B3 · 96 m”, cu
`--tour-language ro`; implicit textele sunt în engleză), iar fiecare pistă de pe drumul camerei
are un marcaj și nota ei cât timp camera este aproape. Secvențele unui tur nu arată etichetele de
stații ale peșterii.

**Timpul.** Camera zboară cu viteza dată de mărimea galeriei (vezi [Zborul pe un traseu între
stații](#zborul-pe-un-traseu-între-stații)), așa că filmul are o durată firească. Două bugete o
țin în frâu: `--tour-seconds` (180) pentru film și `--branch-seconds` (20) pentru o galerie
laterală. Când ceva depășește bugetul, se face, în această ordine:

1. o galerie laterală este parcursă mai repede, până la de două ori viteza; apoi este parcursă
   doar cât îi permite timpul, iar colțul spune cât mai continuă („continuă încă 60 m”). Un zbor
   de întoarcere care nu încape devine mai întâi o tăietură;
2. întregul tur este parcurs mai repede, până la de două ori viteza;
3. zborurile de întoarcere devin tăieturi, cele mai slab clasate întâi;
4. galeriile laterale cele mai slab clasate sunt lăsate deoparte (întâi privirile; cele alese de
   mână, ultimele);
5. galeria principală însăși este scurtată.

Tot ce a fost lăsat deoparte sau scurtat este spus în notele pe care le afișează comanda și în
README-ul unui pachet.

Un exemplu inventat: o galerie principală de 620 m de la `e1` la `s140`, nouă sisteme laterale,
180 s.

| Ramură | Joncțiune | Lungime | Rază | Piste | Aleasă | Mod | Secunde |
|---|---|---|---|---|---|---|---|
| trunk | e1 → s140 | 620 m | — | 1 | da | — | 96 |
| B1 | s12 | 140 m | 110 m | 0 | da | cut | 20 (viteză 1,4×) |
| B2 | s31 | 12 m | 10 m | 0 | nu (scurtă) | skip | 0 |
| B3 | s48 | 96 m | 80 m | 2 | da | cut | 17 |
| B4 | s48 → s77 | 190 m | ocolire | 0 | da | through | 20 (scurtată la 130 m: continuă încă 60 m) |
| B5 | s90 | 22 m | 20 m | 1 | da (pistă) | return | 10 |
| B6–B9 | … | 8–30 m | … | 0 | nu (scurtă) | skip | 0 |

Filmul: de la `e1` la `s12`, în B1 și o tăietură înapoi, mai departe până la `s48`, în B3, prin B4
cât permit 20 s, înapoi la `s48` și mai departe, pe lângă `s77`, până la `s90`, în B5 și înapoi,
apoi până la `s140`. (Tabelul afișat de comandă este în engleză.)

**Un fișier de tur** spune același lucru ca opțiunile și călătorește împreună cu proiectul
(`--tour-file tur.json`; o opțiune dată pe lângă el are întâietate):

```json
{
  "trunk": "farthest", "start": "e1",
  "minLength": 30, "maxCount": 8, "include": ["B2"], "exclude": ["B7"],
  "mode": "cut", "modes": { "B5": "return", "B9": "peek" },
  "priority": ["B5", "B1"],
  "tourSeconds": 180, "branchSeconds": 20,
  "preset": "Full flythrough", "language": "ro",
  "output": { "width": 1920, "height": 1080, "baseName": "tur-pestera" }
}
```

`priority` listează numere de ramificații, cele mai dorite întâi: peste timp, galeriile laterale
pe care le numește sunt lăsate deoparte după celelalte, de la coada listei în sus (o privire tot
înaintea unei vizite, iar o vizită aleasă de mână ultima). `preset` (`--preset`) este presetarea
din care filmul își ia materialele, iluminarea și motorul
de randare; ele sunt scrise în `defaults` din storyboard, unde le puteți schimba pentru toate
secvențele deodată. `--bundle` scrie turul ca [pachet de
randare](#un-pachet-de-randare-pentru-randarea-pe-alt-calculator), cu tabelul și notele în
README. Un agent are aceleași lucruri prin uneltele `plan_tour` (tabelul, fără să scrie nimic) și
`write_tour_storyboard`.

Bine de știut:

- Turul rămâne în partea de peșteră în care se află începutul lui; părțile pe care nicio viză nu
  le leagă de ea sunt numite într-o notă și nu sunt vizitate.
- O galerie laterală parcursă la adâncimea 1 este parcursă până la stația ei cea mai îndepărtată;
  galeriile ei laterale sunt socotite în lungimea și în pistele ei și sunt vizitate doar cu
  `--tour-depth 2`.
- Storyboardul numește fiecare stație pe lângă care trece camera, deci aparține acestui model:
  după ce ridicarea s-a schimbat, faceți turul din nou, în loc să editați un storyboard vechi.

## Peșteri mari, randări lungi

**Pereții pe părți.** La o peșteră mare (peste o jumătate de milion de triunghiuri de pereți sau
peste doi kilometri în lățime) pereții sunt scriși pe părți: galeriile, câteva sute de metri pe
parte (după o rețea de 100 m pentru pereții desenați din scrapuri). Părțile împreună sunt aceeași
rețea de poligoane, vârf cu vârf, cu aceleași culori de adâncime și aceeași dezvăluire, deci
imaginea nu se schimbă. Se schimbă doar faptul că o cameră **în interiorul** peșterii (un zbor
prin galerii, un traseu) arată numai părțile aflate la cel mult 200 m de drumul ei; o cameră din
afară (o orbită, o imagine statică) le arată pe toate. `--parts on` împarte orice peșteră, `--parts
off` nu împarte niciuna.

**Etichete lângă traseu.** O cameră din interior etichetează doar stațiile și marchează doar
continuările aflate la cel mult 200 m de drumul ei; limita de 200 de etichete de stații se aplică
apoi acestora. Într-un film, fiecare secvență și le alege pe ale ei. O adnotare a cărei stație se
află într-o parte pe care secvența o lasă deoparte este lăsată deoparte odată cu ea, ca nimic să
nu plutească acolo unde nu sunt pereți.

**Cât va costa.** Înainte de începerea randării, progresul spune cât va costa: cadrele, timpul
după secundele presupuse pe cadru (sau măsurate), discul pentru cadre și film și memoria de care
va avea nevoie Blender. O scenă cu peste 20 de milioane de triunghiuri primește un avertisment (o
placă grafică s-ar putea să nu o încapă). În timpul randării, progresul spune câte secunde ia un
cadru și cât timp a mai rămas, după cadrele scrise până atunci. În linia de comandă, `--estimate`
afișează estimarea și nu randează nimic:

    therion-cli blender pestera.lox --storyboard tur.storyboard.json --estimate

**Un interval de cadre.** `--frame-range 1-600 --frames <dosar>` randează doar cadrele 1–600 ale
unei scene în dosarul de cadre și nimic altceva. Mai multe randări pot folosi același dosar în
același timp, fiecare cu intervalul ei; filmul se scrie după ce toate sunt gata:

    therion-cli blender pestera.lox --spec tur.json --frames cadre --frame-range 1-900
    therion-cli blender pestera.lox --spec tur.json --frames cadre --frame-range 901-1800
    therion-cli blender pestera.lox --spec tur.json --frames cadre --compose-only

Merită pe un calculator cu **mai multe plăci grafice**: porniți câte o randare pentru fiecare
placă, fiecare văzând doar placa ei (`CUDA_VISIBLE_DEVICES=0`, `=1`, … la NVIDIA), fiecare cu
intervalul ei. Pe un singur calculator fără ele se câștigă puțin, pentru că Cycles folosește deja
toate nucleele. Secvențele unui film se randează oricum una câte una; pentru un interval dintr-o
secvență, randați doar acea secvență (`--shot`).

**O randare odată.** În ThIDE randările rulează pe rând, oricine le pornește: o randare pornită din
panou în timp ce randează un agent (sau invers) își așteaptă rândul, iar panoul spune asta. Randarea
unui agent care s-a terminat apare în istoricul panoului. Dacă ThIDE se oprește în mijlocul unei
randări (nu când aceasta este anulată), randarea continuă cu cadrele ei la următoarea pornire a
ThIDE.

## Executorul de randări

`therion-cli render-worker` randează [pachetele de
randare](#un-pachet-de-randare-pentru-randarea-pe-alt-calculator) care sosesc într-un dosar, pe
rând, de la cel mai vechi, și lasă rezultatele în alt dosar. Este pentru un calculator cu Blender și
o placă grafică bună, care nu are nimic altceva din ThIDE; felul în care se umplu dosarele (un
dosar sincronizat, un SFTP) rămâne la alegerea dumneavoastră.

    therion-cli render-worker --inbox /srv/render/in --outbox /srv/render/out

- Puneți un pachet în dosarul de intrare ca `<lucrare>.zip`. Cât timp se randează,
  `<lucrare>.status.json` din dosarul de ieșire spune cât a avansat: secvența, cadrul, secundele pe
  cadru și timpul rămas. La final, dosarul de ieșire conține `<lucrare>.mp4` (sau imaginile),
  subtitrările și `<lucrare>.log`.
- Pentru a opri o lucrare, puneți un fișier gol `<lucrare>.cancel` lângă arhiva ei. Cadrele ei sunt
  păstrate; ștergeți fișierul și lucrarea continuă cu ele. Un executor oprit (Ctrl+C) continuă și
  el cu cadrele lucrării la următoarea pornire.
- `--once` se oprește când dosarul de intrare este gol; `--blender` numește Blender-ul;
  `--max-hours`, `--max-triangles` și `--max-bundle-gb` stabilesc limitele unei lucrări;
  `--scratch` este locul unde pachetele sunt dezarhivate și cadrele păstrate.

**Securitate.** Un pachet este o colecție de date, iar executorul îl tratează ca atare. Nu rulează
niciodată `render.py`, `render.sh` sau `thide_player` din pachet: citește planurile, rețelele de
poligoane și imaginile și le redă cu propriul player. Refuză, înainte să pornească Blender, un pachet
ale cărui planuri numesc dosare de module sau funcții din afara celor ale playerului, o cale care
iese din pachet, o legătură simbolică, un plan care nu trece verificările și un pachet peste limite
(dimensiunea lui, dimensiunea dezarhivată, triunghiurile, cadre × pixeli × eșantioane, timpul).
Module în plus vin doar dintr-un dosar numit de operator (`--modules`). Blender rulează cu `-b
--factory-startup` (fără add-on-uri, fără scripturi de pornire) și în dosarul de ieșire ajung doar
fișierele pe care executorul le numește. Rămâne riscul unui defect în cititoarele de rețele de
poligoane și imagini ale Blender: rulați executorul ca un utilizator care poate citi și scrie cele
două dosare și prea puțin altceva.

### Randarea pentru un server

Același executor își poate lua lucrările de la un server care păstrează pachete de randare (un nod
de randare al unui server precum SilexGIS), în loc de un dosar: cere serverului o lucrare, descarcă
pachetul, îl randează, trimite înapoi filmul, subtitrările și jurnalul Blender și cere următoarea
lucrare. Serverul nu se conectează niciodată la calculator; calculatorul este cel care apelează
serverul. Protocolul este descris în
[`docs/dev/render-worker-protocol.md`](../../dev/render-worker-protocol.md) (în engleză).

    therion-cli render-worker --server https://example.org/api/v1/render-nodes/ \
        --token-file ~/.config/thide-node/token --node-name gpu-1

- `--server` este adresa dată de administratorul serverului; trebuie să fie HTTPS (HTTP simplu
  este acceptat doar către acest calculator, pentru teste). `--token-file` este un fișier al cărui
  prim rând este tokenul nodului, dat de administrator; trebuie să poată fi citit doar de
  dumneavoastră (`chmod 600`), altfel executorul refuză să pornească. Tokenul nu este scris
  niciodată în jurnal.
- `--node-name` este numele afișat de server (implicit numele calculatorului); `--scratch` este
  locul unde se descarcă pachetele și se păstrează cadrele (câte un dosar pentru fiecare lucrare);
  `--poll-seconds` (30) este cât așteaptă o lucrare înainte de a întreba din nou; `--once` face o
  singură cerere și randează ce primește. `--blender`, `--modules` și limitele sunt cele ale
  executorului cu dosare; limitele unei lucrări le pot doar micșora.
- Filmul este scris în containerul cerut de lucrare: implicit MP4 (H.264) sau WebM, pe care un
  server îl poate prefera pentru că H.264 vine cu condițiile unui grup de brevete.
- O lucrare anulată pe server oprește Blender la următorul semnal de prezență (o treime din durata
  rezervării, 40 s pentru o rezervare de 2 minute). O rețea care cade este încercată din nou, cu
  așteptări tot mai lungi; dacă serverul nu poate fi atins o rezervare întreagă, lucrarea este
  oprită, iar serverul o dă altcuiva. Cadrele ei rămân: dacă aceeași lucrare revine la acest
  calculator, continuă de la ele. Un token refuzat de server oprește executorul (codul de ieșire 3).

**Pregătirea unui calculator cu placă grafică.**

1. Instalați Blender 4.2 sau mai nou de pe [blender.org](https://www.blender.org/download/)
   (versiunile LTS sunt cele testate), verificați descărcarea cu SHA-256 publicat pe site și
   dezarhivați-o, de exemplu în `/opt/blender`. Verificați că vede placa: în preferințele Blender
   sau randând cu Cycles un pachet mic cu executorul cu dosare: fișierul `<lucrare>.log` spune
   `THIDE:device=OPTIX` sau `CUDA` (pe o placă NVIDIA), `HIP`, `ONEAPI` sau `METAL`, și `CPU` când
   nicio placă nu a putut fi folosită.
2. Compilați `therion-cli` din sursele ThIDE cu .NET SDK:
   `dotnet publish src/Therion.Cli -c Release -o /opt/thide-cli`.
3. Creați un utilizator doar pentru executor (`useradd --system --create-home thide-render`), puneți
   fișierul cu tokenul în dosarul lui personal cu `chmod 600` și porniți executorul ca acel
   utilizator, de exemplu dintr-un serviciu systemd cu `Restart=on-failure`,
   `--blender /opt/blender/blender` și `--scratch /var/lib/thide-render`.
4. Pe **Windows**, Blender poate desena cu OpenGL doar într-o sesiune de desktop în care un
   utilizator este autentificat. Un executor pornit ca serviciu, de o sarcină programată care
   rulează indiferent dacă utilizatorul este autentificat sau nu, ori prin SSH randează cu Cycles
   pe placă, dar nu o previzualizare (vederea solidă) și nici cu EEVEE: Blender se oprește acolo,
   iar eroarea lucrării spune de ce. Pentru a le randa și pe acestea, porniți executorul în
   sesiunea unui utilizator autentificat (o sarcină programată „la autentificare”, care rulează
   doar când utilizatorul este autentificat) și împiedicați blocarea acelei sesiuni.

Pentru un container, scrieți o rețetă care descarcă Blender în timp ce se construiește imaginea (de
pe blender.org, verificând SHA-256) și construiți imaginea pe calculatorul care o rulează. Nu
publicați o imagine care conține Blender: Blender este GPL, iar o imagine care îl conține este o
redistribuire a Blender, cu tot ce cere aceasta celui care o publică. Nu dați containerului altă
rețea decât adresa serverului și alt dosar decât cel de lucru.

**Securitatea unui nod.** Tot ce s-a spus mai sus despre un pachet este valabil și pentru o lucrare
de la un server: este o colecție de date, scripturile lui nu rulează niciodată, este verificat
înainte să pornească Blender și se trimit înapoi doar fișierele numite de executor. În plus,
dimensiunea și SHA-256 ale pachetului sunt verificate față de cele date de server înainte ca el să
fie citit, tokenul merge doar la adresa serverului și doar prin HTTPS, iar o lucrare eșuată nu
încarcă nimic în afară de motivul eșecului. Pentru că rămâne riscul unui defect în cititoarele
Blender, rulați Blender ca un utilizator care nu poate atinge mult mai mult decât dosarul de lucru:
un utilizator separat sau un container ca mai sus.

## Cerințe și sfaturi

- **Blender 4.2 sau mai nou** — versiunile mai vechi sunt respinse cu un mesaj clar.
- **Cycles este alegerea implicită sigură.** EEVEE este mai rapid, dar necesită un context grafic,
  deci poate eșua pe o mașină Linux fără afișaj — atât panoul, cât și CLI-ul trec elegant la o
  alternativă și vă spun ce dispozitiv a fost folosit efectiv.
- **Peșterile mari** se randează bine, dar încet; folosiți **Previzualizare** pentru a verifica în
  câteva secunde mișcarea camerei și încadrarea, înainte de varianta finală.
- **Spațiul pe disc** — secvențele de cadre păstrează fiecare PNG și pot ocupa mult, la fel și
  cadrele din care se face un film cu texte; ThIDE estimează dimensiunea și refuză de la început
  dacă unitatea este aproape plină.

## Depanare

| Simptom | Ce faceți |
|---|---|
| *„Blender nu a fost găsit”* | Instalați Blender 4.2+ sau setați calea în **Setări → Unelte externe**. |
| *„…prea veche…”* | Actualizați Blender la 4.2 sau mai nou. |
| Lista derulantă a sursei este goală | Compilați mai întâi proiectul (**Compilare → Compilează**) sau bifați **Folosește un fișier extern**. |
| Randarea a eșuat | Deschideți **jurnalul lucrării** din notificare sau din panou — conține întreaga ieșire a Blender. |
| Randarea EEVEE eșuează pe un server | Schimbați motorul la **Cycles**. |
| *„…the player lights a scene with a Radiance .hdr or an OpenEXR .exr image…”*, *„…cannot travel with the plan…”* | Imaginea iluminării HDRI este alt fel de imagine, este deteriorată sau este prea mare (mesajul spune care). Folosiți un fișier `.hdr` sau `.exr`; sau puneți **Constructor de scenă** pe *Generator*, care încarcă orice imagine pe care o citește Blender. |
| Fiecare randare are numele peșterii scris sus | Îl scriu presetările integrate. Goliți câmpul **Titlu** (sau `--title ""`). |
| Titlul este un cod scurt, nu numele peșterii | Este numele pe care îl are modelul: dați celui mai din afară `survey` al proiectului un `-title` sau scrieți titlul pe care îl vreți. |
| Previzualizarea nu are teren | Un teren transparent nu apare în previzualizări; randarea îl desenează. |
| Un titlu sau săgeata nordului sunt ascunse de pereți | Scena a fost construită de generatorul de scripturi (el le desenează în scenă). Puneți **Constructor de scenă** pe *Auto* sau *Player*. |
