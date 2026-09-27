; ============================================================
; DeEarthX V4 - 自定义 NSIS 安装器模板
; UI 风格与 DeEarthX-CE 程序一致：简洁、现代、绿白色调
; 注意：页面和安装逻辑由 Tauri CLI 处理，此文件仅定制 UI
; ============================================================

; ── 基础定义 ──────────────────────────────────────────────
Unicode true
ManifestDPIAware true
RequestExecutionLevel admin

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; ── 品牌信息 ──────────────────────────────────────────────
!define PRODUCT_NAME         "DeEarthX V4"
!define PRODUCT_VERSION      "4.0.1-beta"
!define PRODUCT_PUBLISHER    "DeEarthX-CE"
!define PRODUCT_WEB_SITE     "https://github.com/DeEarthX-CE"
!define PRODUCT_DIR_REGKEY   "Software\DeEarthX-CE\DeEarthX-V4"
!define PRODUCT_UNINST_KEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"

; ── UI 风格：DeEarthX 绿白主题 ────────────────────────────
!define MUI_ABORTWARNING
!define MUI_BGCOLOR "F5F5F5"

; ── 页面文案（英语默认值） ────────────────────────────────
!define MUI_WELCOMEPAGE_TITLE          "Welcome to DeEarthX V4"
!define MUI_WELCOMEPAGE_TEXT           "DeEarthX V4 is a fast, modern Minecraft server management tool.\
                                        $\n$\nIt supports one-click server creation, AI crash detection,\
                                        $\nand multi-version modpack management.\
                                        $\n$\nClick Next to continue."
!define MUI_FINISHPAGE_TITLE           "Installation Complete"
!define MUI_FINISHPAGE_TEXT            "DeEarthX V4 has been successfully installed.\
                                        $\n$\nLaunch the application and start managing your Minecraft servers!"
!define MUI_FINISHPAGE_RUN             "$INSTDIR\DeEarthX V4.exe"
!define MUI_FINISHPAGE_RUN_TEXT        "Launch DeEarthX V4"
!define MUI_FINISHPAGE_SHOWREADME      "${PRODUCT_WEB_SITE}"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "View Documentation"
!define MUI_DIRECTORYPAGE_TEXT_TOP     "Select the installation folder for DeEarthX V4.\
                                        $\n$\nRecommended: keep the default path."
!define MUI_UNCONFIRMPAGE_TEXT_TOP     "Are you sure you want to completely remove DeEarthX V4?"

; ── 页面（Tauri 会追加自己的 Section） ────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; ── 卸载器页面 ────────────────────────────────────────────
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ── 语言（必须在页面之后） ────────────────────────────────
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "TradChinese"
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "Korean"

; ── 覆盖各语言 MUI 内置文本 ───────────────────────────────

LangString MUI_TEXT_WELCOME_INFO_TITLE  ${LANG_SIMPCHINESE} "欢迎使用 DeEarthX V4"
LangString MUI_TEXT_WELCOME_INFO_TEXT   ${LANG_SIMPCHINESE} "DeEarthX V4 是一款快速、现代的 Minecraft 服务端管理工具。$\n$\n支持一键创建服务端、AI 崩溃检测、$\n多版本整合包管理等。$\n$\n点击下一步继续。"
LangString MUI_TEXT_FINISH_INFO_TITLE   ${LANG_SIMPCHINESE} "DeEarthX V4 安装完成"
LangString MUI_TEXT_FINISH_INFO_TEXT    ${LANG_SIMPCHINESE} "DeEarthX V4 已成功安装。$\n$\n启动程序，开始管理你的 Minecraft 服务端吧！"
LangString MUI_TEXT_FINISH_RUN_TEXT     ${LANG_SIMPCHINESE} "启动 DeEarthX V4"
LangString MUI_TEXT_FINISH_SHOWREADME_TEXT ${LANG_SIMPCHINESE} "查看文档"
LangString MUI_DIRECTORYPAGE_TEXT_TOP   ${LANG_SIMPCHINESE} "选择 DeEarthX V4 的安装路径。$\n$\n建议使用默认路径。"
LangString MUI_UNCONFIRMPAGE_TEXT_TOP   ${LANG_SIMPCHINESE} "确定要完全卸载 DeEarthX V4 吗？"

LangString MUI_TEXT_WELCOME_INFO_TITLE  ${LANG_TRADCHINESE} "歡迎使用 DeEarthX V4"
LangString MUI_TEXT_WELCOME_INFO_TEXT   ${LANG_TRADCHINESE} "DeEarthX V4 是一款快速、現代的 Minecraft 伺服器管理工具。$\n$\n支援一鍵建立伺服器、AI 當機檢測、$\n多版本整合包管理等。$\n$\n點擊下一步繼續。"
LangString MUI_TEXT_FINISH_INFO_TITLE   ${LANG_TRADCHINESE} "DeEarthX V4 安裝完成"
LangString MUI_TEXT_FINISH_INFO_TEXT    ${LANG_TRADCHINESE} "DeEarthX V4 已成功安裝。$\n$\n啟動程式，開始管理你的 Minecraft 伺服器吧！"
LangString MUI_TEXT_FINISH_RUN_TEXT     ${LANG_TRADCHINESE} "啟動 DeEarthX V4"
LangString MUI_TEXT_FINISH_SHOWREADME_TEXT ${LANG_TRADCHINESE} "檢視文件"
LangString MUI_DIRECTORYPAGE_TEXT_TOP   ${LANG_TRADCHINESE} "選擇 DeEarthX V4 的安裝路徑。$\n$\n建議使用預設路徑。"
LangString MUI_UNCONFIRMPAGE_TEXT_TOP   ${LANG_TRADCHINESE} "確定要完全解除安裝 DeEarthX V4 嗎？"

