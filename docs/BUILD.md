# 로케테스트 빌드 (Windows)

빌드 실행은 사용자가 한다. 이 문서는 빌드 전에 맞출 설정과 빌드 후 현장 PC에서 확인할 항목이다.
현재 프로젝트 설정 값은 C3 작업 시점(`ProjectSettings/ProjectSettings.asset`)에서 읽은 것이다.

---

## 1. 빌드 전 씬 준비

- [ ] `docs/SETUP.md`의 메뉴를 전부 실행하고 씬 저장:
  `Mydongari/Setup Versus Scene` → `Mydongari/Add Missing Mech Components` → `Mydongari/Tuning/Create Default Tuning And Assign`
- [ ] `GameManager` → **Versus Mode 켜짐** (끄면 1인 + 더미 빌드가 된다)
- [ ] 스폰 포인트 두 개가 서로 마주 보게 회전돼 있음 (SETUP 12)
- [ ] `Assets/Resources/BernardTuning.asset` 값 최종 확인. **플레이 모드에서 바꾼 값은 에셋에 남아 있다** — 의도한 값인지 git diff로 확인 후 커밋
- [ ] `GameManager` 오브젝트에 `HitStop`, `CombatFeedback`(효과음 클립), `AppRuntime`, `VersusInputPairing`이 있음
- [ ] 에디터 플레이로 한 판(3판 2선승) 끝까지 돌려 Console에 빨간 오류 없음

## 2. Build Profiles (File → Build Profiles)

- [ ] 플랫폼 **Windows** (활성화돼 있지 않으면 Switch Platform)
- [ ] Scene List: `Assets/Scenes/SampleScene.unity` 하나만 체크 (현재 설정 그대로)
- [ ] Architecture: **Intel 64-bit**
- [ ] **Development Build 끔** (켜면 화면 구석에 개발 콘솔이 뜬다). 현장에서 오류 추적이 꼭 필요하면 켜되 Autoconnect Profiler는 끔
- [ ] 출력 폴더: **쓰기 권한이 있는 곳** (예: `D:\Mydongari_Build\`). `Program Files` 아래에 두면 `Logs/PlayLogs` 폴더를 못 만들어 로그가 저장되지 않는다. 프로젝트 안 `Builds/` 폴더도 된다(.gitignore 대상)

## 3. Player Settings (Project Settings → Player)

| 항목 | 권장 | 현재 | 이유 |
| --- | --- | --- | --- |
| Company Name / Product Name | 팀 이름 / Mydongari | DefaultCompany / Mydongari | Player.log 경로가 이 이름으로 정해진다 |
| Version | 0.1.0 등 | 0.1.0 | 로그와 빌드를 맞춰 보기 위함 |
| Resolution → Fullscreen Mode | **Fullscreen Window** | Fullscreen Window | Alt+Tab이 빠르고 안전 |
| Default Is Native Resolution | **켬** | 켬 | 모니터 해상도 그대로 |
| Run In Background | **켬** | **끔** | 포커스를 잃어도(다른 창 클릭) 게임이 멈추지 않게 |
| Visible In Background | 켬 | 켬 | |
| Resizable Window | 끔 | 끔 | |
| Allow Fullscreen Switch (Alt+Enter) | 켬 | 켬 | 현장에서 창 모드 전환이 필요할 때 |
| Force Single Instance | **켬** | 끔 | 실수로 두 번 실행하면 패드를 두 프로세스가 나눠 잡는다 |
| Use Player Log | 켬 | 켬 | 오류 추적용 |
| Other → Active Input Handling | **Input System Package (New)** | 새 입력 시스템 | 코드가 새 입력 시스템만 쓴다 |
| Other → Scripting Backend | Mono (빌드 빠름) | — | IL2CPP도 되지만 빌드가 오래 걸린다 |

`AppRuntime`은 빌드 시작 시 **모니터 기본 해상도의 Fullscreen Window로 강제**한다(`Force Fullscreen On Start`). 예전에 창 모드로 실행해 레지스트리에 남은 설정을 덮어쓰기 위함이다. 창 모드 테스트가 필요하면 GameManager → `AppRuntime` → 끄고 빌드.

## 4. Input System 설정 (Project Settings → Input System Package)

- [ ] **Background Behavior**: `Ignore Focus` 권장 — 창이 포커스를 잃어도 패드 입력을 계속 받는다 (Run In Background와 함께). 기본값(Reset And Disable Non Background Devices)이면 포커스를 잃는 순간 입력이 끊길 수 있다.
- [ ] 위 설정을 바꿨다면 Input System 설정 에셋 변경분을 커밋

## 5. 빌드 후 확인 (현장 PC, 패드 2개)

**시작**
- [ ] 실행하면 모니터 해상도 전체 화면, 마우스 커서 안 보임
- [ ] 좌우 화면 분할, 3·2·1 카운트다운 → FIGHT, 가운데 승수 표시(○○ / ○○)
- [ ] 패드 1 → 왼쪽(P1), 패드 2 → 오른쪽(P2). 서로 간섭 없음. 반대면 USB를 뽑았다 순서대로 다시 꽂기
- [ ] 플레이 중 패드 하나를 뽑았다 다시 꽂음 → 같은 플레이어로 복귀

**대전**
- [ ] 이동·질주(↑↑)·사이드스텝·퀵턴·슬라이딩, 런지, 펀치, 가드·패리, 태클, 앵커 클로·슬램이 에디터와 같게 동작
- [ ] PARRY / GUARD BREAK / CLASH / STUN / OVERHEAT / EXHAUSTED 팝업이 양쪽 화면 모두에서 읽힘
- [ ] 가드(파랑)·스턴(노랑)·과열(빨강 깜빡임) 틴트가 끝나면 원래 색
- [ ] 효과음을 넣었다면 재생, 소리는 한 번씩만 (이중 재생 없음)
- [ ] 3판 2선승 끝 → `P? WINS THE MATCH` → 패드 Start(또는 Enter)로 재매치
- [ ] 60fps 근처 유지 (두 카메라 렌더). 끊김이 심하면 Quality 설정 낮춰 다시 빌드

**운영**
- [ ] ESC를 짧게 누름 → 아무 일 없음
- [ ] ESC를 1초 누르고 있음 → 화면 아래 `HOLD ESC TO QUIT [####------]` 진행 표시 후 종료. 중간에 떼면 취소
- [ ] Alt+Tab으로 나갔다 돌아옴 → 커서 다시 숨김, 패드 입력 정상 (나가 있는 동안에도 게임이 멈추지 않음 — Run In Background)
- [ ] 실행 파일 옆 **`Logs/PlayLogs` 폴더**에 `playlog_*.csv`, `events_*.csv`, `summary_*.csv`가 생김 (실행 시각 이름). 매치가 끝날 때마다 갱신, 매치 도중 종료하면 요약에 미완료 표시
- [ ] 20분 이상 여러 매치 연속 → 메모리·프레임 저하 없음, 로그 정상
- [ ] 문제가 생기면 `%USERPROFILE%\AppData\LocalLow\<Company Name>\Mydongari\Player.log`를 챙긴다

**현장 운영 팁**
- 매 세션 끝에 `Logs/PlayLogs` 폴더를 통째로 백업(파일 이름이 실행 시각이라 덮어쓰지 않음)
- 패드는 게임 실행 **전에** 두 개 다 꽂아 두는 게 배정이 가장 안정적이다
