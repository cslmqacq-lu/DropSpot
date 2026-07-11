#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "DropSpot"
#define MyAppExeName "DropSpot.exe"
#define MyAppPublisher "cslm"
#define PortableDir "..\artifacts\portable\DropSpot_v" + MyAppVersion + "_win-x64"

[Setup]
AppId={{6D2C8928-58E4-4E33-9E71-00BA91004EF8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
DefaultDirName={localappdata}\Programs\DropSpot
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=DropSpot_Setup_v{#MyAppVersion}_win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\DropSpot.ico
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[InstallDelete]
Type: files; Name: "{localappdata}\Programs\DiskWriteWatcher\DiskWriteWatcher.exe"
Type: files; Name: "{localappdata}\Programs\DiskWriteWatcher\README.txt"
Type: files; Name: "{localappdata}\Programs\DiskWriteWatcher\unins000.exe"
Type: files; Name: "{localappdata}\Programs\DiskWriteWatcher\unins000.dat"
Type: files; Name: "{userdesktop}\活跃文件夹.lnk"
Type: filesandordirs; Name: "{userprograms}\活跃文件夹"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#PortableDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PortableDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure StopProcessByName(const FileName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "' + FileName + '" /T /F', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopProcessByName('DiskWriteWatcher.exe');
  StopProcessByName('DropSpot.exe');
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RemoveDir(ExpandConstant('{localappdata}\Programs\DiskWriteWatcher'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DropSpot');
end;
