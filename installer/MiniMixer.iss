; Instalador de Mini-Mixer (Inno Setup 6).
; No se compila a mano: lo genera tools\build-release.ps1, que le pasa AppVersion y SourceExe.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\portable\MiniMixer.exe"
#endif

[Setup]
AppId={{8C1F6E2A-5B3D-4F7A-9E21-6D4B0C3A7F15}
AppName=Mini-Mixer
AppVersion={#AppVersion}
AppVerName=Mini-Mixer {#AppVersion}
AppPublisher=CrhissJMC
AppPublisherURL=https://github.com/CrhissJMC/MiniMix
AppSupportURL=https://github.com/CrhissJMC/MiniMix/blob/main/INSTALAR.md
; Instalación por usuario, sin administrador, en la misma carpeta que usa tools\install.ps1
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\MiniMixer
DisableDirPage=auto
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=MiniMixer-Setup-{#AppVersion}
SetupIconFile=..\src\MiniMixer\app.ico
UninstallDisplayIcon={app}\MiniMixer.exe
UninstallDisplayName=Mini-Mixer
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar Mini-Mixer con Windows (directo a la bandeja)"; GroupDescription: "Opciones:"
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked

[InstallDelete]
; Restos de versiones anteriores (la instalación con tools\install.ps1 dejaba muchas DLL sueltas)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Mini-Mixer"; Filename: "{app}\MiniMixer.exe"
Name: "{userdesktop}\Mini-Mixer"; Filename: "{app}\MiniMixer.exe"; Tasks: desktopicon

[Registry]
; Inicio con Windows (lo mismo que escribe la app; se borra al desinstalar)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MiniMixer"; \
    ValueData: """{app}\MiniMixer.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MiniMixer.exe"; Description: "Abrir Mini-Mixer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\MiniMixer.exe"; Parameters: "--exit"; RunOnceId: "CloseMiniMixer"; Flags: skipifdoesntexist

[Code]
{ Cierra Mini-Mixer limpiamente (MiniMixer.exe --exit) si está abierto, para poder reemplazar el .exe }
procedure CloseRunningApp();
var
  Rc, I: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\MiniMixer.exe');
  if CheckForMutexes('MiniMixer.SingleInstance') and FileExists(Exe) then
  begin
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Rc);
    for I := 1 to 40 do
    begin
      if not CheckForMutexes('MiniMixer.SingleInstance') then Break;
      Sleep(200);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp();
  Result := True;
end;
