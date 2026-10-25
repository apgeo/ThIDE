# Vizualizatorul 3D de modele încorporat

ThIDE poate afișa un model de peșteră compilat (`.lox` / `.3d`) într-un panou andocabil **Vizualizator 3D** —
rotire / deplasare / zoom, suprapuneri de colorare și **selectarea stațiilor cu salt înapoi la sursa `.th`**.

Motorul de randare este [CaveView.js](https://github.com/aardgoose/CaveView.js) (MIT), inclus în
`ThIDE/Assets/caveview/` și găzduit în **`NativeWebView`** din Avalonia 12. Vizualizarea web folosește
motorul *nativ* al fiecărui sistem de operare (WebView2 pe Windows, WKWebView pe macOS, WebKitGTK/WPE pe Linux) —
**nu este inclus niciun Chromium**.

## Activare

Funcția este **dezactivată implicit**. Activați-o în **Preferințe → Compilare / Vizualizare**:

- *Vizualizator 3D încorporat (CaveView.js într-o vizualizare web nativă) — VIS-01*
- *Afișează automat modelul 3D (.lox / .3d) după o compilare — VIS-01*

După activare, deschideți-l din **Vizualizare → Vizualizator 3D** (sau din paleta de comenzi: *Comută Vizualizatorul 3D*). Apare
în bara de unelte din dreapta.

## Utilizare

- **Lista Model** (stânga sus) afișează fiecare model produs de thconfig-ul activ — fiecare țintă `export model`
  a cărei ieșire este un `.lox`/`.3d`, plus orice `.lox`/`.3d` detectat lângă thconfig. Alegeți unul
  pentru a schimba modelul afișat. Țintele care nu au fost încă compilate apar ca *„— necompilat” („— not built”)*.
- **Deschidere automată:** trecerea la panoul Vizualizator 3D deschide modelul implicit (prima ieșire `export model`
  care există), chiar dacă este vechi. Când fișierul nu este de azi, bara de jos afișează
  *„Generat la &lt;dată&gt;. Recompilați pentru un model nou.” („Generated &lt;date&gt;. Recompile for a fresh model.”)* Un model mai vechi decât sursele sale este marcat și
  **învechit (stale)** (recompilați pentru reîmprospătare — BUILD-03).
- **Deschide…** încarcă direct orice fișier `.lox`/`.3d`; după o compilare, cel mai nou model se încarcă automat
  (`.lox` are prioritate față de `.3d`) când afișarea automată este activă.
- **Bara de control** (la fel ca în galeriile publice CaveView): **orientare** (Plan / profil N·E·S·V),
  **cameră** (Perspectivă / Ortografică) și comutatoare pentru elemente — **Pereți** (activ implicit), **Radiale** (splays),
  **Suprafață**, **Stații**, **Etichete**, **Nume**, **Cutie** (cutia de încadrare), **Rotire automată**, **Lumină**
  (direcțională), **HUD** (afișajul suprapus al CaveView) și **Meniu** (bara laterală proprie a CaveView).
- **Colorează după:** Înălțime · Topografie · Lungime · Înclinare · Simplu (ultima alegere este reținută).
- **Ecran complet** mută întregul panou 3D într-o fereastră pe tot ecranul, fără margini — apăsați **Esc** (chiar
  și când pânza 3D are focusul) sau butonul **Ieși din ecran complet** pentru a-l readuce în andocare.
  **Resetează vederea** reîncadrează modelul; **↺ Implicite** readuce fiecare comutator la valoarea implicită; **Reîmprospătează**
  reîncarcă. **DevTools** deschide inspectorul motorului web acolo unde este disponibil (altfel folosiți F12 / clic dreapta ▸
  Inspectează; ieșirea consolei JS este reflectată și în panoul **Jurnal**).
- Schimbarea spațiului de lucru/thconfig-ului descarcă modelul curent, apoi deschide automat modelul implicit al noului proiect.
- **Faceți clic pe o stație** în vederea 3D pentru a duce editorul la locul unde este declarată acea stație în
  sursa `.th`.
- **Arată în vizualizarea 3D** — faceți clic dreapta pe o stație (sau pe *De la*/*Până la* al unei vizări) în grila **Măsurători** pentru a o
  selecta și încadra în vederea 3D; aceeași opțiune este oferită în cardul de hover al editorului și în
  meniul de clic dreapta pentru referințele la stații/survey-uri (un survey primește o cutie de încadrare).
- Rândurile din **Fișiere generate** au butoane de acțiune pe fiecare rând — deschide în vizualizatorul extern (Loch/Aven),
  **Vizualizează în vizualizatorul 3D intern**, afișează în managerul de fișiere și salt la definiție — plus o
  casetă de bifare cu 3 stări **Deschidere automată** (implicit = folosește setarea pe tip de fișier · întotdeauna · niciodată), a cărei alegere
  este reținută de la o compilare la alta.

## Cum funcționează

```
BuildViewModel artifacts ─▶ Model3DViewerViewModel ─▶ Caveview3DAssetHost (127.0.0.1 loopback)
       (.lox/.3d, stale?)          │  stages model + serves viewer.html + CaveView.js
                                   ▼
                          NativeWebView ⇄ viewer.html (CV2.CaveViewer)
   pick {station,x,y,z} ──▶ WebMessageReceived ──▶ StationSourceResolver ──▶ editor navigation
```

CaveView preia modelul prin HTTP din `surveyDirectory`, așa că o pagină `file://` ar fi
blocată de CORS. De aceea `Caveview3DAssetHost` servește resursele incluse și modelul pregătit dintr-un
`HttpListener` loopback pe un port aleatoriu, legat doar la `127.0.0.1`.

Corespondența etichetă de stație → sursă este **de tip best-effort / calitate de previzualizare**: calea completă cu puncte
a unei stații din model este comparată cu tabela semantică de simboluri (numele calificat exact, `point@survey`, apoi un
nume final simplu unic). Prefixele de cale ale survey-urilor și equate-urile sunt tratate acolo unde nu există ambiguitate; altfel
linia de stare raportează că nu s-a găsit o declarație sursă unică, în loc să navigheze.

## Cerințe și soluție de rezervă

- **Windows:** runtime-ul WebView2 vine cu Windows 11 / Edge modern — nu necesită configurare.
- **Linux:** instalați motorul WebKit, de exemplu `apt install libwebkit2gtk-4.1-0`. CaveView se afișează în
  panoul andocat pe WebKitGTK, inclusiv cu GL software. Două particularități Linux sunt tratate automat: pagina
  își trimite mesajele prin puntea `invokeCSharpAction()` a NativeWebView (WebKitGTK nu are
  `window.chrome.webview`), iar aplicația reface aranjarea după ce există gazda nativă a motorului web,
  pe care acesta o creează după propria etapă de aranjare a controlului. Înainte de 0.3.0-alpha.2, panoul putea rămâne
  gol sau blocat la *„Se încarcă …” („Loading …”)* până când ceva îl redimensiona.
- **macOS:** WKWebView este integrat.

Dacă motorul web sau resursele incluse nu sunt disponibile, panoul afișează un mesaj prietenos și un buton
**Deschide modelul extern (Loch / Aven)** în locul unui control gol.

## Actualizarea CaveView.js inclus

`Assets/caveview/CaveView/js/CaveView2.js` este pachetul UMD precompilat (v2.9.0, cu three.js inclus)
de pe site-ul publicat al proiectului, împreună cu `css/`, `images/`, `lib/` și `LICENSE`. Pentru a-l
actualiza, puneți în loc un build mai nou al `CaveView2.js` de la
<https://github.com/aardgoose/CaveView.js>. `viewer.html` este propria noastră pagină gazdă (puntea JS↔C#) și
nu face parte din CaveView.

> **Patch local (reaplicați după actualizare):** pagina de informații a pachetului se construiește cu
> `const n=i.loadedSource.getNames();`, care aruncă *„Cannot read properties of null (reading
> 'getNames')”* când **Meniu**-ul lateral este afișat și modelul este reîncărcat/reîmprospătat — deoarece noi
> încărcăm modelele prin `loadCave`, nu prin lista de peșteri proprie a CaveView, așa că managerul de surse al UI-ului nu are
> `loadedSource`. Protejăm la null acest unic punct de apel, transformându-l în
> `const n=(i.loadedSource?i.loadedSource.getNames():[]);` (pagina de informații omite atunci doar numele
> fișierului). Reaplicați această modificare de un singur token ori de câte ori `CaveView2.js` este inclus din nou.
