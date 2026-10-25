# Testarea UI și automatizare

ThIDE are două metode complementare de a controla aplicația reală, în execuție, atât pentru rulări de
teste nesupravegheate, cât și pentru utilizare interactivă de către un dezvoltator sau un agent de
programare.

| | Server de automatizare în aplicație | Driver AT-SPI (`uidrive`) |
|---|---|---|
| Comunică cu | arborele de automation peer-uri Avalonia, din interiorul procesului | magistrala de accesibilitate Linux, din exterior |
| Protocol | MCP peste HTTP loopback (unelte `ui_*`) | bibliotecă Python + CLI |
| Platforme | Windows, Linux, macOS | Linux |
| Intrare | pattern-uri invoke/toggle/select/expand, evenimente de tastă și text rutate | acțiuni AT-SPI, mouse și tastatură X11 reale (xdotool) |
| Poate citi | tot ce conține arborele, plus textul editorului de cod și orice cale de proprietate (`DataContext.Status`) | nume, stări, limite, textul casetelor de text |
| Capturi de ecran | randate de aplicație (nu e nevoie de display; vizualizările web native ies goale) | captură reală a ecranului (include vizualizarea web 3D) |
| Teste | `tests/ThIDE.UiTests` (C#, xUnit) | `tests/e2e-linux` (pytest) |

Ambele folosesc același limbaj de selectori și același proiect fixture inventat (`tests/ui-fixtures/`).

## Rularea testelor

```sh
tests/run-ui-tests.sh            # ambele suite
tests/run-ui-tests.sh mcp        # doar suita C#
tests/run-ui-tests.sh atspi      # doar suita AT-SPI (Linux)
tests/run-ui-tests.sh atspi -k 3d -x   # argumentele suplimentare merg la pytest / dotnet test
```

Pe Linux, scriptul rulează totul pe un display Xvfb privat, cu propria sesiune D-Bus și un profil de
unică folosință, deci nu are nevoie de desktop și nu vă atinge niciodată setările. Rezultatele, inclusiv
o captură de ecran, arborele de elemente și jurnalul aplicației pentru fiecare test AT-SPI eșuat, ajung
în `tests/ui-artifacts/`.

`dotnet test ThIDE.sln` rămâne o rulare de teste unitare: testele UI se sar singure dacă nu este setat
`THIDE_UI_TESTS=1`.

Cerințe preliminare pe Linux (Ubuntu): `xvfb xfwm4 at-spi2-core xdotool python3-gi gir1.2-atspi-2.0
python3-pil python3-pytest libwebkit2gtk-4.1-0`. Instalați și `therion` și `survex` pentru testele de
compilare și 3D (acestea se sar fără Therion).

## Selectori

| Selector | Potrivește |
|---|---|
| `#Toolbar.OpenFile` | automation id (`AutomationProperties.AutomationId`, altfel `x:Name`) |
| `name:3D Viewer` sau `3D Viewer` | numele accesibil exact |
| `name~:^Open` / `text~:demo\.lox` | regex pe nume / pe conținutul text |
| `role:TabItem` | tipul de control de automatizare (`role:page tab` în termeni AT-SPI) |
| `type:TextEditor` | clasa controlului (doar serverul din aplicație) |
| `state:selected` | selected, focused, checked, expanded, enabled, showing (serverul din aplicație; uidrive are `--state`) |
| `a;b` | ambii termeni |
| `#Viewer3D.View >> role:Text` | un descendent |
| `...@2` | a treia potrivire |

Automation id-urile urmează forma `<Area>.<Thing>`: `Menu.File`, `Menu.Build.Build`, `Toolbar.Compile`,
`Welcome.OpenWorkspaceFolder`, `Viewer3D.ResetView`, `Diagnostics.Filter`,
`WorkspaceExplorer.Tree`, `Preferences.Ok`. Controalele interactive noi ar trebui să primească unul, iar
controalele doar cu pictogramă au nevoie și de un `AutomationProperties.Name` localizat (de obicei
cheia tooltip-ului).

Uneltele care acționează asupra unui element așteaptă până la 5 s să apară acesta, astfel încât un apel
imediat după un clic nu intră în cursă cu UI-ul.

## Controlul aplicației manual sau dintr-un agent

```sh
U="python3 tools/uidrive/uidrive.py"
$U start --automation --project tests/ui-fixtures/demo-cave/demo.thconfig
$U tree                                   # ce este pe ecran (compact; --hidden / --raw pentru mai mult)
$U click "role:page tab;name:3D Viewer"   # acțiune AT-SPI; --mouse pentru un clic real
$U type "# hello" --into "#Editor"        # tastatură reală
$U shot --of "#Viewer3D.View"             # se afișează calea PNG
$U call ui_get '{"selector": "type:TextEditor"}'           # server în aplicație: textul complet al editorului
$U call ui_menu '{"path": "Build > Build"}'
$U call ui_screenshot '{"selector": "#Toolbar.Panel"}' --png toolbar.png
$U log                                    # stdout/stderr al aplicației
$U stop
```

Sesiunea (display, magistrală, profil, aplicație) supraviețuiește încheierii CLI-ului, așa că un agent
poate emite câte o comandă pe pas și se poate reconecta după o deconectare. Folosiți `--session NAME`
pentru a rula mai multe sesiuni în paralel.

Pentru a oferi nativ uneltele `ui_*` unui agent compatibil MCP, porniți cu `--automation` (sesiunea
`default` ascultă pe portul 47800) și înregistrați serverul, de ex. copiați
`tools/uidrive/mcp.example.json` în configurația MCP locală. `headersHelper` din acesta citește bearer
token-ul sesiunii care rulează.

## Serverul din aplicație

`ThIDE/Services/UiAutomation/`. Pornește doar când procesul este lansat cu
`THIDE_UI_AUTOMATION=1`, se leagă la 127.0.0.1, cere un bearer token și își scrie endpoint-ul
(`port`, `token`, `url`) în `THIDE_UI_AUTOMATION_ENDPOINT` (implicit
`<app data>/ThIDE/ui-automation-endpoint.json`, accesibil doar proprietarului). `THIDE_UI_AUTOMATION_PORT`
și `THIDE_UI_AUTOMATION_TOKEN` fixează portul și token-ul. Este separat de serverul MCP de proiect
destinat utilizatorilor și nu este activat niciodată printr-o setare.

Unelte: `ui_windows`, `ui_tree`, `ui_find`, `ui_get`, `ui_invoke`, `ui_menu`, `ui_set_text`, `ui_key`,
`ui_type`, `ui_wait`, `ui_screenshot`.

## Limitări cunoscute

- Peer-urile MenuItem din Avalonia nu au pattern invoke/expand; serverul controlează direct `MenuItem`,
  iar `uidrive click` recurge la un clic real de mouse.
- Editorul AvaloniaEdit nu are interfață text AT-SPI: tastați în el cu `uidrive`, citiți-l cu `ui_get`.
- Tastele trimise prin serverul din aplicație sunt evenimente rutate, nu intrare de la sistemul de
  operare; folosiți `uidrive key` când chiar calea de intrare a platformei este testată.
- Pe Windows/macOS, suita C# rulează pe profilul dumneavoastră ThIDE real (doar Linux este izolat).
