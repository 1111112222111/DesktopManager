#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceExe
  #error SourceExe is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename is required
#endif
#ifndef IconFile
  #error IconFile is required
#endif

[Setup]
AppId={{73DB05FD-0966-4E70-8F98-80B95B01DB59}
AppName=桌面管理
AppVersion={#AppVersion}
AppVerName=桌面管理 {#AppVersion}
AppPublisher=DesktopManager
AppPublisherURL=https://github.com/1111112222111/DesktopManager
AppSupportURL=https://github.com/1111112222111/DesktopManager/issues
AppUpdatesURL=https://github.com/1111112222111/DesktopManager/releases
DefaultDirName={localappdata}\Programs\DesktopManager
DefaultGroupName=桌面管理
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\DesktopManager.App.exe
UninstallDisplayName=桌面管理
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=桌面管理
VersionInfoDescription=桌面管理安装程序
VersionInfoCompany=DesktopManager
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "DesktopManager.App.exe"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\Install.cmd"
Type: files; Name: "{app}\Uninstall.cmd"
Type: files; Name: "{app}\install.ps1"
Type: files; Name: "{app}\uninstall.ps1"
Type: files; Name: "{app}\README.txt"
Type: files; Name: "{app}\release.json"

[Icons]
Name: "{autoprograms}\桌面管理"; Filename: "{app}\DesktopManager.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\桌面管理"; Filename: "{app}\DesktopManager.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DesktopManager.App.exe"; Description: "启动桌面管理"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RegDeleteKeyIncludingSubkeys(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopManager');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'DesktopManager');
end;
