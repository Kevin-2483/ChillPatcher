; =============================================================================
; OmniMixPlayer Installer Script
; 使用 Inno Setup 7 构建: "C:\Program Files\Inno Setup 7\ISCC.exe" this_script.iss
; =============================================================================

#define MyAppName "OmniMixPlayer"
#define MyAppVersion "3.0.4"
#define MyAppPublisher "Kevin-2483"
#define MyAppURL "https://github.com/Kevin-2483/Chill"
#define MyAppExeName "omnimix_gui.exe"
#define MyAppBackendName "OmniMixPlayer.Backend.exe"
#define MyServiceName "OmniMixPlayerBackend"
; SourceDir — 构建时由脚本自动替换为实际 playerbuild 路径
#define SourceDir "E:/Document/Github/ChillPatcher/playerbuild"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
; Windows service installation and migration require elevation.
PrivilegesRequired=admin
OutputDir=E:/Document/Github/ChillPatcher/release
OutputBaseFilename=OmniMixPlayer_V3.0.4_installer
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
; 最小 Windows 版本: Windows 10 (10.0.10240) 或 Windows 11 on Arm
MinVersion=10.0.10240

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[CustomMessages]
english.CleanupPageTitle=Optional cleanup
english.CleanupPageDescription=Choose data left by previous OmniMixPlayer installations
english.CleanupPageSubCaption=All options are disabled by default. Only select data you explicitly want to remove.
english.OldInstallDirPageTitle=Previous installation directory
english.OldInstallDirPageDescription=Select the previous OmniMixPlayer installation to remove
english.OldInstallDirPageSubCaption=The detected directory is filled automatically. You can browse to another previous installation directory. The directory is only deleted when the deletion option is selected.
english.OldInstallDirLabel=Previous installation directory:
english.CleanupAudioCache=Clear rebuildable audio and image caches
english.CleanupGuiSettings=Reset desktop GUI preferences (theme, volume, shortcuts, window position and saved game paths)
english.CleanupPreviousInstallDir=Delete the entire previous installation directory (removes its backend configuration, library and module data)
english.CleanupLoginData=Remove music service sessions stored in default locations (NetEase, QQ Music, Bilibili and Spotify)
english.CleanupIntegrationData=Remove game integration records, downloaded mod copies and backups (installed game mods remain, but automatic uninstall/restore may no longer work)
english.CleanupConfirm=The selected cleanup options may delete the entire previous installation directory, settings, login sessions or integration backups.%n%nThis operation cannot be undone. Continue?
english.CleanupOldInstall=Previous backend directory detected:
english.CleanupPreviousInstall=Previous installer directory detected:
english.CleanupNoOldInstall=No valid previous installation directory was detected. Directory deletion is unavailable; other cleanup options still apply to the selected installation directory and user profile data.
english.CleanupLogPrefix=Optional cleanup:
english.ServiceUpdateFailed=Failed to update the OmniMixPlayerBackend service path. Check the setup log or run the installer as administrator.
english.InvalidOldInstallDir=The selected previous installation directory is not recognized as an OmniMixPlayer installation:
english.DeleteOldInstallFailed=Failed to completely delete the previous installation directory. Close programs using this directory and try again:
english.CleanupPathFailed=Failed to remove selected cleanup data. Close OmniMixPlayer and try again:
chinesesimplified.CleanupPageTitle=可选清理
chinesesimplified.CleanupPageDescription=选择需要清理的旧版 OmniMixPlayer 数据
chinesesimplified.CleanupPageSubCaption=所有选项默认不勾选。请只选择你明确需要删除的数据。
chinesesimplified.OldInstallDirPageTitle=原安装目录
chinesesimplified.OldInstallDirPageDescription=选择需要删除的旧版 OmniMixPlayer 安装目录
chinesesimplified.OldInstallDirPageSubCaption=安装器会自动填入检测到的目录，你也可以浏览选择其他旧版安装目录。仅在勾选删除选项后才会删除该目录。
chinesesimplified.OldInstallDirLabel=原安装目录：
chinesesimplified.CleanupAudioCache=清理可重新生成的音频与图片缓存
chinesesimplified.CleanupGuiSettings=重置桌面 GUI 偏好（主题、音量、快捷键、窗口位置和已保存的游戏路径）
chinesesimplified.CleanupPreviousInstallDir=完整删除原安装目录（包括其中的后端配置、曲库和模块数据）
chinesesimplified.CleanupLoginData=删除默认位置中的音乐源登录状态（网易云、QQ 音乐、Bilibili 和 Spotify）
chinesesimplified.CleanupIntegrationData=删除游戏集成记录、已下载的 Mod 副本和备份（不会卸载游戏中的 Mod，但之后可能无法自动卸载或恢复）
chinesesimplified.CleanupConfirm=所选清理项可能完整删除原安装目录、设置、登录状态或游戏集成备份。%n%n此操作无法撤销，是否继续？
chinesesimplified.CleanupOldInstall=检测到旧后端目录：
chinesesimplified.CleanupPreviousInstall=检测到上一次安装目录：
chinesesimplified.CleanupNoOldInstall=未检测到有效的原安装目录，无法使用完整目录删除；其他清理选项仍会作用于当前安装目录和用户配置目录。
chinesesimplified.CleanupLogPrefix=可选清理：
chinesesimplified.ServiceUpdateFailed=OmniMixPlayerBackend 服务路径更新失败。请检查安装日志，或以管理员身份重新运行安装程序。
chinesesimplified.InvalidOldInstallDir=所选目录不是可识别的 OmniMixPlayer 安装目录：
chinesesimplified.DeleteOldInstallFailed=无法完整删除原安装目录。请关闭正在使用该目录的程序后重试：
chinesesimplified.CleanupPathFailed=无法删除所选清理数据。请关闭 OmniMixPlayer 后重试：

