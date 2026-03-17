; ============================================================
;  TR Vision AI — Inno Setup Script
;  Two Rockets Software Solutions
; ============================================================

#define AppName      "TR Vision AI"
#define AppVersion   "1.0.0"
#define AppPublisher "Two Rockets Software Solutions"
#define AppExe       "TRVisionAI.Desktop.exe"
#define BuildDir     "..\TRVisionAI.Desktop\bin\Release\net8.0-windows"

[Setup]
AppId={{E3A7F1C2-4B8D-4F9A-A1C3-2D5E6F7A8B9C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://www.tworockets.com
DefaultDirName={autopf}\TRVisionAI
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=TRVisionAI_Setup_v{#AppVersion}
SetupIconFile=..\TRVisionAI.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

; Splash / imágenes
WizardImageFile=wizard-image.bmp
WizardSmallImageFile=wizard-small.bmp

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce

[Files]
; --- Ejecutable y DLLs principales ---
Source: "{#BuildDir}\{#AppExe}";                    DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\*.dll";                         DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "{#BuildDir}\*.json";                        DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\runtimes\win-x64\*";            DestDir: "{app}\runtimes\win-x64"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar";         Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar {#AppName}"; Flags: nowait postinstall skipifsilent

; ============================================================
;  Verificación de .NET 8 Desktop Runtime
; ============================================================
[Code]
function DotNet8Installed(): Boolean;
var
  ResultCode: Integer;
begin
  // Busca dotnet.exe y verifica si el runtime 8.x está presente
  Result := Exec('cmd.exe',
    '/C dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 8."',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure InitializeWizard();
begin
  if not DotNet8Installed() then
    MsgBox(
      '.NET 8 Desktop Runtime no está instalado en este equipo.' + #13#10 +
      #13#10 +
      'Descárgalo desde:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10 +
      #13#10 +
      'Instálalo y vuelve a ejecutar este instalador.',
      mbError, MB_OK);
end;
