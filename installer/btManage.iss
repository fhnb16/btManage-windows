; Installer of btManage for Windows, built with Inno Setup 6.3 or later.
;
; Installs for the current user, without administrator rights, into
; %LOCALAPPDATA%\Programs\btManage. That folder stays writable, so the application can
; update itself in place.
;
;   iscc /DAppVersion=0.2.0 /DArch=x64 /DSourceDir=..\out\win-x64 /DOutputDir=..\out installer\btManage.iss
;
; SourceDir is the published application folder (dotnet publish).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\win-" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\out"
#endif

[Setup]
AppId={{8C2B7A54-3E0C-4B0E-9F5D-6B6E2F6A51C7}
AppName=btManage
AppVersion={#AppVersion}
AppPublisher=btManage
DefaultDirName={localappdata}\Programs\btManage
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=btManage-windows-{#Arch}-setup
SetupIconFile=..\src\BtManage\Assets\app.ico
UninstallDisplayIcon={app}\BtManage.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#Arch == "arm64" ? "arm64" : "x64compatible"}
ArchitecturesInstallIn64BitMode={#Arch == "arm64" ? "arm64" : "x64compatible"}
CloseApplications=yes
MinVersion=10.0.17763

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
; "Automatically start btManage", in the language of the installer.
Name: "autostart"; Description: "{cm:AutoStartProgram,btManage}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\btManage"; Filename: "{app}\BtManage.exe"

[Registry]
; The same value the application writes for "Start with Windows".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "btManage"; ValueData: """{app}\BtManage.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\BtManage.exe"; Description: "{cm:LaunchProgram,btManage}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The application lives in the tray; stop it so its files can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM BtManage.exe /F"; Flags: runhidden; RunOnceId: "StopBtManage"
