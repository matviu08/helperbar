; Build with Build-Installer.ps1 and Inno Setup 7.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifdef InstallerTest
  #define ProductName "HelperBar Installer QA"
  #define ProductId "{{377B6959-994F-4E0F-982A-A441808379BA}"
  #define OutputName "HelperBar-Setup-QA-x64"
#else
  #define ProductName "HelperBar"
  #define ProductId "{{D87FEAF1-AE71-4A34-9894-EA87F98A5F51}"
  #define OutputName "HelperBar-Setup-x64"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=HelperBar
AppPublisherURL=https://github.com/matviu08/helperbar
AppSupportURL=https://github.com/matviu08/helperbar/issues
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
WizardStyle=modern dark
SetupIconFile=..\Assets\HelperBar.ico
UninstallDisplayIcon={app}\HelperBar.exe
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
DisableWelcomePage=no

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ukrainian.CloseFirst=HelperBar ще працює. Закрий його через значок біля годинника й повтори спробу. Дані залишаться збереженими.
english.CloseFirst=HelperBar is still running. Close it from the system tray and try again. Your data will be preserved.
ukrainian.NewerInstalled=У цій папці вже є новіша версія HelperBar. Скористайся новішим інсталятором.
english.NewerInstalled=A newer HelperBar version is already installed in this folder. Please use a newer installer.
ukrainian.KeepData=Налаштування, нотатки та справи зберігаються окремо в AppData і залишаться після видалення програми.
english.KeepData=Settings, notes and tasks are stored separately in AppData and will remain after uninstalling.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "qa,data,*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\HelperBar.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\HelperBar.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\HelperBar.exe"; Description: "{cm:LaunchProgram,HelperBar}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'HelperBar.Personal';
  IslandMutex = 'Local\HelperBar.Personal.Island';
var
  RetargetStartup: Boolean;

function StartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\HelperBar.exe') + '" --startup';
end;

function StopIsland: Boolean;
var
  Window: HWND;
  Attempt: Integer;
begin
  Result := True;
  if not CheckForMutexes(IslandMutex) then Exit;
  Log('Requesting graceful HelperBar shutdown.');
  Window := FindWindowByWindowName('HelperBar — твій острів');
  if Window <> 0 then PostMessage(Window, $0010, 0, 0);
  for Attempt := 1 to 80 do begin
    if not CheckForMutexes(IslandMutex) then begin
      Log('HelperBar closed gracefully.');
      Exit;
    end;
    Sleep(100);
  end;
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, PackageVersion: Int64;
#ifndef InstallerTest
  Command, ExecutablePath, ExecutableVersion: String;
#endif
begin
  Result := '';
  if GetPackedVersion(ExpandConstant('{app}\HelperBar.exe'), InstalledVersion) and
     StrToVersion('{#AppVersion}', PackageVersion) then
    if ComparePackedVersion(InstalledVersion, PackageVersion) > 0 then begin
      Result := CustomMessage('NewerInstalled');
      Exit;
    end;
  if not StopIsland then begin
    Result := CustomMessage('CloseFirst');
    Exit;
  end;
  RetargetStartup := False;
#ifndef InstallerTest
  if RegQueryStringValue(HKCU, RunKey, RunValue, Command) then begin
    if (Copy(Command, 1, 1) = '"') and
       (Copy(Command, Length(Command) - 10, 11) = '" --startup') then begin
      ExecutablePath := Copy(Command, 2, Length(Command) - 12);
      RetargetStartup := SameText(ExtractFileName(ExecutablePath), 'HelperBar.exe') and
        FileExists(AddBackslash(ExtractFileDir(ExecutablePath)) + 'HelperBar.dll') and
        GetVersionNumbersString(ExecutablePath, ExecutableVersion);
    end;
  end;
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and RetargetStartup then
    if not RegWriteStringValue(HKCU, RunKey, RunValue, StartupCommand) then
      Log('Could not retarget the existing HelperBar startup entry.');
end;

function InitializeUninstall: Boolean;
begin
  Result := StopIsland;
  if not Result then SuppressibleMsgBox(CustomMessage('CloseFirst'), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
#ifndef InstallerTest
var
  Command: String;
#endif
begin
#ifndef InstallerTest
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, RunValue, Command) then
      if SameText(Command, StartupCommand) then RegDeleteValue(HKCU, RunKey, RunValue);
#endif
  { No user data directory is included in the uninstall log or deleted here. }
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + CustomMessage('KeepData');
end;