[Files]
; ═══ 所有可执行文件和 DLL (排除 VC 运行库，它单独处理) ═══
Source: "{#SourceDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion; Excludes: "VC_redist.x64.exe"
Source: "{#SourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; ═══ 配置文件 ═══
Source: "{#SourceDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.config"; DestDir: "{app}"; Flags: ignoreversion

; ═══ data 目录 (Flutter 资源) ═══
Source: "{#SourceDir}\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs

; ═══ wwwroot 目录 (Web GUI) ═══
Source: "{#SourceDir}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs

; ═══ modules 目录 (插件模块) ═══
Source: "{#SourceDir}\modules\*"; DestDir: "{app}\modules"; Flags: ignoreversion recursesubdirs createallsubdirs

; ═══ native 目录 (原生库) ═══
Source: "{#SourceDir}\native\*"; DestDir: "{app}\native"; Flags: ignoreversion recursesubdirs createallsubdirs

; ═══ VC++ 运行库安装器 (复制到临时目录，安装后删除) ═══
Source: "{#SourceDir}\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
; NOTE: 不要对共享系统文件使用 "Flags: ignoreversion"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; VC++ 运行库 — 仅在未安装时运行
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/quiet /norestart"; \
  StatusMsg: "正在安装 VC++ 运行库..."; Flags: waituntilterminated; \
  Check: ShouldInstallVCRedist

; 安装后启动 GUI
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载前停止并删除服务
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopOmniMixPlayerBackend"; Check: ShouldRemoveServiceOnUninstall
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteOmniMixPlayerBackend"; Check: ShouldRemoveServiceOnUninstall

; ═════════════════════════════════════════════════════════════════════════════
; [Code] 部分 — Pascal 脚本
; ═════════════════════════════════════════════════════════════════════════════

[Code]

var
  CleanupPage: TInputOptionWizardPage;
  OldInstallDirPage: TInputDirWizardPage;
  OldServiceDir: string;
  PreviousInstallDir: string;
  ConfirmedCleanupSignature: string;
  CleanupFailure: string;
  ApprovedOldInstallDir: string;

const
  ServiceRegistryPath = 'SYSTEM\CurrentControlSet\Services\{#MyServiceName}';
  UninstallRegistryPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}_is1';

