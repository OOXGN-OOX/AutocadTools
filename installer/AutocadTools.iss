#ifndef BundleDir
  #error BundleDir must point to a complete AutocadTools.bundle
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif
#define ProjectDir ".."
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{D3A3F30D-A5F7-4DF9-A6D4-CC17D24B83DB}
AppName=Autocad Tools
AppVersion={#AppVersion}
AppPublisher=OOXGN
AppPublisherURL=https://github.com/OOXGN-OOX/AutocadTools
AppSupportURL=https://github.com/OOXGN-OOX/AutocadTools/issues
AppUpdatesURL=https://github.com/OOXGN-OOX/AutocadTools/releases
DefaultDirName={code:GetInstallDirectory}
DisableDirPage=yes
DisableWelcomePage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=no
UninstallDisplayName=Autocad Tools (AutoCAD 2025–2027)
UninstallDisplayIcon={uninstallexe}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile={#ProjectDir}\LICENSE
InfoBeforeFile={#ProjectDir}\installer\BeforeInstall.txt
OutputDir={#ReleaseDir}
OutputBaseFilename=AutocadTools-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
SetupLogging=yes
SetupMutex=Global\AutocadTools.Setup
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}\Contents\Windows\2025-2026\ExcelRuntime"
Type: filesandordirs; Name: "{app}\Contents\Windows\2025-2026\Assets\AdditionalFunctions"
Type: filesandordirs; Name: "{app}\Contents\Windows\2027\ExcelRuntime"
Type: filesandordirs; Name: "{app}\Contents\Windows\2027\Assets\AdditionalFunctions"

Type: files; Name: "{app}\Contents\Windows\2025-2026\*.dll"
Type: files; Name: "{app}\Contents\Windows\2025-2026\*.deps.json"
Type: files; Name: "{app}\Contents\Windows\2027\*.dll"
Type: files; Name: "{app}\Contents\Windows\2027\*.deps.json"

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectDir}\README.md"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "{#ProjectDir}\LICENSE"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "{#ProjectDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "{#ProjectDir}\docs\*"; DestDir: "{app}\Documentation\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectDir}\scripts\Get-LoadDiagnostics.ps1"; DestDir: "{app}\Documentation"; Flags: ignoreversion

[Code]
function GetInstallDirectory(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{commonpf}\Autodesk\ApplicationPlugins\AutocadTools.bundle')
  else
    Result := ExpandConstant('{userappdata}\Autodesk\ApplicationPlugins\AutocadTools.bundle');
end;

function AutoCADIsRunning(): Boolean;
var Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name FROM Win32_Process WHERE Name="acad.exe" OR Name="accoreconsole.exe"');
    Result := Processes.Count > 0;
  except
    RaiseException('Не удалось проверить, закрыт ли AutoCAD. / Cannot check whether AutoCAD is closed.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var RootKey: Integer; Key, InstallDir: String;
begin
  Result := '';
  if AutoCADIsRunning() then
  begin
    Result := 'Закройте AutoCAD и Core Console перед установкой или обновлением. / Close AutoCAD and Core Console before installing.';
    Exit;
  end;
  if IsAdminInstallMode then RootKey := HKLM64 else RootKey := HKCU64;
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D3A3F30D-A5F7-4DF9-A6D4-CC17D24B83DB}_is1';
  if RegQueryStringValue(RootKey, Key, 'InstallLocation', InstallDir) then
  begin
    // In-place upgrades preserve the existing installation until files are replaced.
    if CompareText(RemoveBackslashUnlessRoot(InstallDir), RemoveBackslashUnlessRoot(WizardDirValue)) <> 0 then
    begin
      Result := 'Обнаружена другая папка установки Autocad Tools. Сначала удалите прежнюю копию через «Установленные приложения» Windows: ' + InstallDir;
      Exit;
    end;

  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := not AutoCADIsRunning();
  if not Result then
    MsgBox('Закройте AutoCAD и Core Console перед удалением. / Close AutoCAD and Core Console before uninstalling.', mbError, MB_OK);
end;
