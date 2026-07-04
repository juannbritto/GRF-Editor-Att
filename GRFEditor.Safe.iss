; -- GRF Editor Safe: isolated side-by-side installer --

#define SafeVersion "1.7.0.0"

[Setup]
AppId={{D5229D36-6D89-4E2B-B9F9-5D670E366A13}
AppName=GRF Editor Safe
AppVersion={#SafeVersion}
DefaultDirName={autopf32}\GRF Editor Safe
DefaultGroupName=GRF Editor Safe
UninstallDisplayIcon={app}\GRF Editor Safe.exe
Compression=lzma2
SolidCompression=yes
OutputDir=installer-output
OutputBaseFilename=GRF Editor Safe Installer
WizardImageFile=setupBackground.bmp
DisableProgramGroupPage=yes
ChangesAssociations=no
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=lowest

[Files]
Source: "GRFEditor\bin\Release\GRF Editor Safe.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "GRFEditor\bin\Release\GRF Editor Safe.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "GRFEditor\Resources\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\GRF Editor Safe"; Filename: "{app}\GRF Editor Safe.exe"
Name: "{autodesktop}\GRF Editor Safe"; Filename: "{app}\GRF Editor Safe.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[UninstallDelete]
Type: files; Name: "{app}\crash.log"
Type: files; Name: "{app}\debug.log"
Type: filesandordirs; Name: "{app}\tmp"
Type: files; Name: "{userappdata}\GRF Editor Safe\crash.log"
Type: files; Name: "{userappdata}\GRF Editor Safe\debug.log"
Type: filesandordirs; Name: "{userappdata}\GRF Editor Safe\~tmp"

[Code]
function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := Release >= 528040;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  if not IsDotNet48Installed then
  begin
    MsgBox('.NET Framework 4.8 is required. Setup will open the official download page.', mbInformation, MB_OK);
    ShellExec('', 'https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end
  else
    Result := True;
end;
