# Utilizare

> [Index documentație](README.md) · [README-ul proiectului](../../README.md)

## Unelte externe

ThIDE le apelează ca **procese separate** (configurabile în **Setări → Unelte externe**); niciuna nu este inclusă sau legată în aplicație:

| Unealtă | Folosită pentru | Note |
|---|---|---|
| **Therion** (`therion`) | Compilarea proiectelor | Descoperită automat sau setați calea manual. Versiunea de gramatică țintă fixată: v6.4.0. |
| **Loch / Aven** | Vizualizatoare externe 3D / 2D | Lansate pe ieșirea compilată. |
| **Mapiah** | Editarea schițelor `.th2` | Deschide fișierul `.th2` și îl reîncarcă automat la salvare. |

Deoarece Therion (și Loch/Aven/Mapiah) sunt lansate la distanță, prin linia de comandă, ThIDE nu leagă codul lor GPL — vedeți [LICENSING.md](../../LICENSING.md).

## Unelte de linie de comandă

**CLI** — validare și raportare rapidă, fără interfața grafică:

```sh
dotnet run --project src/Therion.Cli -- validate path/to/project.thconfig
dotnet run --project src/Therion.Cli -- lint   path/to/file.th
dotnet run --project src/Therion.Cli -- format path/to/file.th --write
dotnet run --project src/Therion.Cli -- stats  path/to/project.thconfig
dotnet run --project src/Therion.Cli -- deps   path/to/project.thconfig --dot
dotnet run --project src/Therion.Cli -- gis    path/to/project.thconfig --format kml --out entrances.kml
```

**LSP** — îndreptați orice editor compatibil LSP către executabilul `therion-lsp` pentru diagnostice în timp real:

```sh
dotnet run --project src/Therion.Lsp
```

Vedeți [lsp.md](lsp.md) pentru configurarea clientului.

## Deschiderea fișierelor din linia de comandă

```sh
ThIDE path/to/cave.thconfig /home/me/caves/survey.th
```

Fiecare argument care denumește un fișier `.th` / `.th2` / `.thconfig` existent se deschide imediat ce fereastra este afișată;
funcționează atât căile relative, cât și cele absolute. Argumentele care încep cu `-` (sau `/` pe Windows) sunt tratate
ca opțiuni și ignorate. Lansările *Deschide cu (Open with)* din managerul de fișiere folosesc același mecanism — vedeți
[file-association.md](file-association.md).

## Variabile de mediu pentru automatizare

| Variabilă | Efect |
|---|---|
| `THIDE_UI_AUTOMATION=1` | Pornește serverul de automatizare UI din aplicație (MCP pe loopback, bearer token). Altfel este oprit; nicio setare nu îl activează. |
| `THIDE_UI_AUTOMATION_ENDPOINT` | Unde scrie serverul `port` / `token` / `url` (implicit `<app data>/ThIDE/ui-automation-endpoint.json`, accesibil doar proprietarului). |
| `THIDE_UI_AUTOMATION_PORT`, `THIDE_UI_AUTOMATION_TOKEN` | Fixează portul și token-ul. |
| `THIDE_UI_TESTS=1` | Permite rularea `tests/ThIDE.UiTests`; fără ea, testele sunt sărite, așa că `dotnet test ThIDE.sln` rămâne o rulare de teste unitare. |

Detalii: [dev/ui-testing.md](dev/ui-testing.md).

## Configurare și locațiile datelor

Setările utilizatorului și starea per proiect se află în folderul de date al aplicației specific platformei (`%AppData%/ThIDE` pe Windows, echivalentele XDG/`~/Library` în rest), inclusiv:

- preferințele, scurtăturile de tastatură, tema și limba;
- indexul persistent de simboluri, cache-ul de parsare și bufferele de recuperare după blocare;
- configurația regulilor semantice (`rules.json`), plugin-urile (`plugins/*.dll`) și fișierele însoțitoare per proiect (piste, metadate).

Funcțiile costisitoare din punct de vedere al performanței sau al parsării au comutatoare explicite de activare/dezactivare în **Preferințe ▸ Performanță / Extensii / Vizualizare**. Înregistrarea asocierilor de fișiere în sistemul de operare este descrisă în [file-association.md](file-association.md).
