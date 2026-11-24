; Build from the project root with: .\scripts\build-installer.ps1
#ifndef AppVersion
  #define AppVersion "1.0.7"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\publish\win-x64"
#endif
#ifndef InstallerDir
  #define InstallerDir SourcePath + "..\artifacts\installer"
#endif

[Setup]
AppId={{67AC143F-1CA9-4C38-BBE1-7C16F8065990}
AppName=SlotCast
AppVersion={#AppVersion}
AppPublisher=SlotCast
DefaultDirName={localappdata}\Programs\SlotCast
DefaultGroupName=SlotCast
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerDir}
OutputBaseFilename=SlotCast-Setup-{#AppVersion}-win-x64
SetupIconFile=..\logo.ico
UninstallDisplayIcon={app}\SlotCast.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\SlotCast.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
; Local credentials and the SQLite library are intentionally outside the payload.
; Existing %LOCALAPPDATA%\SlotCast data is retained on upgrade and uninstall.

[Icons]
Name: "{group}\SlotCast"; Filename: "{app}\SlotCast.exe"; WorkingDir: "{app}"; IconFilename: "{app}\SlotCast.exe"
Name: "{autodesktop}\SlotCast"; Filename: "{app}\SlotCast.exe"; WorkingDir: "{app}"; IconFilename: "{app}\SlotCast.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SlotCast.exe"; Description: "Launch SlotCast"; Flags: nowait postinstall skipifsilent

; Restart only after an update initiated from inside SlotCast.
Filename: "{app}\SlotCast.exe"; Flags: nowait; Check: IsSlotCastUpdate

[Code]
function IsSlotCastUpdate: Boolean;
begin
  Result := ExpandConstant('{param:SLOTCASTUPDATE|0}') = '1';
end;