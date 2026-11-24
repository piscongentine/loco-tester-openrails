; Loco Tester - Inno Setup script
; Build: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\LocoTester.iss   (or run build-setup.ps1)

#define AppName "Loco Tester"
#define AppVersion "1.0"
#define AppVersionName "First Light"
#define Publisher "Piscongentine"
#define RepoUrl "https://github.com/piscongentine/loco-tester-openrails"

[Setup]
AppId={{6F3C8A9E-2B4D-4E71-9C55-LOCOTESTER01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion} "{#AppVersionName}"
AppPublisher={#Publisher}
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=© 2026 {#Publisher}
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; installs for the current user without admin rights; can also install for everyone
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=LocoTester-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\Loco Tester.exe
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE.txt
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard@2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small@2x.bmp
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nLoco Tester is a workbench for Open Rails and MSTS locomotives: an HD 3D view with working pantographs, wipers, lights and smoke, the 2D cab, a sound lab, a 0 → 100 km/h run test and a health check that repairs broken sound files.%n%nMade by Piscongentine · Made in Bharat

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\build5\Loco Tester.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\logo.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Loco Tester.exe"; Comment: "Test Open Rails / MSTS locomotives"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Loco Tester.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Loco Tester.exe"; Description: "Start Loco Tester"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Loco Tester.exe"; Parameters: "--syscheck"; Description: "Check this PC (requirements, graphics, Open Rails folder)"; Flags: nowait postinstall skipifsilent unchecked

[Code]
// .NET Framework 4.8 is built into Windows 10 (1903+) and 11; older PCs are told where to get it.
function IsDotNet48Installed(): Boolean;
var release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) and (release >= 528040);
end;

function InitializeSetup(): Boolean;
var code: Integer;
begin
  Result := True;
  if not IsDotNet48Installed() then
  begin
    if MsgBox('Loco Tester needs Microsoft .NET Framework 4.8, which is not installed on this PC.' + #13#10#13#10 +
              'Open the official Microsoft download page now? Run this setup again after installing it.',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, code);
    Result := False;
  end;
end;
