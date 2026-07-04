; TherionProc Windows installer (Inno Setup 6).
;
; Wraps the self-contained, single-file publish (dotnet publish -r win-x64 --self-contained
; -p:PublishSingleFile=true) into a signed-capable setup.exe with Start Menu / optional desktop
; shortcuts, an "Add/Remove Programs" uninstaller, and opt-in file-type associations that mirror
; TherionProc.Services.FileAssociationCatalog (kept in sync by InstallerAssociationConsistencyTests).
;
; Build:
;   ISCC.exe /DMyAppVersion=1.2.3 /DSourceDir="C:\path\to\publish\win-x64" TherionProc.iss
; Defaults (below) let you also just open it in the Inno Setup IDE after a local publish.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

; Folder holding the published win-x64 output. Relative paths resolve from this .iss file.
#ifndef SourceDir
  #define SourceDir "..\..\publish\win-x64"
#endif

#define MyAppName "TherionProc"
#define MyAppPublisher "TherionProc"
#define MyAppURL "https://github.com/apgeo/TherionProc"
#define MyAppExeName "TherionProc.exe"
#define MyIcon "..\..\TherionProc\Assets\avalonia-logo.ico"

[Setup]
; A stable AppId ties upgrades + the uninstaller together across versions — never change it.
AppId={{B1E7C4A2-9D3F-4E8B-A6C1-2F5D8E9A0B34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
LicenseFile=..\..\LICENSE
OutputBaseFilename=TherionProc-Setup-{#MyAppVersion}
SetupIconFile={#MyIcon}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Self-contained win-x64 build is 64-bit only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installing into Program Files needs elevation; that also lets the associations land in HKLM.
PrivilegesRequired=admin
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "Associate Therion file types (.th, .th2, .thconfig, .thc, .thl, .xvi) with TherionProc"; GroupDescription: "File associations:"

[Files]
; The whole published tree (single-file exe + any native side-by-side libs, WebView2 loader, etc.).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; A loose copy of the icon so association DefaultIcon entries have a stable target.
Source: "{#MyIcon}"; DestDir: "{app}"; DestName: "TherionProc.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; --- .th : Therion survey source ---
Root: HKA; Subkey: "Software\Classes\TherionProc.th"; ValueType: string; ValueName: ""; ValueData: "Therion survey source"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.th\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.th\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.th"; ValueType: string; ValueName: ""; ValueData: "TherionProc.th"; Flags: uninsdeletevalue; Tasks: associate

; --- .th2 : Therion 2D map / scrap ---
Root: HKA; Subkey: "Software\Classes\TherionProc.th2"; ValueType: string; ValueName: ""; ValueData: "Therion 2D map / scrap"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.th2\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.th2\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.th2"; ValueType: string; ValueName: ""; ValueData: "TherionProc.th2"; Flags: uninsdeletevalue; Tasks: associate

; --- .thconfig : Therion configuration ---
Root: HKA; Subkey: "Software\Classes\TherionProc.thconfig"; ValueType: string; ValueName: ""; ValueData: "Therion configuration"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thconfig\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thconfig\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.thconfig"; ValueType: string; ValueName: ""; ValueData: "TherionProc.thconfig"; Flags: uninsdeletevalue; Tasks: associate

; --- .thc : Therion configuration ---
Root: HKA; Subkey: "Software\Classes\TherionProc.thc"; ValueType: string; ValueName: ""; ValueData: "Therion configuration"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.thc"; ValueType: string; ValueName: ""; ValueData: "TherionProc.thc"; Flags: uninsdeletevalue; Tasks: associate

; --- .thl : Therion library ---
Root: HKA; Subkey: "Software\Classes\TherionProc.thl"; ValueType: string; ValueName: ""; ValueData: "Therion library"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thl\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.thl\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.thl"; ValueType: string; ValueName: ""; ValueData: "TherionProc.thl"; Flags: uninsdeletevalue; Tasks: associate

; --- .xvi : Therion XVI scan ---
Root: HKA; Subkey: "Software\Classes\TherionProc.xvi"; ValueType: string; ValueName: ""; ValueData: "Therion XVI scan"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.xvi\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\TherionProc.xvi\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.xvi"; ValueType: string; ValueName: ""; ValueData: "TherionProc.xvi"; Flags: uninsdeletevalue; Tasks: associate

[Code]
// Ask Explorer to refresh its association/icon cache once install finishes, so newly associated
// files pick up the icon without a reboot.
const SHCNE_ASSOCCHANGED = $08000000;
const SHCNF_IDLIST = $0000;

procedure SHChangeNotify(wEventId, uFlags: Integer; dwItem1, dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('associate') then
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;
