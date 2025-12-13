; Inno Setup Script for WowQuestTtsTool
; Erstellt mit Inno Setup 6.x

#define MyAppName "WowQuestTtsTool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Sam"
#define MyAppExeName "WowQuestTtsTool.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output-Verzeichnis und Dateiname
OutputDir=.\Installer
OutputBaseFilename=WowQuestTtsTool_Setup_{#MyAppVersion}
; Kompression
Compression=lzma2/ultra64
SolidCompression=yes
; 64-bit
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; UI
WizardStyle=modern
; Berechtigungen
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Hauptanwendung (Self-Contained Single-File)
Source: "Release\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Config und Data Ordner
Source: "Release\config\*"; DestDir: "{app}\config"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Release\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
