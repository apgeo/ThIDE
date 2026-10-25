# Deschiderea fișierelor cu ThIDE (UX-09)

ThIDE acceptă căi de fișiere în trei moduri:

1. **Drag-and-drop** — trageți unul sau mai multe fișiere `.th` / `.th2` / `.thconfig` peste fereastra
   principală și acestea se deschid ca file (tab-uri). (Implementat în `MainWindow` `OnDrop`.)
2. **Linie de comandă** — `ThIDE path/to/cave.thconfig path/to/survey.th` deschide acele fișiere
   după ce apare fereastra. Funcționează atât căile relative, cât și cele absolute. Căile care nu
   există și argumentele care încep cu `-` (sau cu `/` pe Windows, unde acesta marchează o opțiune)
   sunt ignorate. (Implementat în `App.ParseFileArgs` → `MainWindow.OpenStartupFileArgs`.)
3. **Asocierea de fișiere a sistemului de operare („Open with ThIDE”)** — odată făcută asocierea,
   dublu clic pe un `.th`/`.thconfig` (sau clic dreapta → *Open with* / *Deschide cu*) pornește
   aplicația cu calea fișierului ca argument, pe care modul 2 îl deschide apoi.

Tratarea în aplicație (modurile 1 și 2) este inclusă în executabil. Înregistrarea asocierii (modul 3)
ține de împachetare/installer și depinde de sistemul de operare:

## Windows

Asociați per utilizator prin registry (înlocuiți calea executabilului):

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\.thconfig]
@="ThIDE.thconfig"

[HKEY_CURRENT_USER\Software\Classes\ThIDE.thconfig]
@="Therion project configuration"

[HKEY_CURRENT_USER\Software\Classes\ThIDE.thconfig\shell\open\command]
@="\"C:\\Path\\To\\ThIDE.exe\" \"%1\""
```

Repetați pentru `.th` / `.th2`. Un installer MSI/MSIX ar trebui să scrie aceste intrări (și un `ProgID`
cu pictogramă) la instalare și să le elimine la dezinstalare.

## Linux

Livrați un fișier `.desktop` cu `MimeType=` și înregistrați un tip MIME:

```ini
[Desktop Entry]
Type=Application
Name=ThIDE
Exec=thide %F
MimeType=application/x-therion-config;application/x-therion;
```

Definiți tiparele MIME (de ex. `*.thconfig`, `*.th`, `*.th2`) într-un
`~/.local/share/mime/packages/thide.xml` și rulați `update-mime-database` +
`update-desktop-database`.

## macOS

Declarați `CFBundleDocumentTypes` (și un UTI `UTExportedTypeDeclarations` pentru `.thconfig`/`.th`) în
`Info.plist` din pachetul aplicației. macOS transmite apoi aplicației fișierele deschise; Avalonia le
expune ca argumente în linia de comandă / evenimente de deschidere a fișierelor.
