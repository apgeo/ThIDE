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
   - **Motor** — *Cycles* (implicit, funcționează peste tot) sau *EEVEE* (mai rapid, dar necesită
     un afișaj; pe un server Linux fără afișaj folosiți Cycles). **Dispozitiv** alege backend-ul
     GPU pentru Cycles (Auto le încearcă pe toate și revine la CPU).
   - **Animație** — cadre pe secundă și durată; panoul afișează **numărul de cadre** rezultat.
   - **Ieșire** — un videoclip (MP4/MKV/WebM), o secvență PNG numerotată sau o singură imagine;
     plus rezoluția și numele fișierului.
   - **Etichete** — activați etichetele stațiilor și marcajele pistelor. *Mărimea marcajelor*
     alege cât de mare e un marcaj: *Auto* îl dimensionează după galerie într-un zbor prin peșteră
     și după toată peștera altfel; *Cave*, *Passage*, *CameraDistance* (cam aceeași mărime în
     imagine la orice distanță, cu tot cu textul lui) și *HiddenInFlythrough* fixează un singur
     fel. *Etichete deasupra*
     alege tavanul deasupra căruia se ridică eticheta unei stații: întâi înălțimea măsurată, întâi
     pereții, doar pereții sau deloc (pe stație).
   - **Texte peste imagine** — **Titlul**, **Linia de credite**, **Cadrul de final** și
     **Subtitrările**, fiecare în timpul ei: vedeți [Textele din panou](#textele-din-panou).
   - **Constructor de scenă (avansat)** — ce construiește scena în Blender; lăsați-l pe *Auto*.
     Vedeți [Doi constructori de scenă](#doi-constructori-de-scenă).
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

### Textele din panou

La **Texte peste imagine**, panoul are textele pe care cel mai probabil le scrieți chiar
dumneavoastră:

- **Titlu** — titlul. Presetarea stabilește dacă rămâne în partea de sus a imaginii sau este un
  cadru cu care începe filmul; ce scrieți aici este textul. Gol, nu există titlu.
- **Linie de credite** — o linie mică ce rămâne în colțul din dreapta jos (cine a topografiat,
  cine a desenat, o licență).
- **Cadru de final** — un text pe o imagine neagră cu care se încheie filmul, timp de secundele
  de alături.
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

**Texte în presetări.** O presetare păstrează textele împreună cu toate celelalte. **Salvează**
stochează titlul, linia de credite, cadrul de final și subtitrările așa cum sunt în panou, iar
alegerea presetării le aduce înapoi mai târziu, subtitrările ca rânduri pe care le puteți
modifica. Folosiți `{cave}` într-o presetare pe care vreți să o folosiți pentru mai multe peșteri.

Celelalte texte (scara grafică, săgeata nordului, legenda adâncimii, un subtitlu, linia de colț,
trecerile) se setează într-o specificație de randare (JSON-ul pe care îl ține o presetare, la
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

Presetarea **Map reveal** dezvăluie după distanță. Orice altă presetare sau specificație primește
o dezvăluire din linia de comandă (`--reveal date`, vezi [mai jos](#din-linia-de-comandă)), prin
uneltele MCP (`reveal`) sau chiar în specificația de randare:

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
  **Map reveal** îl are pornit: treceți presetarea pe `date` și ea numără anii.
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
| `marker` | un marcaj: o bilă luminoasă |
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
| `kind` | `label`, `callout`, `marker` sau `photo` |
| `station` *sau* `at` | unde: numele unei stații, așa cum îl are modelul, sau un punct `[est, nord, sus]` în coordonatele modelului |
| `text` | ce spune o etichetă sau o notă |
| `image` | fișierul unei fotografii, `.png` sau `.jpg`, numit față de locul în care se află fișierul de adnotări |
| `size` | în metri: înălțimea literelor unui text, raza unui marcaj, lățimea unei fotografii. Dacă lipsește, se potrivește cu peștera: un text cât o etichetă de stație, o fotografie cât o zecime din întinderea peșterii |
| `offset` | `[est, nord, sus]` în metri: unde stă textul unei note sau o fotografie față de stație. O notă fără el stă la patru înălțimi de literă deasupra; o fotografie fără el este pe stația ei, iar cu el are o linie până la ea |
| `color` | `[roșu, verde, albastru]`, fiecare între 0 și 1, pentru un text sau un marcaj |
| `framed` | `true` pune un chenar în jurul unei fotografii |
| `facing`, `normal` | `"facing": "fixed"` face dintr-o fotografie un panou care privește de-a lungul lui `normal` (`[est, nord, sus]`) și rămâne așa; fără el, fotografia stă cu fața spre cameră |
| `from`, `to` | când este prezentă, în secunde de la începutul filmului; fără ele, tot timpul |
| `within` | este prezentă doar cât timp camera se află la cel mult atâția metri de stația ei: o fotografie care apare când un zbor prin peșteră ajunge la locul ei |
| `fade` | în cât timp apare și dispare, în secunde (0,5 dacă nu spuneți altfel; 0 este o tăietură) |
| `id`, `role` | numele ei în scenă și numele unui grup, pentru `changes` dintr-un plan de scenă |

O fotografie care stă cu fața spre cameră rămâne paralelă cu imaginea și dreaptă în ea, așa că se
vede mereu ca dreptunghiul care este. Își arată imaginea așa cum ar arăta-o un ecran, oricare ar fi
lumina din peșteră, și nu luminează nimic. Un text și un marcaj strălucesc, ca etichetele.

**Fotografii.** O fotografie este un PNG sau un JPEG de cel mult 32 MiB, 16 384 de pixeli pe o
latură și 64 de milioane de pixeli; orice altceva este respins înainte ca Blender să pornească. O
imagine lată de 2 000 de pixeli este mai mult decât arată un film: fișierele mai mici dau pachete
mai mici și randări mai rapide. Fișierele rămân unde sunt; ThIDE le copiază pentru randare și
într-un pachet (în dosarul lui `photos`).

**De unde vin.** În linia de comandă, `--annotations note.json` (o dată pentru fiecare fișier);
prin uneltele MCP, `annotations` numește un astfel de fișier din spațiul de lucru; într-o
specificație de randare, aceeași listă stă la `annotations`. Panoul nu are încă un câmp pentru ele.

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

Este o setare a specificației de randare, la `lighting`, și a liniei de comandă:

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
- Panoul nu are încă un câmp pentru ea: salvați o specificație care o are ca fișier de presetare
  sau folosiți linia de comandă.

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

O previzualizare este mereu a playerului, orice s-ar alege aici. **Exportă scriptul…** și
**Deschide în Blender…** folosesc generatorul de scripturi.

## Alte moduri de rulare

- **Exportă scriptul…** — în loc să randeze, ThIDE scrie scriptul `render.py` împreună cu
  `model.ply` și `scene-meta.json` într-un dosar ales de dumneavoastră, ca să îl puteți rula singur
  mai târziu cu `blender -b --python render.py` sau să îl arhivați/distribuiți. Două opțiuni de la
  **Ieșire** schimbă ce îi trebuie scriptului lângă el:
  - **Script autonom** pune datele scenei (etichete, legendă) în script, așa că acesta nu mai
    citește `scene-meta.json`. Rețeaua de poligoane a modelului, `model.ply`, o citește în
    continuare de lângă el.
  - **Include rețelele de poligoane în script**, care se poate bifa doar cât timp scriptul este
    autonom, pune în script și rețeaua de poligoane (și terenul cu imaginea lui, când scena îl
    desenează): un singur fișier `.py` care se randează oriunde, dar mult mai mare. Este oprită
    dacă nu o bifați.
- **Deschide în Blender…** — deschide scena în fereastra *interactivă* a Blender, ca să o
  inspectați sau să o ajustați manual înainte de randare.
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
                    [--hdri <image.hdr|.exr> [--hdri-rotation <degrees>] [--hdri-strength <n>]]
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
