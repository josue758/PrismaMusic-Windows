#ifndef AppVersion
  #error AppVersion must match the published application.
#endif
#ifndef PayloadRoot
  #error PayloadRoot must contain the self-contained win-x64 publish output.
#endif
#ifndef SetupOutput
  #error SetupOutput must name an output directory.
#endif

[Setup]
AppId=PrismaMusic.Windows
AppName=Prisma Music
AppVersion={#AppVersion}
AppPublisher=Prisma Music
AppPublisherURL=https://github.com/josue758/PrismaMusic-Windows
AppUpdatesURL=https://github.com/josue758/PrismaMusic-Windows/releases
DefaultDirName={localappdata}\Programs\PrismaMusic.Windows
DefaultGroupName=Prisma Music
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#SetupOutput}
OutputBaseFilename=Prisma-Music-Windows-{#AppVersion}-Setup-x64
SetupLogging=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Prisma Music.exe
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Prisma Music Setup

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Prisma Music"; Filename: "{app}\Prisma Music.exe"; WorkingDir: "{app}"; Check: not WizardNoIcons

[Run]
Filename: "{app}\Prisma Music.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,Prisma Music}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  LegacyLocation: String;
begin
  if (CurStep = ssPostInstall) and
     RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\PrismaMusic.Windows',
       'InstallLocation', LegacyLocation) and
     (CompareText(RemoveBackslashUnlessRoot(LegacyLocation), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) = 0) then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\PrismaMusic.Windows');
end;
