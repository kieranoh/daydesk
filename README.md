# 📅 Daydesk

2026-11-04 · Daydesk · `Windows` `TimeTree + Todo Mate`

---

TimeTree 일정과 Todo Mate 할 일을 한 화면에 표시하는 Windows 바탕화면 위젯입니다.
설치파일에는 실행 환경이 포함되어 있어 Codex·Python·Node를 따로 설치할 필요가 없습니다.

**[Windows 설치파일 다운로드](https://github.com/kieranoh/daydesk/releases/latest)** · [변경 이력](CHANGELOG.md) · [연동 출처](THIRD_PARTY_NOTICES.md)

> 💡 **처음 사용하기** — 설치 → 앱 실행 → 설정의 **계정 연결**에서 TimeTree와 Todo Mate에 로그인하세요. PlayMCP는 선택 사항이며 나중에 연결해도 됩니다.

## 목차

- [주요 기능](#주요-기능)
- [설치](#설치)
- [계정 연결](#계정-연결)
- [사용 방법](#사용-방법)
- [CLI](#cli)
- [개인정보와 저장 위치](#개인정보와-저장-위치)
- [소스에서 빌드](#소스에서-빌드)
- [참고한 프로젝트와 한계](#참고한-프로젝트와-한계)

## 주요 기능

| 영역 | 지원 기능 |
| --- | --- |
| TimeTree | 월간 달력, 원래 일정 색상과 라벨, 일정 추가·수정·삭제, 선택한 반복 회차 편집 |
| Todo Mate | 날짜별 할 일, 카테고리 선택, 추가·수정·삭제·완료·알림 |
| 화면 | 배경 투명도 조절, 할 일 영역 폭 조절, 모든 테두리와 모서리에서 크기 조절 |
| 동기화 | 암호화한 로컬 캐시, 수동 또는 15·30·60분 동기화, 직접 변경 시 갱신 |
| 설정 | 추천 설정 복원, 자동 실행, 숨길 카테고리, 계정 연결 건너뛰기 |
| CLI | 위젯 제어, TimeTree 일정과 Todo Mate 할 일 관리, JSON 출력 |

TimeTree는 기본으로 기준 월의 앞뒤 6개월을 포함한 13개월을 캐시합니다. 이전에 저장한 다른 달도 다시 사용할 수 있습니다. 캐시가 있으면 화면 이동 시 먼저 표시하고, 동기화 시 변경된 내용을 갱신합니다.

## 설치

**Windows 10 1809 이상 또는 Windows 11의 x64 환경**을 대상으로 합니다. .NET Framework 4.6.2 이상이 필요합니다.

1. [Releases](https://github.com/kieranoh/daydesk/releases/latest)에서 `Daydesk-Setup-1.4.0-x64.exe`를 받습니다.
2. 설치 경로를 선택합니다.
3. 바탕화면 바로가기, Windows 로그인 시 자동 실행, CLI를 PATH에 추가할지 선택합니다.
4. 설치를 마치고 Daydesk를 실행합니다.

설치파일은 전자서명되어 있지 않습니다. 같은 릴리스의 `.sha256` 파일로 다운로드한 파일을 확인할 수 있습니다.

```powershell
Get-FileHash .\Daydesk-Setup-1.4.0-x64.exe -Algorithm SHA256
```

### 업데이트

같은 PC와 Windows 계정에서 새 설치파일을 실행해 덮어 설치하세요. 계정 정보·설정·캐시는 설치 폴더 바깥에 저장하므로 일반 업데이트에서 유지됩니다. 다른 PC에서는 다시 로그인해야 합니다.

이전 개발용 버전에서 처음 넘어오는 경우에는 설치형 앱의 **설정 → 계정 연결**에서 TimeTree와 선택 사항인 PlayMCP를 한 번 연결해야 할 수 있습니다.

## 계정 연결

### TimeTree

1. **설정 → 계정 연결**에서 TimeTree 이메일과 비밀번호를 입력합니다.
2. **로그인·연결**을 누릅니다.
3. 표시할 캘린더를 선택합니다.

TimeTree MCP와 필요한 실행 환경은 설치파일에 함께 들어 있습니다. 별도 MCP 등록이나 API 키 입력은 필요하지 않습니다.

### Todo Mate

1. Todo Mate에서 사용하는 **이메일·비밀번호 계정**을 입력합니다.
2. **로그인·연결**을 누릅니다.

Todo Mate가 사용하는 Firebase에 본인 계정으로 연결합니다. 사용자가 Firebase 프로젝트를 만들거나 관리 키를 발급할 필요는 없습니다. 비밀번호는 저장하지 않고 로그인 토큰을 암호화해 보관합니다.

### PlayMCP 보조 연결 — 선택

평소에는 Firebase를 사용합니다. Firebase의 조회 요청이 `429 RESOURCE_EXHAUSTED`로 제한될 때만 PlayMCP 보조 조회를 사용합니다. 인증 오류나 권한 오류를 보조 연결로 우회하지 않으며, 추가·수정·삭제·완료는 Firebase로 처리합니다.

1. 앱에서 **PlayMCP 열기**를 누르고 카카오 계정으로 로그인합니다.
2. **투두메이트**를 찾아 도구함에 추가하고 위젯과 같은 Todo Mate 계정으로 인증합니다.
3. 도구함에서 **OpenClaw와 연결** → 데이터 제공 동의 → **연결 프롬프트 생성**을 선택합니다.
4. 생성된 프롬프트 전체 또는 일회용 키(OTT)만 위젯의 입력칸에 붙여 넣고 **연결**을 누릅니다. JSON·줄바꿈·Markdown이 섞여 있어도 키만 추출하며, 프롬프트의 명령문은 실행하지 않습니다.

발급 후 10분 안에 사용하고, 만료되면 새로 생성하세요. 위젯이 연결 프롬프트를 처리하므로 OpenClaw를 따로 설치할 필요는 없습니다. [카카오 공식 연결 안내](https://www.kakaocorp.com/page/detail/12012)

> ⚠️ **사용량 제한** — Firebase와 PlayMCP에는 각각 서비스 제한이 있습니다. 보조 연결은 무제한 사용을 보장하지 않습니다.

### 나중에 연결하기

**건너뛰기 · 나중에 연결**을 누르면 다음 실행부터 연결 창을 자동으로 띄우지 않습니다. 이후 **설정 → 계정 연결**에서 이어서 연결할 수 있습니다. 두 기본 계정을 연결하기 전에는 위젯 배경이 불투명하며, 연결 후 저장된 투명도가 적용됩니다.

## 사용 방법

- **날짜 선택:** 달력의 날짜를 누르면 오른쪽 할 일 목록도 그 날짜로 이동합니다.
- **TimeTree:** 상단의 **일정 추가**를 누르거나 기존 일정을 눌러 편집합니다. 실제 캘린더의 라벨 이름과 색상으로 선택할 수 있습니다. 삭제는 확인 후 실행됩니다.
- **반복 일정:** 선택한 회차만 수정·삭제합니다. 반복 회차의 종일 여부 변경은 원본 TimeTree에서 하세요.
- **Todo Mate 추가:** 카테고리의 **+**를 누르면 목록 아래에 투명한 입력 줄과 얇은 밑줄만 생깁니다. 수정할 때도 같은 밑줄로 입력 중임을 표시합니다. Enter로 추가한 뒤 계속 입력할 수 있고, Shift+Enter는 줄바꿈, Esc는 취소입니다. 다른 곳을 클릭해도 저장됩니다.
- **내용 수정:** 할 일을 더블클릭하거나 키보드로 선택한 뒤 F2를 누르면 기존 배경·글자·줄 위치 그대로 텍스트만 수정합니다. Enter로 저장하고 Esc로 취소합니다. 완료 상태와 기존 순서는 유지합니다.
- **달력 월 이동:** 이전·다음 달을 둘러보아도 투두메이트의 날짜와 목록은 유지됩니다. 달력의 특정 날짜를 직접 누르면 해당 날짜의 할 일로 바뀝니다.
- **투두 사이드 패널:** **전체 카테고리** 옆의 **투두** 토글을 끄면 달력이 전체 너비를 사용합니다. 토글은 계속 표시되며, 다시 켜면 기존 폭과 선택 날짜로 돌아옵니다. 표시 상태는 재실행 후에도 유지됩니다. CLI에서는 `daydesk settings --todo-visible off` 또는 `on`으로 전환합니다.
- **좌우 정렬:** Todo Mate 로고·도구는 박스 위에 표시하고, 할 일 박스의 윗선과 달력 날짜 격자의 윗선을 맞춥니다.
- **TimeTree 추가 라벨:** 마지막으로 추가에 성공한 라벨을 계정·캘린더별 암호화 캐시에 기억하고 다음 일정 추가 시 자동 선택합니다. 기존 일정 편집 시에는 원래 라벨을 유지합니다.
- **미루기:** **··· → 내일로 미루기**를 누르면 다음 날로 이동합니다. 오늘이 아닌 날짜에는 **다음 날로 미루기**로 표시하며 알림도 하루 뒤로 옮깁니다. 날짜·카테고리·알림은 같은 메뉴의 별도 항목에서 설정합니다. 완료 처리는 목록의 체크박스로 합니다.
- **혼잡한 주:** **전체 보기**로 그 주의 모든 일정을 펼칩니다.
- **카테고리:** 상단 스위치로 전체 카테고리와 당일 할 일이 있는 카테고리를 전환합니다. 설정에서 제외한 카테고리는 두 모드 모두 숨깁니다.
- **크기 조절:** 바깥 테두리와 네 모서리를 드래그합니다. 달력과 할 일 사이의 경계는 할 일 영역의 폭을 조절합니다.
- **가장 뒤로 고정:** 상단의 **뒤로 고정** 스위치 또는 **설정 → 화면**에서 켜고 끕니다. 작업 표시줄이나 Alt+Tab에서 위젯을 불러오면 앞으로 나오면서 뒤로 고정 토글이 자동으로 꺼집니다. 이후 일반 창처럼 반복해서 불러올 수 있으며, 바탕화면 뒤로 붙이려면 토글을 다시 켜세요. 항상 위에 표시와는 동시에 켤 수 없습니다.
- **투명 배경:** 상단 **투명** 토글을 끄면 배경이 불투명해집니다. 다시 켜면 마지막 투명도로 복원하며, 이 값은 재실행 후에도 기억합니다. 글자와 일정 색은 계속 선명하게 유지됩니다.
- **빠른 일정 편집:** 저장된 일정 정보로 편집창을 먼저 엽니다. 저장할 때 서버의 원본과 비교하며, 다른 기기에서 변경된 일정은 새로고침 후 다시 편집해야 합니다.
- **즉시 동기화:** **새로고침** 또는 `F5`를 누릅니다. TimeTree 팝업에서는 `Ctrl+Enter`로 저장할 수 있습니다. Todo Mate 입력 중에는 입력을 마친 뒤 새로고침합니다.

저장 응답이 끊기면 같은 작업을 바로 반복하지 말고 새로고침으로 반영 여부를 먼저 확인하세요.

## CLI

설치할 때 PATH 추가를 선택했다면 **새 터미널**에서 실행합니다. ID와 카테고리 이름은 본인 계정에서 조회한 값으로 바꾸세요. 모든 명령에 `--json`을 붙일 수 있습니다.

```powershell
daydesk --help
daydesk status
daydesk open
daydesk refresh
daydesk settings --transparency 65 --sync-minutes 30 --todo-width 320
daydesk settings --recommended
daydesk settings --pin-back on
daydesk settings --pin-back off
daydesk autostart on

daydesk timetree calendars --json
daydesk timetree events --date 2026-11-04 --view month --json
daydesk timetree add --title "예시 회의" --start 2026-11-05T10:00 --end 2026-11-05T11:00 --label 1
daydesk timetree edit --id EVENT_UUID --title "변경한 제목"
daydesk timetree delete --id EVENT_UUID --yes

daydesk todo goals --json
daydesk todo list --date 2026-11-04 --json
daydesk todo add --goal CATEGORY_ID --content "예시 할 일" --date 2026-11-04
daydesk todo edit --id TASK_ID --content "변경한 내용"
daydesk todo done --id TASK_ID
daydesk todo done --id TASK_ID --undo
daydesk todo remind --id TASK_ID --at 2026-11-05T09:00
daydesk todo delete --id TASK_ID
```

TimeTree의 종일 일정은 `--all-day --start 2026-11-05 --end 2026-11-05`처럼 입력하며 종료 날짜를 포함합니다. 반복 회차 수정·삭제에는 조회 결과의 `start` 값을 `--occurrence`로 전달합니다. 시간 입력은 한국 시간 기준입니다.

<details>
<summary>개발 환경에서 CLI 실행하기</summary>

네이티브 실행파일과 TimeTree MCP를 빌드한 뒤 프로젝트 루트에서 실행합니다.

```powershell
python -m pip install --no-deps -e .
python daydesk.py --help
```

</details>

## 개인정보와 저장 위치

계정 정보·일정·할 일은 기본적으로 아래 경로에 저장되며 저장소와 설치파일에는 포함하지 않습니다.

```text
%LOCALAPPDATA%\TimeTreeWidget\
  settings.json          화면·동기화 설정
  todo-panel.json        카테고리 표시 설정
  timetree-auth.bin      암호화한 TimeTree 계정 정보
  firebase\             암호화한 Todo Mate 로그인 토큰
  mcp\                  선택 사항인 PlayMCP 로그인 토큰
  cache\                암호화한 일정·할 일 캐시
```

| 항목 | 보관 방식 |
| --- | --- |
| TimeTree 이메일·비밀번호 | 자동 재연결을 위해 Windows 사용자 계정에 묶인 DPAPI 암호화로 저장 |
| Todo Mate 비밀번호 | 저장하지 않음 |
| Todo Mate·PlayMCP 토큰 | DPAPI로 암호화해 저장 |
| 일정·할 일 캐시 | DPAPI로 암호화해 저장 |
| 화면·카테고리 설정 | 로컬 JSON으로 저장 |

DPAPI는 로그인한 Windows 사용자 계정에 연결됩니다. 암호화는 같은 Windows 사용자 권한으로 실행되는 다른 프로그램까지 차단하는 보안 경계는 아닙니다.

`firebase-public.json`은 Todo Mate 웹 클라이언트의 **공개 프로젝트 설정**입니다. 사용자 비밀번호·로그인 토큰·Firebase 관리자 서비스 계정 키가 아닙니다. 데이터 접근에는 사용자 인증과 서버의 보안 규칙이 적용됩니다. [Firebase API 키 안내](https://firebase.google.com/docs/projects/api-keys)

자세한 제외 원칙과 제보 안내는 [SECURITY.md](SECURITY.md)를 확인하세요. 실제 일정, 토큰, 로그인 화면이 담긴 캡처를 이슈에 올리지 마세요.

## 소스에서 빌드

필요 환경: Windows x64, .NET Framework 4.6.2 이상, Python 3.11 이상, Node 22 이상과 npm. C# 컴파일러는 .NET Framework 설치 경로의 `csc.exe`를 사용합니다.

```powershell
git clone https://github.com/kieranoh/daydesk.git
cd daydesk

# 필요한 서버와 의존성은 저장소 내부에서 빌드합니다.
cd vendor\timetree-mcp
npm ci
npm run build
cd ..\..

# 네이티브 위젯과 CLI를 프로젝트 루트에 빌드합니다.
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
python -m unittest discover -v
.\TimeTreeWidget.exe
```

TimeTree 계정 연결도 앱의 설정에서 진행합니다. 개인 Codex 설정 파일이나 별도의 Todo Mate 프로젝트는 필요하지 않습니다.

### 설치파일 만들기

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\build_release.ps1
```

빌드 도구는 공식 배포처에서 Python·Node 런타임과 Inno Setup을 받아 `build/`에 준비합니다. 생성되는 파일은 `dist/Daydesk-Setup-1.4.0-x64.exe`입니다. 빌드 전에 커밋된 기본 설정을 사용하며 빌드한 사람의 로그인 정보나 일정 캐시는 복사하지 않습니다.

<details>
<summary>코드 구성과 테스트</summary>

| 위치 | 역할 |
| --- | --- |
| `CalendarWindow.cs`, `WidgetTheme.cs` | WPF 달력과 공통 UI |
| `TodoPanel.cs`, `FirebaseTodoDialog.cs`, `TreeEventDialog.cs` | 할 일 목록과 작성·편집 창 |
| `FirebaseMateClient.cs`, `shared/LegacyMcpClient.cs` | Firebase 기본 연결과 PlayMCP 보조 연결 |
| `calendar_data.py`, `backend.py`, `timetree_write.py` | TimeTree 연동·캐시·일정 변경 |
| `daydesk.py`, `DaydeskCli.cs` | CLI와 위젯 통신 |
| `shared/` | 기존 위젯에서 분리한 공통 코드 |
| `vendor/timetree-mcp/` | 출처와 라이선스를 보존한 TimeTree MCP 소스 |
| `packaging/` | 런타임 준비, 개인정보 검사, Windows 설치파일 빌드 |
| `qa/fixture.json`, `test_*.py` | 가상 데이터와 자동 테스트 |

자동 테스트는 암호화 캐시, CLI 인자, 일정 변경 요청, 반복 회차 범위, 중복 저장 방지 등을 검증합니다. 실제 계정에서 일정을 추가·삭제하는 테스트는 자동 실행하지 않습니다.

```powershell
.\TimeTreeWidget.exe --self-test qa\fixture.json build\self-test.json
python packaging\audit_payload.py
python tools\check_public_tree.py
```

네이티브 자체 검증은 가상 데이터를 사용합니다. `audit_payload.py`는 설치용 묶음을 만든 뒤 실행하세요.

</details>

## 참고한 프로젝트와 한계

- **[ehs208/TimeTree-MCP](https://github.com/ehs208/TimeTree-MCP)**: TimeTree 연동에 사용하는 비공식 MCP 구현입니다. 이 저장소에 라이선스와 함께 포함했습니다.
- **[3x-haust/todomate-api](https://github.com/3x-haust/todomate-api)**: Todo Mate의 Firebase 데이터 구조와 API 동작을 파악하는 데 참고했습니다. 해당 프로젝트를 런타임 의존성으로 사용하지 않습니다.
- **[PlayMCP](https://playmcp.kakao.com/)**: 선택 사항인 Todo Mate 보조 조회 연결입니다.

이 프로젝트는 TimeTree·Todo Mate·카카오·Firebase의 공식 앱이나 공식 지원 통합이 아닙니다. 서비스의 API·인증 방식·정책이 바뀌면 연결이 동작하지 않을 수 있습니다. 외부 코드의 버전·라이선스 및 로고 관련 사항은 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 정리했습니다.
