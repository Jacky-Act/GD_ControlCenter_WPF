; --- 1. 全局宏定义 ---
#define MyAppName "GD控制中心"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "GD仪器开发团队"
#define MyAppExeName "GD_ControlCenter_WPF.exe"

[Setup]
AppId={{12345678-ABCD-1234-ABCD-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes

; 【无缝更新配置】：覆盖安装时自动静默关闭旧程序，不弹卸载和报错提示
CloseApplications=yes

OutputDir=.\Output
OutputBaseFilename=GD_ControlCenter_Setup_v{#MyAppVersion}

SetupIconFile=Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: unchecked

[Files]
; 核心文件路径 (指向你最新的 Publish 目录)
Source: "bin\Release\net6.0-windows\publish\win-x86\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; 【核心修改】：打包时排除掉 config.json。
; 这样安装目录里绝对没有配置文件的影子，程序运行时会严格去 AppData 里找。
; 升级覆盖时，只替换代码，用户的 AppData 配置文件完好无损。
Source: "bin\Release\net6.0-windows\publish\win-x86\*"; DestDir: "{app}"; Excludes: "config.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent