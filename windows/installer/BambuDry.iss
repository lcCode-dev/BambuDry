; Inno Setup script for BambuDry.
; Builds the BambuDry-<version>-Setup.exe installer that the GitHub
; Actions workflow signs and attaches to releases.
;
; Compile locally:
;   1. Install Inno Setup 6+ (https://jrsoftware.org/isinfo.php)
;   2. dotnet publish ../src/BambuDry.App -c Release -r win-x64 ^
;        --self-contained true -p:PublishSingleFile=false ^
;        -o publish
;   3. iscc BambuDry.iss
;
; The CI workflow does these steps automatically. Self-contained=true
; bundles the .NET runtime so users don't need to install .NET 8 themselves.

#define MyAppName       "BambuDry"
#define MyAppPublisher  "lcCode"
#define MyAppURL        "https://github.com/lcCode-dev/BambuDry"
#define MyAppExeName    "BambuDry.exe"
; The CI workflow injects a real version via /DMyAppVersion=...
#ifndef MyAppVersion
  #define MyAppVersion  "0.1.0"
#endif
; CI also injects /DPublishDir= pointing to the dotnet publish output;
; default works for local compiles relative to this .iss file.
#ifndef PublishDir
  #define PublishDir    "..\src\BambuDry.App\bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
; AppId is a stable GUID — never change it across releases or upgrades break.
AppId={{2F4E3B7C-9F5A-4D1C-8B7E-3D9A0F1C8B2A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=BambuDry-{#MyAppVersion}-Setup
SetupIconFile=..\src\BambuDry.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Default to per-user install (no admin); user can opt up to per-machine.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Launch BambuDry when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort: if the user enabled "Launch at login" via the in-app Advanced
; tab, that writes HKCU\...\Run\BambuDry. Inno doesn't manage that key, so
; clean it up on uninstall to avoid orphan entries.
Filename: "reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v BambuDry /f"; Flags: runhidden; RunOnceId: "DelLaunchAtLogin"
