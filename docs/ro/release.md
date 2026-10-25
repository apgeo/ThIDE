# Publicarea versiunilor ThIDE

## Canalul de release

Push-ul unui tag `v*` (de ex. `v0.3.0`) declanșează [`.github/workflows/release.yml`](../../.github/workflows/release.yml),
care produce publish-uri **self-contained, într-un singur fișier** pentru:

- `win-x64`
- `linux-x64`
- `osx-arm64`

Fiecare este arhivat (`.zip` pe Windows, `.tar.gz` pe Unix), încărcat ca artifact al workflow-ului și —
pentru build-urile din tag-uri — atașat la GitHub Release-ul corespunzător.

```bash
git tag v0.3.0
git push origin v0.3.0
```

`workflow_dispatch` vă permite să produceți artifactele fără a crea un release.

## Programe de instalare

Pe lângă arhivele portabile, același workflow construiește un **program de instalare nativ** pentru
Windows și Linux (uneltele și scripturile de build locale se află în [`/build`](../../build/README.md)):

| Platformă | Artifact | Construit cu |
|----------|----------|------------|
| Windows  | `ThIDE-Setup-<ver>.exe` | Inno Setup (`build/windows/ThIDE.iss`) |
| Linux    | `thide_<ver>_amd64.deb` | `dpkg-deb` (`build/linux/build-deb.sh`) |
| Linux    | `ThIDE-<ver>-x86_64.AppImage` | `appimagetool` (`build/linux/build-appimage.sh`) |
| macOS    | `ThIDE-osx-arm64.tar.gz` (portabil — încă fără program de instalare) | — |

Programele de instalare împachetează publish-ul self-contained (nu este nevoie de .NET instalat în
sistem), creează o scurtătură în meniu și înregistrează tipurile de fișiere Therion
(`.th .th2 .thconfig .thc .thl .xvi`). Pachetul `.deb` și setup-ul Windows se dezinstalează curat
(Add/Remove Programs; `apt remove thide`); **AppImage** este un executabil unic, portabil, fără
instalare, pentru utilizatorii care nu doresc un pachet de sistem. Tipurile de fișiere înregistrate
sunt ținute în sincron cu `FileAssociationCatalog` al aplicației de către
`InstallerAssociationConsistencyTests`. Versiunea este derivată din tag-ul `v*`. Pentru a construi
manual unul dintre ele, consultați [build/README.md](../../build/README.md).

## Semnarea codului și notarizarea (manual / condiționat de secrete)

Semnarea necesită certificate care trebuie păstrate în secretele CI, așa că intenționat **nu** este
inclusă în workflow-ul de mai sus. Adăugați-o pe fiecare platformă când certificatele sunt disponibile:

- **Windows** — semnați Authenticode atât `.exe`-ul publicat, **cât și** `ThIDE-Setup-*.exe` generat,
  cu `signtool` și un certificat de code-signing (`AzureSignTool` funcționează bine cu un certificat
  din Key Vault). Semnați exe-ul aplicației înainte de a rula Inno Setup, apoi semnați programul de
  instalare rezultat.
- **macOS** — semnați cu un certificat Developer ID Application (`codesign --deep --options runtime`),
  apoi notarizați cu `xcrun notarytool` și `staple`. Împachetați ca `.dmg`.
- **Linux** — împachetați ca `AppImage` (sau `.deb`/`.rpm`); opțional, semnați artifactul cu GPG.

Stocați certificatul/parola ca secrete ale repository-ului și adăugați un pas de semnare după
*Publish* și înainte de *Archive*.

## Actualizare automată

Aplicația este distribuită ca build-uri self-contained, așa că un mecanism de actualizare trebuie doar
să compare versiunile și să descarce arhiva mai nouă:

1. La pornire (opțional, la alegerea utilizatorului), interogați API-ul GitHub Releases
   (`/releases/latest`) și comparați tag-ul acestuia cu versiunea assembly-ului care rulează.
2. Dacă este mai nouă, afișați o notificare (centrul de toast/clopoțel UX-07 este locul firesc)
   cu link către release, sau descărcați și înlocuiți la următoarea repornire.

Pentru un updater gata făcut, [Velopack](https://github.com/velopack/velopack) (succesorul lui
Squirrel) se integrează cu acest model de publish într-un singur fișier și gestionează actualizările
delta și secvența de repornire; poate fi adăugat mai târziu fără a schimba artifactele workflow-ului
de release.
