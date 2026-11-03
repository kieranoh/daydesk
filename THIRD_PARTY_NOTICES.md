# 📚 참고 프로젝트와 외부 구성 요소

2026-11-04 · Daydesk · `출처` `라이선스`

---

## TimeTree MCP

- 원본: [ehs208/TimeTree-MCP](https://github.com/ehs208/TimeTree-MCP)
- 기준 커밋: [`3f77995f4d074364c6ffe14bb95a9d0c43a7c1d8`](https://github.com/ehs208/TimeTree-MCP/commit/3f77995f4d074364c6ffe14bb95a9d0c43a7c1d8)
- 포함 위치: `vendor/timetree-mcp/`
- 포함 스냅샷의 패키지 버전: `0.7.0`
- 라이선스: [MIT 전문](vendor/timetree-mcp/LICENSE)

기존 연동 환경에서 사용하던 소스 스냅샷을 포함했으며 원본 대비 로컬 수정이 포함될 수 있습니다. 버전 번호만으로 원본 저장소의 동일 태그를 의미하지 않습니다. MCP 프로토콜, TimeTree 일정·반복 회차 처리와 서버 연결을 제공합니다. TypeScript 소스를 직접 빌드하고 설치용 런타임에 묶습니다.

## Todo Mate API 참고 자료

- 프로젝트: [3x-haust/todomate-api](https://github.com/3x-haust/todomate-api)
- 참고 범위: Firebase 인증·데이터 구조와 할 일 관련 필드의 동작 파악
- 포함 범위: 해당 프로젝트의 소스나 패키지를 이 저장소에 복사하거나 런타임으로 사용하지 않습니다.

Daydesk의 Firebase 클라이언트는 C#의 HTTP 클라이언트로 구현되어 있습니다.

## 실행 환경과 패키지

| 구성 요소 | 출처 | 포함 방식 |
| --- | --- | --- |
| CPython embeddable runtime | [python.org](https://www.python.org/) | 공식 Windows x64 ZIP과 라이선스 |
| Node.js | [nodejs.org](https://nodejs.org/) | 공식 Windows 실행파일과 라이선스 |
| tzdata | [PyPI](https://pypi.org/project/tzdata/) | 시간대 데이터와 라이선스 |
| MCP TypeScript SDK, Zod 및 의존성 | `vendor/timetree-mcp/package-lock.json` | 서버 묶음 및 포함 패키지의 라이선스 |
| Inno Setup | [jrsoftware/issrc](https://github.com/jrsoftware/issrc) | 설치파일 빌드 도구 |

설치용 파일을 만들 때 서버 묶음에 포함된 npm 패키지의 라이선스를 수집하고 `licenses/`에 넣습니다. 실제 의존성 버전은 잠금 파일과 빌드 산출물에 따릅니다.

## 서비스 이름과 로고

TimeTree, Todo Mate, PlayMCP 및 Firebase의 이름과 로고에 관한 권리는 각 권리자에게 있습니다. 위젯의 Todo Mate 표시는 해당 서비스의 공개 웹 아이콘을 참고했습니다. 이 프로젝트가 해당 서비스의 공식 제품 또는 승인을 받은 제품임을 의미하지 않습니다.

독자적인 Daydesk 코드에 대해서는 현재 별도의 오픈소스 라이선스를 지정하지 않았습니다. 위 외부 구성 요소에는 각각의 라이선스가 적용됩니다.
