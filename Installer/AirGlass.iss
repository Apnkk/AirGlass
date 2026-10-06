; Script d'installation Inno Setup pour AirGlass
; Produit un installateur Windows classique (menu Démarrer, désinstalleur,
; raccourci bureau optionnel) à partir de l'exe self-contained publié.

#define AppName "AirGlass"
#define AppVersion "1.1.0"
#define AppPublisher "AirGlass"
#define AppExeName "AirGlass.exe"
; Chemin de l'exe single-file produit par « dotnet publish ... PublishSingleFile=true »
#define PublishExe "..\bin\Release\net10.0-windows\win-x64\publish\AirGlass.exe"

[Setup]
AppId={{B4D2F1A6-7C3E-4A91-9F0B-2E6D8C5A3F17}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=AirGlass-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Identité visuelle : images générées par scripts/make-installer-art.ps1
WizardImageFile=assets\wizard-164.bmp,assets\wizard-246.bmp,assets\wizard-328.bmp
WizardSmallImageFile=assets\small-55.bmp,assets\small-110.bmp
SetupIconFile=..\AirGlass.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
DisableWelcomePage=no
ShowLanguageDialog=auto
; AirGlass ouvre des ports réseau (mDNS 5353, AirPlay 7000-7002) :
; une installation par-machine permet d'autoriser le pare-feu proprement.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
french.WelcomeLabel1=Bienvenue dans AirGlass
french.WelcomeLabel2=Diffuse l'écran de ton iPhone, iPad, Mac ou téléphone Android sur ton PC.%n%nL'installation prend moins d'une minute. Ferme les autres applications avant de continuer.
french.FinishedHeadingLabel=AirGlass est installé
french.FinishedLabel=Lance AirGlass et choisis ton mode.%n%nApple : ouvre le Centre de contrôle et choisis « Recopie de l'écran ».%nAndroid : branche le téléphone en USB avec le débogage USB activé.
english.WelcomeLabel1=Welcome to AirGlass
english.WelcomeLabel2=Mirror your iPhone, iPad, Mac or Android phone screen on your PC.%n%nSetup takes under a minute. Close other applications before continuing.
english.FinishedHeadingLabel=AirGlass is installed
english.FinishedLabel=Launch AirGlass and pick your mode.%n%nApple: open Control Center and pick Screen Mirroring.%nAndroid: plug the phone in over USB with USB debugging enabled.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishExe}"; DestDir: "{app}"; Flags: ignoreversion
; Récepteur AirPlay natif (uxplay.exe + DLL GStreamer + plugins), déployé dans
; <app>\uxplay-win\ — c'est le chemin que UxPlayLauncher.ResolvePackageDir()
; cherche en priorité. recursesubdirs embarque lib\gstreamer-1.0\.
Source: "..\external\uxplay-win\*"; DestDir: "{app}\uxplay-win"; Excludes: "_disabled-plugins\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\external\scrcpy-win\*"; DestDir: "{app}\scrcpy-win"; Flags: ignoreversion recursesubdirs createallsubdirs

; Mentions légales : licence GPLv3 d'UxPlay, licences des composants inclus.
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\UxPlay\LICENSE"; DestDir: "{app}\licenses"; DestName: "UxPlay-LICENSE.txt"; Flags: ignoreversion
Source: "..\licenses\UxPlay\playfair-LICENSE.md"; DestDir: "{app}\licenses"; DestName: "playfair-LICENSE.md"; Flags: ignoreversion
Source: "..\licenses\UxPlay\llhttp-LICENSE-MIT"; DestDir: "{app}\licenses"; DestName: "llhttp-LICENSE-MIT"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Ouvre les ports dans le pare-feu Windows pour qu'un iPhone puisse joindre le récepteur.
; Delete first: re-installing or updating must not create duplicate rules.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (mDNS 5353 UDP)"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (AirPlay 7000-7002 TCP)"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (AirPlay 7000-7002 UDP)"""; Flags: runhidden
; Règle obsolète (port 5000) laissée par les anciennes versions.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (RAOP 5000 TCP)"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""AirGlass (mDNS 5353 UDP)"" dir=in action=allow protocol=UDP localport=5353 profile=any"; \
  Flags: runhidden; StatusMsg: "Configuration du pare-feu (mDNS)..."
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""AirGlass (AirPlay 7000-7002 TCP)"" dir=in action=allow protocol=TCP localport=7000-7002 profile=any program=""{app}\uxplay-win\uxplay.exe"""; \
  Flags: runhidden; StatusMsg: "Configuration du pare-feu (AirPlay TCP)..."
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""AirGlass (AirPlay 7000-7002 UDP)"" dir=in action=allow protocol=UDP localport=7000-7002 profile=any program=""{app}\uxplay-win\uxplay.exe"""; \
  Flags: runhidden; StatusMsg: "Configuration du pare-feu (AirPlay UDP)..."
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Retire les règles de pare-feu à la désinstallation.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (mDNS 5353 UDP)"""; Flags: runhidden; RunOnceId: "DelFwMdns"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (AirPlay 7000-7002 TCP)"""; Flags: runhidden; RunOnceId: "DelFwAirplayTcp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (AirPlay 7000-7002 UDP)"""; Flags: runhidden; RunOnceId: "DelFwAirplayUdp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirGlass (RAOP 5000 TCP)"""; Flags: runhidden; RunOnceId: "DelFwRaop"

[Code]
// Ferme AirGlass et uxplay.exe avant d'installer/désinstaller (fichiers verrouillés sinon).
procedure KillAirGlass;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM AirGlass.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM uxplay.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillAirGlass;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillAirGlass;
end;