LangString MUI_TEXT_WELCOME_INFO_TITLE  ${LANG_JAPANESE} "DeEarthX V4 へようこそ"
LangString MUI_TEXT_WELCOME_INFO_TEXT   ${LANG_JAPANESE} "DeEarthX V4 は、高速でモダンな Minecraft サーバー管理ツールです。$\n$\nワンクリックでのサーバー作成、AI クラッシュ検出、$\nマルチバージョン Modpack 管理をサポートしています。$\n$\n「次へ」をクリックして続行します。"
LangString MUI_TEXT_FINISH_INFO_TITLE   ${LANG_JAPANESE} "DeEarthX V4 のインストールが完了しました"
LangString MUI_TEXT_FINISH_INFO_TEXT    ${LANG_JAPANESE} "DeEarthX V4 が正常にインストールされました。$\n$\nアプリケーションを起動して、Minecraft サーバーを管理しましょう！"
LangString MUI_TEXT_FINISH_RUN_TEXT     ${LANG_JAPANESE} "DeEarthX V4 を起動"
LangString MUI_TEXT_FINISH_SHOWREADME_TEXT ${LANG_JAPANESE} "ドキュメントを表示"
LangString MUI_DIRECTORYPAGE_TEXT_TOP   ${LANG_JAPANESE} "DeEarthX V4 のインストール先を選択してください。$\n$\nデフォルトのパスを推奨します。"
LangString MUI_UNCONFIRMPAGE_TEXT_TOP   ${LANG_JAPANESE} "DeEarthX V4 を完全に削除してもよろしいですか？"

LangString MUI_TEXT_WELCOME_INFO_TITLE  ${LANG_KOREAN} "DeEarthX V4에 오신 것을 환영합니다"
LangString MUI_TEXT_WELCOME_INFO_TEXT   ${LANG_KOREAN} "DeEarthX V4는 빠르고 현대적인 Minecraft 서버 관리 도구입니다.$\n$\n원클릭 서버 생성, AI 충돌 감지,$\n다중 버전 모드팩 관리를 지원합니다.$\n$\n다음을 클릭하여 계속하세요."
LangString MUI_TEXT_FINISH_INFO_TITLE   ${LANG_KOREAN} "DeEarthX V4 설치 완료"
LangString MUI_TEXT_FINISH_INFO_TEXT    ${LANG_KOREAN} "DeEarthX V4가 성공적으로 설치되었습니다.$\n$\n애플리케이션을 실행하여 Minecraft 서버를 관리하세요!"
LangString MUI_TEXT_FINISH_RUN_TEXT     ${LANG_KOREAN} "DeEarthX V4 실행"
LangString MUI_TEXT_FINISH_SHOWREADME_TEXT ${LANG_KOREAN} "문서 보기"
LangString MUI_DIRECTORYPAGE_TEXT_TOP   ${LANG_KOREAN} "DeEarthX V4의 설치 폴더를 선택하세요.$\n$\n기본 경로를 권장합니다."
LangString MUI_UNCONFIRMPAGE_TEXT_TOP   ${LANG_KOREAN} "DeEarthX V4를 완전히 제거하시겠습니까?"

; ── 品牌文字 ──────────────────────────────────────────────
BrandingText "DeEarthX V4 - Minecraft Server Manager"

; ── 安装包名称（Tauri 会覆盖输出路径） ────────────────────
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
InstallDir "$PROGRAMFILES64\DeEarthX-CE\DeEarthX V4"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""

; ── 安装器版本信息 ────────────────────────────────────────
VIProductVersion "4.0.1.0"
VIAddVersionKey "ProductName"     "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName"     "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "(c) 2025 DeEarthX-CE"
VIAddVersionKey "FileDescription" "DeEarthX V4 Installer"
VIAddVersionKey "FileVersion"     "${PRODUCT_VERSION}"

; ── 主安装 Section（Tauri CLI 会在此注入文件安装命令） ──
Section "DeEarthX V4" SectionMain
    SectionIn RO
    SetOutPath "$INSTDIR"

    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayName"     "${PRODUCT_NAME}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayIcon"     "$INSTDIR\DeEarthX V4.exe"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "DisplayVersion"  "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "Publisher"       "${PRODUCT_PUBLISHER}"
    WriteRegStr HKLM "${PRODUCT_UNINST_KEY}" "URLInfoAbout"    "${PRODUCT_WEB_SITE}"
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${PRODUCT_UNINST_KEY}" "NoRepair" 1

    WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

; ── 卸载器 ────────────────────────────────────────────────
Section "Uninstall"
    Delete "$INSTDIR\uninstall.exe"
    RMDir /r "$INSTDIR"
    Delete "$SMPROGRAMS\DeEarthX-CE\DeEarthX V4.lnk"
    RMDir "$SMPROGRAMS\DeEarthX-CE"
    Delete "$DESKTOP\DeEarthX V4.lnk"
    DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
SectionEnd