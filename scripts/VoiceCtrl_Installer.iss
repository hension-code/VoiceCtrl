[Setup]
; 应用程序基本信息
AppName=VoiceCtrl
AppVersion=1.0.0
AppPublisher=VoiceCtrl Open Source
AppPublisherURL=https://github.com/hension-code/VoiceCtrl

; 默认安装路径 (当前用户的 Local AppData 下，或使用 {pf} 安装到 Program Files)
DefaultDirName={autopf}\VoiceCtrl
DefaultGroupName=VoiceCtrl

; 卸载程序图标
UninstallDisplayIcon={app}\VoiceCtrl.exe

; 压缩设置，使安装包体积尽可能小
Compression=lzma2/ultra64
SolidCompression=yes

; 输出安装程序保存位置和文件名
OutputDir=..\Output
OutputBaseFilename=VoiceCtrl_Installer_v1.0

; 界面风格设置
WizardStyle=modern
PrivilegesRequired=lowest

[Tasks]
; 附加任务：创建桌面快捷方式与开机自启
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式:"; Flags: unchecked
Name: "startupicon"; Description: "开机自动启动 VoiceCtrl"; GroupDescription: "启动选项:"; Flags: unchecked

[Files]
; 指向你 build.ps1 打包出的目标单文件（务必在此之前运行 build.ps1）
Source: "..\VoiceCtrl\bin\Release\net8.0-windows\win-x64\publish\VoiceCtrl.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单快捷方式
Name: "{group}\VoiceCtrl"; Filename: "{app}\VoiceCtrl.exe"
; 桌面快捷方式（依赖上述 Task）
Name: "{autodesktop}\VoiceCtrl"; Filename: "{app}\VoiceCtrl.exe"; Tasks: desktopicon
; 开机自启动启动文件夹快捷方式（依赖上述 Task）
Name: "{userstartup}\VoiceCtrl"; Filename: "{app}\VoiceCtrl.exe"; Tasks: startupicon

[Run]
; 安装完成后允许立即运行
Filename: "{app}\VoiceCtrl.exe"; Description: "启动 VoiceCtrl"; Flags: nowait postinstall skipifsilent

[Code]
// The CurStepChanged is called right before actual files get copied (ssInstall)
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Forcefully kill any running VoiceCtrl.exe to release file locks
    Exec('cmd.exe', '/c taskkill /f /im VoiceCtrl.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('cmd.exe', '/c taskkill /f /im TypeNo.Windows.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('cmd.exe', '/c taskkill /f /im VoiceCtrl.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('cmd.exe', '/c taskkill /f /im TypeNo.Windows.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
