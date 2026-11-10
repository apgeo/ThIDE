# English → Romanian term list

The terms the Romanian documentation (`docs/ro/`) uses for recurring English ones. It is for
translators and is not published on the documentation site.

**Precedence.** A label the reader sees on screen — a menu, a button, a panel title, a setting —
is quoted exactly as the Romanian UI shows it (`ThIDE/Resources/Strings.ro.resx`, without the `_`
access-key marker), even where it differs from this list. This list governs running prose.

**Style.** Address the reader formally in the plural (*dumneavoastră*; *deschideți*, *apăsați*).
Write ș and ț with a comma below (U+0219, U+021B), never with a cedilla. Quote with „…”. Keep code,
Therion keywords (`survey`, `centreline`, `equate`, `scrap`, `map`, `fix`, `input`…), file names,
diagnostic codes, key names (Ctrl, Shift, Enter, Esc) and settings keys in English.

| English | Romanian | Notes |
|---|---|---|
| cave | peșteră | |
| caver | speolog | |
| cave survey (the activity) | topografie speologică | |
| survey (a surveyed unit, Therion `survey`) | topografie | UI: *Topografie*; the keyword stays `survey` |
| survey file | fișier de topografie | UI: *Leagă un fișier de topografie…* |
| surveyor | topograf | |
| trip (survey trip) | ieșire | UI: *Ieșire nouă…* |
| station | stație | UI: *Stații* |
| leg | tronson | UI: *tronson* |
| shot | viză | UI: *Lipește ca date de vize…* |
| splay | radială | UI uses both *Radiale* and *Splays*; prose: *radială* |
| centreline | poligonală | the keyword stays `centreline` |
| loop / loop closure | buclă / închiderea buclei | |
| misclosure | eroare de închidere | |
| length | lungime | |
| depth | adâncime | |
| height (HUD, elevation range) | înălțime | UI: *Înălțime* |
| azimuth / compass bearing | azimut | |
| clino / inclination | înclinare | |
| magnetic declination | declinație magnetică | |
| extended elevation | profil desfășurat | |
| plan (projection) | plan | |
| entrance | intrare | UI: *Intrări* |
| fixed point / fixed station | punct fix / stație fixă | UI: *puncte fixe* |
| lead (exploration lead) | pistă (de explorare) | UI: *Piste* |
| passage end | capăt de galerie | |
| passage | galerie | |
| wall(s) | pereți | UI: *Pereți* |
| surface (terrain) | suprafață | UI: *Suprafață* |
| scrap | schiță | UI: *Schiță*; the keyword stays `scrap` |
| sketch (drawing) | schiță | |
| map (Therion map) | hartă | |
| equate (the link) | echivalare | UI: *Echivalare*; the keyword stays `equate` |
| project | proiect | |
| workspace | spațiu de lucru | UI: *Spațiu de lucru* |
| folder | dosar | UI: *Deschide dosar…* |
| file | fișier | |
| thconfig | thconfig | not translated |
| active thconfig | thconfig-ul activ | |
| compile / compilation | a compila / compilare | UI: *Compilează* |
| build (a target, an export) | a compila / compilare | UI: *Compilează ținta* |
| export (noun / verb) | export / a exporta | |
| import | import / a importa | |
| output (files of a build) | rezultat / fișiere de ieșire | UI panel: *Ieșire* |
| diagnostic | diagnostic (pl. diagnostice) | UI: *Diagnostice* |
| error / warning / hint | eroare / avertisment / indiciu | |
| quick fix | remediere rapidă | UI: *Remediere rapidă* |
| fix all | corectează toate | UI: *Corectează toate „…” din spațiul de lucru…* |
| preview | previzualizare | |
| undo / redo | anulare / refacere | |
| settings | setări | UI: *Setări* |
| keyboard shortcut | scurtătură de tastatură | UI: *Scurtături tastatură* |
| command palette | paletă de comenzi | UI: *Paletă de comenzi* |
| panel / tab / toolbar | panou / filă / bară de instrumente | |
| status bar | bară de stare | |
| notification (toast) | notificare | |
| outline | contur | UI: *Contur* |
| object browser | navigator de obiecte | UI: *Navigator obiecte* |
| Mainline Preview | previzualizare principală | UI: *Previzualizare principală* |
| 3D viewer / map viewer | vizualizator 3D / vizualizator de hărți | |
| snippet (code template) | fragment de cod | UI: *Fragment* |
| thconfig fragment | fragment de thconfig | UI: *Salvează ca fragment…* |
| template | șablon | UI: *Nou din șablon…* |
| macro / task / hook | macro (pl. macrouri) / sarcină / hook | UI: *Macrouri*, *Sarcini*; *hook* stays |
| workspace trust | încredere în spațiul de lucru | |
| incoming folder | dosarul de intrare | UI: *Urmărește dosarul de intrare* |
| assistant (AI) | asistent | |
| strike / dip | direcție / înclinare (strat) | structural geology |
| fault / bedding plane / joint | falie / plan de stratificație / diaclază | |

## Also settled during the first translation

| English | Romanian | Notes |
|---|---|---|
| plugin | plugin (pl. pluginuri) | as the UI writes it |
| render / rendering | randare / a randa | Blender |
| preset | presetare | |
| wrapper survey / wrapper `.th` | topografie de legătură / fișier `.th` de legătură | Scaffold Project |
| loop adjustment / adjusted | compensarea buclelor / compensat | |
| vertical range | denivelare | UI: `Dash_VerticalRange` |
| stale (output) | învechit | UI: `Gen_State_Stale` |
| hover card | card informativ | |
| tooltip | indicație (tooltip) | |
| toast | notificare flotantă | |
| sidecar (file) | fișier însoțitor | |
| stereonet | stereogramă | UI |
| dead end | capăt închis | |
| blunder / outlier | greșeală grosolană / valoare aberantă | |
| foresight / backsight | viză directă / viză inversă | |
| headless | fără interfață grafică (headless) | |
| gesture (keyboard) | combinație (de taste) | UI: *Combinație* |
| breadcrumb | cale de navigare | |
| trust a folder | a acorda încredere dosarului | |

## UI strings that clash with this list

Quoted as the UI shows them in the documentation; worth fixing in `Strings.ro.resx`:

- `Pref_AutoRecalcLeads` says *registrul de indicii* — the Leads register is *piste*.
- `Proj_Lead_ShowAll` *Arată toate pistele deschise* — the English means passage ends.
- *Vizări* (Shots tab), *Radieri* (`Struct_SplayShots`, `Menu_Tools_ShotTable`) — prose uses *vize* / *radiale*.
- *Adaugă scrap-urile nereferite în hărți…* — prose uses *schițe*.
- *Arată-le în Object Browser* — the panel is *Navigator obiecte*.
- *Activează cârlige de script* — prose keeps *hook*.
- View menu *Aspect* vs toolbar *Aranjament* for Layout.
- Hard-coded English in the Romanian UI: the 3D viewer's “— not built”, the built-in macro titles
  *Release check* and *End of trip*.