// ── 字符串和路径辅助函数 ──
function StripTrailingSlash(Value: string): string;
begin
  Result := Trim(Value);
  while (Length(Result) > 3) and
        ((Result[Length(Result)] = '\') or (Result[Length(Result)] = '/')) do
    Delete(Result, Length(Result), 1);
end;

function ExtractExecutablePath(ImagePath: string): string;
var
  EndPos: Integer;
  LowerPath: string;
begin
  Result := '';
  ImagePath := Trim(ImagePath);
  if ImagePath = '' then
    Exit;

  if ImagePath[1] = '"' then
  begin
    Delete(ImagePath, 1, 1);
    EndPos := Pos('"', ImagePath);
    if EndPos > 0 then
      Result := Copy(ImagePath, 1, EndPos - 1)
    else
      Result := ImagePath;
  end
  else
  begin
    LowerPath := Lowercase(ImagePath);
    EndPos := Pos('.exe', LowerPath);
    if EndPos > 0 then
      Result := Copy(ImagePath, 1, EndPos + 3)
    else
      Result := ImagePath;
  end;

  Result := RemoveQuotes(Trim(Result));
end;

function ReadServiceInstallDir(): string;
var
  ImagePath: string;
  BackendPath: string;
begin
  Result := '';
  ImagePath := '';
  if IsWin64 then
    RegQueryStringValue(HKLM64, ServiceRegistryPath, 'ImagePath', ImagePath);
  if ImagePath = '' then
    RegQueryStringValue(HKLM, ServiceRegistryPath, 'ImagePath', ImagePath);

  BackendPath := ExtractExecutablePath(ImagePath);
  if BackendPath <> '' then
    Result := StripTrailingSlash(ExtractFileDir(BackendPath));
end;

function ReadRegisteredInstallDir(): string;
begin
  Result := '';
  if IsWin64 then
    RegQueryStringValue(HKLM64, UninstallRegistryPath, 'InstallLocation', Result);
  if Result = '' then
    RegQueryStringValue(HKLM, UninstallRegistryPath, 'InstallLocation', Result);
  if Result = '' then
    RegQueryStringValue(HKCU, UninstallRegistryPath, 'InstallLocation', Result);
  Result := StripTrailingSlash(RemoveQuotes(Result));
end;

function ShouldRemoveServiceOnUninstall(): Boolean;
begin
  Result :=
    CompareText(
      ReadServiceInstallDir(),
      StripTrailingSlash(ExpandConstant('{app}'))
    ) = 0;
end;

function IsKnownInstallDir(const BaseDir: string): Boolean;
var
  Normalized: string;
begin
  Normalized := StripTrailingSlash(BaseDir);
  Result :=
    (Length(Normalized) > 3) and
    (
      FileExists(AddBackslash(Normalized) + '{#MyAppBackendName}') or
      FileExists(AddBackslash(Normalized) + '{#MyAppExeName}')
    );
end;

procedure DeleteFileLogged(const FileName: string);
begin
  if FileExists(FileName) then
  begin
    if DeleteFile(FileName) then
      Log(CustomMessage('CleanupLogPrefix') + ' deleted file ' + FileName)
    else
      Log(CustomMessage('CleanupLogPrefix') + ' failed to delete file ' + FileName);
  end;
end;

procedure DeleteTreeLogged(const DirName: string);
begin
  if DirExists(DirName) then
  begin
    if DelTree(DirName, True, True, True) then
      Log(CustomMessage('CleanupLogPrefix') + ' deleted directory ' + DirName)
    else
      Log(CustomMessage('CleanupLogPrefix') + ' failed to delete directory ' + DirName);
  end;
end;

procedure DeleteFileRequired(const FileName: string);
begin
  if FileExists(FileName) and (not DeleteFile(FileName)) then
  begin
    Log(CustomMessage('CleanupLogPrefix') + ' failed to delete required file ' + FileName);
    if CleanupFailure = '' then
      CleanupFailure := CustomMessage('CleanupPathFailed') + #13#10 + FileName;
  end;
end;

procedure DeleteTreeRequired(const DirName: string);
begin
  if DirExists(DirName) and (not DelTree(DirName, True, True, True)) then
  begin
    Log(CustomMessage('CleanupLogPrefix') + ' failed to delete required directory ' + DirName);
    if CleanupFailure = '' then
      CleanupFailure := CustomMessage('CleanupPathFailed') + #13#10 + DirName;
  end;
end;

procedure DeleteFileOrTreeLogged(const Path: string);
begin
  DeleteFileLogged(Path);
  DeleteTreeLogged(Path);
end;

function DeletePreviousInstallDir(const BaseDir: string): Boolean;
var
  Normalized: string;
begin
  Result := False;
  Normalized := StripTrailingSlash(BaseDir);
  if (not IsKnownInstallDir(Normalized)) and
     (CompareText(Normalized, ApprovedOldInstallDir) <> 0) then
  begin
    if Normalized <> '' then
      Log(CustomMessage('CleanupLogPrefix') + ' refused unrecognized installation directory ' + Normalized);
    Exit;
  end;

  Result := DelTree(Normalized, True, True, True);
  if Result then
    Log(CustomMessage('CleanupLogPrefix') + ' deleted previous installation directory ' + Normalized)
  else
    Log(CustomMessage('CleanupLogPrefix') + ' failed to delete previous installation directory ' + Normalized);
end;

procedure CleanupInstallLocation(const BaseDir: string);
var
  ModulesDir: string;
begin
  if not IsKnownInstallDir(BaseDir) then
    Exit;

  ModulesDir := AddBackslash(StripTrailingSlash(BaseDir)) + 'modules\';

  if CleanupPage.Values[0] then
  begin
    DeleteTreeLogged(ModulesDir + 'com.chillpatcher.qqmusic\data\audio_cache');
    DeleteTreeLogged(ModulesDir + 'com.chillpatcher.spotify\data\librespot-cache');
  end;

  if CleanupPage.Values[3] then
  begin
    DeleteFileRequired(ModulesDir + 'com.chillpatcher.qqmusic\data\qqmusic_cookie.json');
    DeleteFileRequired(ModulesDir + 'com.chillpatcher.bilibili\data\bilibili_session.json');
    DeleteFileRequired(ModulesDir + 'com.chillpatcher.spotify\data\spotify_session.json');
  end;
end;

procedure RunOptionalCleanup();
var
  CurrentInstallDir: string;
  SelectedOldInstallDir: string;
  GuiDataDir: string;
begin
  CurrentInstallDir := StripTrailingSlash(WizardDirValue());
  SelectedOldInstallDir := StripTrailingSlash(OldInstallDirPage.Values[0]);

  if CleanupPage.Values[2] then
  begin
    if IsKnownInstallDir(SelectedOldInstallDir) then
      ApprovedOldInstallDir := SelectedOldInstallDir;

    if CompareText(SelectedOldInstallDir, ApprovedOldInstallDir) <> 0 then
      CleanupFailure :=
        CustomMessage('InvalidOldInstallDir') + #13#10 + SelectedOldInstallDir
    else if not DeletePreviousInstallDir(SelectedOldInstallDir) then
      CleanupFailure :=
        CustomMessage('DeleteOldInstallFailed') + #13#10 + SelectedOldInstallDir;
  end;

  if CleanupFailure = '' then
  begin
    CleanupInstallLocation(SelectedOldInstallDir);
    if CompareText(CurrentInstallDir, SelectedOldInstallDir) <> 0 then
      CleanupInstallLocation(CurrentInstallDir);
  end;

  GuiDataDir := ExpandConstant('{userappdata}\com.omnimixplayer\omnimix_gui');

  if CleanupPage.Values[0] then
  begin
    DeleteTreeLogged(AddBackslash(GetEnv('TEMP')) + 'chillpatcher_audio_cache');
    DeleteTreeLogged(ExpandConstant('{win}\SystemTemp\chillpatcher_audio_cache'));
    DeleteFileLogged(AddBackslash(GuiDataDir) + 'libCachedImageData.json');
  end;

  if CleanupPage.Values[1] then
    DeleteFileLogged(AddBackslash(GuiDataDir) + 'shared_preferences.json');

  if CleanupPage.Values[3] then
  begin
    DeleteTreeRequired(ExpandConstant('{localappdata}\go-musicfox'));
    DeleteTreeRequired(ExpandConstant('{sys}\config\systemprofile\AppData\Local\go-musicfox'));
  end;

  if CleanupPage.Values[4] then
    DeleteTreeLogged(ExpandConstant('{localappdata}\OmniMixPlayer\mod_manager'));
end;

procedure InitializeWizard();
var
  PageSubCaption: string;
  DetectedInstallDir: string;
begin
  ConfirmedCleanupSignature := '';
  CleanupFailure := '';
  ApprovedOldInstallDir := '';
  OldServiceDir := ReadServiceInstallDir();
  PreviousInstallDir := ReadRegisteredInstallDir();

  if IsKnownInstallDir(PreviousInstallDir) then
    DetectedInstallDir := PreviousInstallDir
  else if IsKnownInstallDir(OldServiceDir) then
    DetectedInstallDir := OldServiceDir
  else
    DetectedInstallDir := '';

  OldInstallDirPage := CreateInputDirPage(
    wpSelectDir,
    CustomMessage('OldInstallDirPageTitle'),
    CustomMessage('OldInstallDirPageDescription'),
    CustomMessage('OldInstallDirPageSubCaption'),
    False,
    ''
  );
  OldInstallDirPage.Add(CustomMessage('OldInstallDirLabel'));
  OldInstallDirPage.Values[0] := DetectedInstallDir;

  if DetectedInstallDir <> '' then
  begin
    PageSubCaption := CustomMessage('CleanupPageSubCaption');
    PageSubCaption :=
      PageSubCaption + #13#10 + #13#10 +
      CustomMessage('CleanupPreviousInstall') + #13#10 + DetectedInstallDir;
  end
  else
    PageSubCaption :=
      CustomMessage('CleanupPageSubCaption') + #13#10 + #13#10 +
      CustomMessage('CleanupNoOldInstall');

  CleanupPage := CreateInputOptionPage(
    OldInstallDirPage.ID,
    CustomMessage('CleanupPageTitle'),
    CustomMessage('CleanupPageDescription'),
    PageSubCaption,
    False,
    False
  );
  CleanupPage.Add(CustomMessage('CleanupAudioCache'));
  CleanupPage.Add(CustomMessage('CleanupGuiSettings'));
  CleanupPage.Add(CustomMessage('CleanupPreviousInstallDir'));
  CleanupPage.Add(CustomMessage('CleanupLoginData'));
  CleanupPage.Add(CustomMessage('CleanupIntegrationData'));
end;

function GetCleanupSignature(): string;
var
  I: Integer;
begin
  Result := Lowercase(StripTrailingSlash(OldInstallDirPage.Values[0])) + '|';
  for I := 0 to 4 do
  begin
    if CleanupPage.Values[I] then
      Result := Result + '1'
    else
      Result := Result + '0';
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  HasDestructiveSelection: Boolean;
  CleanupSignature: string;
begin
  Result := True;
  if CurPageID <> CleanupPage.ID then
    Exit;

  HasDestructiveSelection :=
    CleanupPage.Values[1] or
    CleanupPage.Values[2] or
    CleanupPage.Values[3] or
    CleanupPage.Values[4];

  CleanupSignature := GetCleanupSignature();
  if HasDestructiveSelection and
     (CompareText(CleanupSignature, ConfirmedCleanupSignature) <> 0) then
  begin
    Result :=
      MsgBox(
        CustomMessage('CleanupConfirm'),
        mbConfirmation,
        MB_YESNO
      ) = IDYES;
    if Result then
      ConfirmedCleanupSignature := CleanupSignature
    else
      ConfirmedCleanupSignature := '';
  end;
end;

// ── 检查 VC++ 2015-2022 x64 运行库是否已安装 ──
function IsVCRedistInstalled(): Boolean;
var
  installValue: Cardinal;
begin
  // 检查 Visual Studio 2015-2022 (v14.0+) x64 运行库注册表
  if RegQueryDWordValue(
    HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed',
    installValue
  ) then
  begin
    Result := (installValue = 1);
  end
  else
    Result := False;
end;

// ── 决定是否需要安装 VC++ 运行库 ──
function ShouldInstallVCRedist(): Boolean;
begin
  Result := not IsVCRedistInstalled();
end;

// ── 从文件读取内容的辅助函数 ──
function GetFileContents(const FileName: string): string;
var
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if LoadStringsFromFile(FileName, Lines) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
      Result := Result + Lines[I] + #13#10;
  end;
end;

// ── 检查服务是否存在 (sc query 不存在时返回 exit code 1060) ──
function ServiceExists(ServiceName: string): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'query "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// ── 检查并停止服务 ──
function StopService(ServiceName: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if ServiceExists(ServiceName) then
  begin
    Result :=
      Exec(
        ExpandConstant('{sys}\sc.exe'),
        'stop "' + ServiceName + '"',
        '',
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode
      ) and ((ResultCode = 0) or (ResultCode = 1062));
    Sleep(1500);
  end;
end;

// ── 检查进程是否在运行 ──
function IsProcessRunning(ProcName: string): Boolean;
var
  ResultCode: Integer;
  TempFile: string;
begin
  TempFile := ExpandConstant('{tmp}\process_check.txt');
  // 使用 tasklist 检查进程
  Exec(ExpandConstant('{cmd}'), '/c ""' + ExpandConstant('{sys}\tasklist.exe') +
       '" /FI "IMAGENAME eq ' + ProcName + '" /NH > "' + TempFile + '""',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := FileExists(TempFile) and (Pos(ProcName, GetFileContents(TempFile)) > 0);
  if FileExists(TempFile) then
    DeleteFile(TempFile);
end;

// ── 强制终止进程 ──
procedure KillProcess(ProcName: string);
var
  ResultCode: Integer;
begin
  if IsProcessRunning(ProcName) then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM "' + ProcName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // 等待进程退出
    Sleep(2000);
  end;
end;

// ── 准备安装：初始化时调用 ──
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  CleanupFailure := '';

  // 1. 检查并停止 OmniMixPlayerBackend 服务
  if ServiceExists('{#MyServiceName}') then
  begin
    Log('检测到 {#MyServiceName} 服务正在运行，正在停止...');
    if not StopService('{#MyServiceName}') then
      Log('警告：停止 {#MyServiceName} 服务失败，将继续终止后端进程');
  end;

  // 2. 终止 GUI 进程
  if IsProcessRunning('{#MyAppExeName}') then
  begin
    Log('检测到 {#MyAppExeName} 正在运行，正在终止...');
    KillProcess('{#MyAppExeName}');
  end;

  // 3. 终止 Backend 进程（可能以非服务方式运行）
  if IsProcessRunning('{#MyAppBackendName}') then
  begin
    Log('检测到 {#MyAppBackendName} 正在运行，正在终止...');
    KillProcess('{#MyAppBackendName}');
  end;

  // 再次确认所有进程已停止
  Sleep(1000);

  // 4. 执行用户在向导中明确选择的清理项
  RunOptionalCleanup();
  Result := CleanupFailure;
end;

// ── 安装完成后创建或更新服务 ──
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  BackendPath: string;
  ServiceUpdated: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    BackendPath := ExpandConstant('{app}\{#MyAppBackendName}');

    if ServiceExists('{#MyServiceName}') then
    begin
      ServiceUpdated :=
        Exec(
          ExpandConstant('{sys}\sc.exe'),
          'config "{#MyServiceName}" binPath= "' + BackendPath + '" start= demand',
          '',
          SW_HIDE,
          ewWaitUntilTerminated,
          ResultCode
        ) and (ResultCode = 0);
      if ServiceUpdated then
        Log('服务 {#MyServiceName} 已更新到新路径：' + BackendPath);
    end
    else
    begin
      ServiceUpdated :=
        Exec(
          ExpandConstant('{sys}\sc.exe'),
          'create "{#MyServiceName}" binPath= "' + BackendPath + '" start= demand',
          '',
          SW_HIDE,
          ewWaitUntilTerminated,
          ResultCode
        ) and (ResultCode = 0);
      if ServiceUpdated then
        Log('服务 {#MyServiceName} 创建成功：' + BackendPath);
    end;

    if ServiceUpdated then
    begin
      // 配置 DACL：允许 Authenticated Users (AU) 无提权启停服务
      Exec(ExpandConstant('{sys}\sc.exe'), 'sdset {#MyServiceName} D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCDCRPWP;;;AU)',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Log('服务 DACL 配置完成');
    end
    else
    begin
      Log('错误：服务 {#MyServiceName} 创建或更新失败，错误代码：' + IntToStr(ResultCode));
      MsgBox(
        CustomMessage('ServiceUpdateFailed'),
        mbError,
        MB_OK
      );
    end;
  end;
end;




