; Loco Tester - Inno Setup script
; Build: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\LocoTester.iss   (or run build-setup.ps1)

#define AppName "Loco Tester"
#define AppVersion "1.1.1"
#define AppVersionName "Power Up"
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
VersionInfoVersion={#AppVersion}.0
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
; always let people choose (Browse…) where to install
DisableDirPage=no
UsePreviousAppDir=yes
ExtraDiskSpaceRequired=1048576

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
// ---------------------------------------------------------------- requirements
// Minimum requirements block the install; recommended ones only warn.
const
  MinRamMB = 2048;  RecRamMB = 4096;
  MinW = 1024; MinH = 700;  RecW = 1280; RecH = 720;

type
  TMemoryStatusEx = record
    dwLength, dwMemoryLoad: DWORD;
    ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual: Int64;
  end;

function GlobalMemoryStatusEx(var m: TMemoryStatusEx): BOOL; external 'GlobalMemoryStatusEx@kernel32.dll stdcall';
function GetSystemMetrics(index: Integer): Integer; external 'GetSystemMetrics@user32.dll stdcall';
function waveOutGetNumDevs(): Cardinal; external 'waveOutGetNumDevs@winmm.dll stdcall';

var
  ReqPage: TOutputMsgMemoWizardPage;
  ReqBlocked: Boolean;
  ReqProblems: String;

function IsDotNet48Installed(): Boolean;
var release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) and (release >= 528040);
end;

function RamMB(): Integer;
var m: TMemoryStatusEx;
begin
  m.dwLength := 64;
  if GlobalMemoryStatusEx(m) then Result := m.ullTotalPhys div (1024 * 1024) else Result := 0;
end;

function GraphicsName(): String;
var keys: TArrayOfString; i: Integer; s, base: String;
begin
  Result := '';
  base := 'SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}';
  if RegGetSubkeyNames(HKLM, base, keys) then
    for i := 0 to GetArrayLength(keys) - 1 do
      if (Length(keys[i]) = 4) and RegQueryStringValue(HKLM, base + '\' + keys[i], 'DriverDesc', s) then
        if (Pos('Basic Display', s) = 0) and (Pos('Remote', s) = 0) then
        begin
          if Result <> '' then Result := Result + ' + ';
          Result := Result + s;
        end;
end;

procedure AddLine(var text: String; ok: Integer; name, detail: String);
var mark: String;
begin
  // ok: 0 = met, 1 = below recommended (warning), 2 = below minimum (blocks)
  if ok = 0 then mark := # else if ok = 1 then mark := # else mark := #;
  text := text + mark + '  ' + name + ':  ' + detail + #13#10;
end;

function BuildReport(): String;
var v: TWindowsVersion; ram, w, h, lvl: Integer; gpu, txt: String;
begin
  ReqBlocked := False; ReqProblems := '';
  GetWindowsVersionEx(v);
  ram := RamMB(); w := GetSystemMetrics(0); h := GetSystemMetrics(1); gpu := GraphicsName();
  txt := 'MINIMUM  (needed to install)' + #13#10;
  if v.Major >= 10 then lvl := 0 else begin lvl := 2; ReqBlocked := True; ReqProblems := ReqProblems + '- Windows 10 or 11' + #13#10; end;
  AddLine(txt, lvl, 'Windows', Format('%d.%d build %d (needs Windows 10 or 11)', [v.Major, v.Minor, v.Build]));
  if IsDotNet48Installed() then lvl := 0 else begin lvl := 2; ReqBlocked := True; ReqProblems := ReqProblems + '- .NET Framework 4.8' + #13#10; end;
  AddLine(txt, lvl, '.NET Framework', 'needs 4.8 (built into Windows 10 and 11)');
  if ram >= MinRamMB then lvl := 0 else begin lvl := 2; ReqBlocked := True; ReqProblems := ReqProblems + '- at least 2 GB of memory' + #13#10; end;
  AddLine(txt, lvl, 'Memory', Format('%d MB (needs 2 GB)', [ram]));
  if (w >= MinW) and (h >= MinH) then lvl := 0 else begin lvl := 2; ReqBlocked := True; ReqProblems := ReqProblems + '- a screen of at least 1024 x 700' + #13#10; end;
  AddLine(txt, lvl, 'Screen', Format('%d x %d (needs 1024 x 700)', [w, h]));
  txt := txt + #13#10 + 'RECOMMENDED  (for the best HD experience)' + #13#10;
  if ram >= RecRamMB then lvl := 0 else lvl := 1;
  AddLine(txt, lvl, 'Memory', '4 GB or more for full-resolution textures');
  if (w >= RecW) and (h >= RecH) then lvl := 0 else lvl := 1;
  AddLine(txt, lvl, 'Screen', '1280 x 720 or larger');
  if gpu <> '' then AddLine(txt, 0, 'Graphics', gpu + ' (DirectX hardware 3D; keep the driver up to date)')
  else AddLine(txt, 1, 'Graphics', 'no graphics driver found: install your card''s driver for smooth HD 3D');
  if waveOutGetNumDevs() > 0 then AddLine(txt, 0, 'Sound', 'output device found')
  else AddLine(txt, 1, 'Sound', 'no output device: everything except sound still works');
  if IsWin64 then AddLine(txt, 0, 'Windows', '64-bit') else AddLine(txt, 1, 'Windows', '32-bit works, 64-bit recommended');
  txt := txt + #13#10 + 'Optional: an Open Rails or MSTS folder (TRAINS\TRAINSET), found when Loco Tester first starts, and DB Tracks for the real track in the 3D view.';
  if ReqBlocked then txt := # + '  This PC does not meet the minimum requirements. Setup cannot continue.' + #13#10#13#10 + txt
  else txt := # + '  This PC meets the minimum requirements.' + #13#10#13#10 + txt;
  Result := txt;
end;

procedure InitializeWizard();
begin
  ReqPage := CreateOutputMsgMemoPage(wpLicense, 'System requirements',
    'Checking this PC against what Loco Tester needs.',
    'Minimum requirements must be met to install. Recommended ones only affect quality and speed.',
    BuildReport());
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (ReqPage <> nil) and (CurPageID = ReqPage.ID) and ReqBlocked then
  begin
    MsgBox('Loco Tester cannot be installed because this PC is missing:' + #13#10#13#10 + ReqProblems, mbCriticalError, MB_OK);
    Result := False;
  end;
end;

// .NET Framework 4.8 is built into Windows 10 (1903+) and 11; older PCs are told where to get it.
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