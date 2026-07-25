; Inno Setup script for Benny Box (BitMagic.BennyBox).
; Built from a self-contained `dotnet publish -r win-x64` output - CI passes the publish folder and
; version in via /DSourceDir and /DMyAppVersion so nothing here needs updating for a normal release.
; AppId is a fixed GUID (do not change) so future versions upgrade in place instead of installing
; side-by-side.

#define MyAppName "Benny Box"
#define MyAppPublisher "BitMagic"
#define MyAppURL "https://github.com/Yazwh0/bennybox"
#define MyAppExeName "Iptv.App.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\installer-output"
#endif

[Setup]
AppId={{25B0B752-7A5B-42BD-87A2-B7C62758B391}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BennyBoxSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; Per-user install (no admin/UAC prompt needed) - a better fit for `winget install` running
; unattended, and it keeps the install dir and the app's %APPDATA%\BennyBox data consistently in the
; same user's profile. {autopf}/{autodesktop}/{group} below all resolve to their per-user
; equivalents automatically once this is set.
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; No app icon or code-signing certificate yet - installer ships unsigned, using Inno's default icon.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Cleans up logs the app writes at runtime (not part of the install payload) - settings/DB under
; %APPDATA%\BennyBox are left in place so reinstalling doesn't lose the user's profiles/favorites.
Type: filesandordirs; Name: "{userappdata}\BennyBox\logs"
