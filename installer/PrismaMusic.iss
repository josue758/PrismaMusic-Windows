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
CloseApplications=yes
RestartApplications=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
SetupIconFile=..\Assets\PrismaMusic.ico
WizardSmallImageFile=..\Assets\PrismaMusic.png
WizardSmallImageFileDynamicDark=..\Assets\PrismaMusic.png
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
Name: "{autoprograms}\Prisma Music"; Filename: "{app}\Prisma Music.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Prisma Music.exe"; IconIndex: 0; Check: not WizardNoIcons

[Run]
Filename: "{app}\Prisma Music.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,Prisma Music}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Prisma Music.exe"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent; Check: IsPrismaUpdate

[Code]
function OpenProcess(Access: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function EnumWindows(Callback: NativeInt; ProcessId: NativeInt): Boolean;
  external 'EnumWindows@user32.dll stdcall';
function GetWindowThreadProcessId(Window: HWND; var ProcessId: LongWord): LongWord;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function GetWindow(Window: HWND; Command: LongWord): HWND;
  external 'GetWindow@user32.dll stdcall';
function IsWindowVisible(Window: HWND): Boolean;
  external 'IsWindowVisible@user32.dll stdcall';
function PostMessage(Window: HWND; MessageId: LongWord; WParam: NativeUInt; LParam: NativeInt): Boolean;
  external 'PostMessageW@user32.dll stdcall';

function IsPrismaUpdate: Boolean;
begin
  Result := StrToIntDef(ExpandConstant('{param:PRISMAUPDATEPID|0}'), 0) > 0;
end;

function RequestPrismaClose(Window: HWND; ProcessId: NativeInt): Boolean;
var
  OwnerProcess: LongWord;
begin
  Result := True;
  GetWindowThreadProcessId(Window, OwnerProcess);
  { WM_CLOSE on visible, unowned windows allows Prisma to save its playback session. }
  if (OwnerProcess = LongWord(ProcessId)) and (GetWindow(Window, 4) = 0) and IsWindowVisible(Window) then
    PostMessage(Window, $0010, 0, 0);
end;

function CloseInstalledPrisma: Boolean;
var
  Locator, Service, Processes, Entry, ImagePath: Variant;
  InstalledExe, RunningExe: String;
  I: Integer;
  ProcessId: LongWord;
  ProcessHandle: THandle;
  Callback: NativeInt;
  VerifyOnly: Boolean;
begin
  Result := False;
  InstalledExe := ExpandConstant('{app}\Prisma Music.exe');
  if not FileExists(InstalledExe) then
  begin
    Result := True;
    Exit;
  end;
  Callback := CreateCallback(@RequestPrismaClose);
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\cimv2');
    VerifyOnly := False;
    repeat
      Processes := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''Prisma Music.exe''');
      for I := 0 to Processes.Count - 1 do
      begin
        Entry := Processes.ItemIndex(I);
        ImagePath := Entry.ExecutablePath;
        RunningExe := '';
        if not VarIsNull(ImagePath) then
          RunningExe := ImagePath;
        if CompareText(RunningExe, InstalledExe) = 0 then
        begin
          ProcessId := Entry.ProcessId;
          ProcessHandle := OpenProcess($00100000, False, ProcessId);
          if ProcessHandle <> 0 then
          begin
            try
              if VerifyOnly then
              begin
                if WaitForSingleObject(ProcessHandle, 0) <> 0 then Exit;
              end
              else
              begin
                Log(Format('Closing Prisma Music process %d in the target installation.', [ProcessId]));
                EnumWindows(Callback, ProcessId);
                if WaitForSingleObject(ProcessHandle, 15000) <> 0 then Exit;
              end;
            finally
              CloseHandle(ProcessHandle);
            end;
          end
          else if VerifyOnly then Exit;
        end;
      end;
      if VerifyOnly then Break;
      VerifyOnly := True;
    until False;
    Result := True;
  except
    Log('Could not check running Prisma Music processes: ' + GetExceptionMessage);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessHandle: THandle;
begin
  Result := '';
  if IsPrismaUpdate then
  begin
    ProcessHandle := OpenProcess($00100000, False, StrToIntDef(ExpandConstant('{param:PRISMAUPDATEPID|0}'), 0));
    if ProcessHandle <> 0 then
    begin
      if WaitForSingleObject(ProcessHandle, 30000) <> 0 then
        Result := FmtMessage(CustomMessage('CloseForUpdate'), ['Prisma Music']);
      CloseHandle(ProcessHandle);
    end;
  end;
  { Older in-app updaters pass /NOCLOSEAPPLICATIONS. Check all copies in this
    installation before touching any files, regardless of that option. }
  if (Result = '') and not CloseInstalledPrisma then
    Result := FmtMessage(CustomMessage('CloseForUpdate'), ['Prisma Music']);
end;

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

[CustomMessages]
spanish.CloseForUpdate=Cierra %1 y vuelve a intentar la actualización.
english.CloseForUpdate=Close %1 and try the update again.
brazilianportuguese.CloseForUpdate=Feche o %1 e tente atualizar novamente.
