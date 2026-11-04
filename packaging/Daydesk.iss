#define AppVersion "1.3.2"
#define Payload "..\build\bundle"

[Setup]
AppId={{A724BD17-93B5-4B8A-8CA6-16A451FB3C40}
AppName=Daydesk
AppVersion={#AppVersion}
AppPublisher=Daydesk
DefaultDirName={localappdata}\Programs\Daydesk
DefaultGroupName=Daydesk
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
InfoBeforeFile=setup-guide.txt
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Daydesk-Setup-{#AppVersion}-x64
SetupIconFile=..\calendar.ico
UninstallDisplayIcon={app}\TimeTreeWidget.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\TimeTreeDesktopWidgetNative
VersionInfoVersion=1.3.2.0
UninstallDisplayName=Daydesk · TimeTree + Todo Mate

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; GroupDescription: "추가 설정:"; Flags: checkedonce
Name: "autostart"; Description: "Windows 로그인 시 자동 실행"; GroupDescription: "추가 설정:"; Flags: checkedonce
Name: "addtopath"; Description: "명령줄에서 daydesk 사용 (새 터미널부터 적용)"; GroupDescription: "추가 설정:"; Flags: checkedonce

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Daydesk"; Filename: "{app}\TimeTreeWidget.exe"; WorkingDir: "{app}"
Name: "{group}\Daydesk 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Daydesk"; Filename: "{app}\TimeTreeWidget.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\TimeTree Calendar"; Filename: "{app}\TimeTreeWidget.exe"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\TimeTreeWidget.exe"; Description: "Daydesk 실행 및 계정 연결"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 394802);
  if not Result then MsgBox('Windows의 .NET Framework 4.6.2 이상이 필요합니다. Windows 업데이트 후 다시 설치해 주세요.', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var OldPath, AppPath: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then begin
    AppPath := ExpandConstant('{app}');
    RegQueryStringValue(HKCU, 'Environment', 'Path', OldPath);
    if Pos(';' + Lowercase(AppPath) + ';', ';' + Lowercase(OldPath) + ';') = 0 then
      RegWriteExpandStringValue(HKCU, 'Environment', 'Path', AppPath + ';' + OldPath);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var OldPath, Remaining, NewPath, Part, AppPath: String; Separator: Integer;
begin
  if CurUninstallStep <> usPostUninstall then exit;
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', OldPath) then exit;
  AppPath := ExpandConstant('{app}'); Remaining := OldPath; NewPath := '';
  while Remaining <> '' do begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then begin Part := Remaining; Remaining := ''; end
    else begin Part := Copy(Remaining, 1, Separator - 1); Delete(Remaining, 1, Separator); end;
    if CompareText(Trim(Part), AppPath) <> 0 then begin
      if NewPath <> '' then NewPath := NewPath + ';';
      NewPath := NewPath + Part;
    end;
  end;
  if NewPath <> OldPath then RegWriteExpandStringValue(HKCU, 'Environment', 'Path', NewPath);
end;
