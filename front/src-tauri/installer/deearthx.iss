; ============================================================
; DeEarthX V4 - Inno Setup 安装脚本
; 从 NSIS 迁移至 Inno Setup，保持全部功能
; ============================================================

#define AppName        "DeEarthX V4"
#define AppVersion     "4.0.1-beta"
#define AppPublisher   "DeEarthX-CE"
#define AppURL         "https://github.com/DeEarthX-CE"
#define AppExeName     "dex-v4-ui.exe"
#define AppBinName     "core.exe"
#define AppRegKey      "Software\DeEarthX-CE\DeEarthX-V4"
#define AppUninstKey   "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeEarthX V4"

[Setup]
AppId={{8F3A7E2B-1D45-4C98-A6F2-E9D8B71C3405}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={localappdata}\DeEarthX-CE\{#AppName}
DefaultGroupName=DeEarthX-CE\{#AppName}
AllowNoIcons=yes
OutputDir=..\target\release\bundle\inno
OutputBaseFilename=DeEarthX-V4-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion=4.0.1.0
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=(c) 2025 DeEarthX-CE
VersionInfoDescription={#AppName} Installer

; ── 语言 ──────────────────────────────────────────────────
[Languages]
Name: "chs"; MessagesFile: "compiler:Languages\Chinese.isl"
Name: "en";  MessagesFile: "compiler:Default.isl"
Name: "ja";  MessagesFile: "compiler:Languages\Japanese.isl"
Name: "ko";  MessagesFile: "compiler:Languages\Korean.isl"

; ── 各语言欢迎页文案 ──────────────────────────────────────
[CustomMessages]
; 简体中文
chs.WelcomeLabel2=DeEarthX V4 是一款快速、现代的 Minecraft 服务端管理工具。%n%n支持一键创建服务端、AI 崩溃检测、%n多版本整合包管理等。%n%n点击下一步继续。
chs.FinishedLabel=DeEarthX V4 已成功安装。%n%n启动程序，开始管理你的 Minecraft 服务端吧！
chs.LaunchProgram=启动 DeEarthX V4
chs.ViewDocs=查看文档
chs.DirDesc=选择 DeEarthX V4 的安装路径。%n%n建议使用默认路径。
chs.UninstConfirm=确定要完全卸载 DeEarthX V4 吗？
chs.AppTitle=DeEarthX V4 安装完成

; 英语
en.WelcomeLabel2=DeEarthX V4 is a fast, modern Minecraft server management tool.%n%nIt supports one-click server creation, AI crash detection,%nand multi-version modpack management.%n%nClick Next to continue.
en.FinishedLabel=DeEarthX V4 has been successfully installed.%n%nLaunch the application and start managing your Minecraft servers!
en.LaunchProgram=Launch DeEarthX V4
en.ViewDocs=View Documentation
en.DirDesc=Select the installation folder for DeEarthX V4.%n%nRecommended: keep the default path.
en.UninstConfirm=Are you sure you want to completely remove DeEarthX V4?
en.AppTitle=DeEarthX V4 Installation Complete

; 日语
ja.WelcomeLabel2=DeEarthX V4 は、高速でモダンな Minecraft サーバー管理ツールです。%n%nワンクリックでのサーバー作成、AI クラッシュ検出、%nマルチバージョン Modpack 管理をサポートしています。%n%n「次へ」をクリックして続行します。
ja.FinishedLabel=DeEarthX V4 が正常にインストールされました。%n%nアプリケーションを起動して、Minecraft サーバーを管理しましょう！
ja.LaunchProgram=DeEarthX V4 を起動
ja.ViewDocs=ドキュメントを表示
ja.DirDesc=DeEarthX V4 のインストール先を選択してください。%n%nデフォルトのパスを推奨します。
ja.UninstConfirm=DeEarthX V4 を完全に削除してもよろしいですか？
ja.AppTitle=DeEarthX V4 のインストールが完了しました

; 韩语
ko.WelcomeLabel2=DeEarthX V4는 빠르고 현대적인 Minecraft 서버 관리 도구입니다.%n%n원클릭 서버 생성, AI 충돌 감지,%n다중 버전 모드팩 관리를 지원합니다.%n%n다음을 클릭하여 계속하세요.
ko.FinishedLabel=DeEarthX V4가 성공적으로 설치되었습니다.%n%n애플리케이션을 실행하여 Minecraft 서버를 관리하세요!
ko.LaunchProgram=DeEarthX V4 실행
ko.ViewDocs=문서 보기
ko.DirDesc=DeEarthX V4의 설치 폴더를 선택하세요.%n%n기본 경로를 권장합니다.
ko.UninstConfirm=DeEarthX V4를 완전히 제거하시겠습니까?
ko.AppTitle=DeEarthX V4 설치 완료

; ── 覆盖各语言内置向导文本 ────────────────────────────────
[Messages]
; 简体中文
chs.WelcomeLabel1={#AppName}
chs.WizardSelectDir=选择安装位置
chs.SelectDirLabel3=选择 DeEarthX V4 的安装路径。%n%n建议使用默认路径。
chs.FinishedHeadingLabel=DeEarthX V4 安装完成
chs.FinishedLabel=DeEarthX V4 已成功安装。%n%n启动程序，开始管理你的 Minecraft 服务端吧！
chs.ConfirmUninstall=确定要完全卸载 DeEarthX V4 吗？
chs.ButtonNext=下一步(&N) >
chs.ButtonInstall=安装(&I)
chs.ButtonFinish=完成(&F)
chs.SetupWindowTitle={#AppName} 安装向导
chs.UninstallAppFullTitle={#AppName} 卸载

; 英语
en.WelcomeLabel1={#AppName}
en.WizardSelectDir=Select Installation Location
en.SelectDirLabel3=Select the installation folder for DeEarthX V4.%n%nRecommended: keep the default path.
en.FinishedHeadingLabel=DeEarthX V4 Installation Complete
en.FinishedLabel=DeEarthX V4 has been successfully installed.%n%nLaunch the application and start managing your Minecraft servers!
en.ConfirmUninstall=Are you sure you want to completely remove DeEarthX V4?
en.SetupWindowTitle={#AppName} Setup
en.UninstallAppFullTitle={#AppName} Uninstall

; 日语
ja.WelcomeLabel1={#AppName}
ja.WizardSelectDir=インストール先の選択
ja.SelectDirLabel3=DeEarthX V4 のインストール先を選択してください。%n%nデフォルトのパスを推奨します。
ja.FinishedHeadingLabel=DeEarthX V4 のインストールが完了しました
ja.FinishedLabel=DeEarthX V4 が正常にインストールされました。%n%nアプリケーションを起動して、Minecraft サーバーを管理しましょう！
ja.ConfirmUninstall=DeEarthX V4 を完全に削除してもよろしいですか？
ja.ButtonNext=次へ(&N) >
ja.ButtonInstall=インストール(&I)
ja.ButtonFinish=完了(&F)
ja.SetupWindowTitle={#AppName} セットアップ
ja.UninstallAppFullTitle={#AppName} アンインストール

; 韩语
ko.WelcomeLabel1={#AppName}
ko.WizardSelectDir=설치 위치 선택
ko.SelectDirLabel3=DeEarthX V4의 설치 폴더를 선택하세요.%n%n기본 경로를 권장합니다.
ko.FinishedHeadingLabel=DeEarthX V4 설치 완료
ko.FinishedLabel=DeEarthX V4가 성공적으로 설치되었습니다.%n%n애플리케이션을 실행하여 Minecraft 서버를 관리하세요!
ko.ConfirmUninstall=DeEarthX V4를 완전히 제거하시겠습니까?
ko.ButtonNext=다음(&N) >
ko.ButtonInstall=설치(&I)
ko.ButtonFinish=완료(&F)
ko.SetupWindowTitle={#AppName} 설치
ko.UninstallAppFullTitle={#AppName} 제거

; ── 文件（相对路径：从 installer/ 到 target/release/） ────
[Files]
Source: "..\target\release\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\{#AppBinName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\*.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\*.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\*.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\target\release\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\target\release\web.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; ── 快捷方式 ──────────────────────────────────────────────
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

; ── 安装后运行 ────────────────────────────────────────────
[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; ── 注册表 ────────────────────────────────────────────────
[Registry]
Root: HKLM; Subkey: "{#AppRegKey}"; ValueType: string; ValueName: ""; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#AppName}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "UninstallString"; ValueData: "{uninstallexe}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "DisplayIcon"; ValueData: "{app}\{#AppExeName}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "DisplayVersion"; ValueData: "{#AppVersion}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "Publisher"; ValueData: "{#AppPublisher}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: string; ValueName: "URLInfoAbout"; ValueData: "{#AppURL}"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: dword; ValueName: "NoModify"; ValueData: "1"
Root: HKLM; Subkey: "{#AppUninstKey}"; ValueType: dword; ValueName: "NoRepair"; ValueData: "1"

; ── 卸载时清理 ────────────────────────────────────────────
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

; ── 安装前检查 ────────────────────────────────────────────
[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;

// 卸载前确认
function InitializeUninstall: Boolean;
begin
  Result := True;
end;