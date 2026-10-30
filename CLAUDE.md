# Mydongari (중장기갑 파트라슈) — Claude Code 작업 지침

이 파일은 Claude Code가 이 저장소에서 일할 때 항상 먼저 읽는 프로젝트 메모다.
게임 규칙의 정답은 `docs/GAME_SPEC.md`, 할 일 목록은 `docs/TASKS.md`다. 셋이 충돌하면 GAME_SPEC → TASKS → 이 파일 순으로 따른다.

## 프로젝트 한눈에

- Unity 6 (6000.3.14f1), URP 17, Input System 1.19, Cinemachine 3 (현재 미사용, 자체 MechCamera 사용)
- 장르: 지면 밀착형 1대1 메카 격투. 리지드바디 물리(AddForce, 관성, 질량) 기반. 애니메이션 주도 이동은 쓰지 않는다.
- 목표: 약 3주 뒤 로케테스트. **한 PC, 패드 2개, 화면 분할, 버나드 표준형 미러전, 3판 2선승.**
- 씬: `Assets/Scenes/SampleScene.unity` 하나. 지금은 플레이어 기체 1대(Mech) + 더미(DummyTarget) 구성이다.

## 폴더와 주요 스크립트

| 경로 | 역할 |
| --- | --- |
| `Assets/Scripts/Movement/` | `MechController`(힘·감속·속도 제한), `MechStateMachine`과 상태들(Idle/Walk/Run/Sprint/Brake/Slide/SideStep/QuickTurn) |
| `Assets/Scripts/Mech/` | `BoostGauge`, `HeatGauge` |
| `Assets/Scripts/Combat/` | `MeleeSystem`(펀치·가드·패리·태클·스턴), `WeaponSystem`(공격 버튼 분기, 현재 사격 포함), `LockOnSystem`, `Health`, `Projectile`, `MeleeDummy` |
| `Assets/Scripts/Combat/Skills/` | `AnchorClaw`(끌어오기 + 파일드라이버), `ImpactSlam` |
| `Assets/Scripts/Camera/MechCamera.cs` | 3인칭 카메라, 하드 락온 추적, FOV |
| `Assets/Scripts/Input/InputReader.cs` | 입력 읽기 (PlayerInput 기반 + 일부 키보드 직접 읽기) |
| `Assets/Scripts/UI/` | `HUDController`, `ArcGauge`, `LinearGauge` |
| `Assets/Scripts/GameManager.cs` | 라운드/매치 (`MyDongari.Core`) |
| `Assets/Scripts/Core/PlayLogger.cs` | 0.1초 간격 CSV 로그 |
| `Assets/Scripts/ExternalControllock.cs` | `ExternalControlLock` — 외부(앵커 클로 등)가 리지드바디를 잡을 때 이동 로직을 잠그는 카운터 락 |

## 코드 규칙

- 네임스페이스는 `MyDongari.<폴더>`를 따른다 (`MyDongari.Combat`, `MyDongari.Movement` …).
- 조정할 수치는 전부 `[SerializeField] private` + `[Header("…")]`, 필요하면 `[Tooltip("…")]`. 매직 넘버를 코드에 박지 않는다. 기존 스타일대로 Header/Tooltip은 한국어로 쓴다.
- Unity 6 API를 쓴다: `Rigidbody.linearVelocity`, `linearDamping` (`velocity`, `drag` 금지).
- 리지드바디 감속은 `MechController.RequestDamping(value, priority)` / `SetBaselineDamping()`을 거친다. 직접 `linearDamping`을 쓰지 않는다.
- 외부에서 상대 기체를 움직일 땐 `ExternalControlLock.Acquire()/Release()` 짝을 반드시 맞춘다.
- **입력은 반드시 `InputReader`를 거친다.** `Mouse.current`, `Keyboard.current`, `Gamepad.current`를 게임 로직에서 직접 읽지 않는다 (2인 플레이가 깨진다).
- **상대는 태그(`"Enemy"`, `"Player"`)로 찾지 않는다.** 각 기체가 가진 상대 참조를 쓴다 (TASKS A0에서 도입).
- **카메라는 `GameObject.FindWithTag("MainCamera")`나 `Camera.main`으로 찾지 않는다.** 각 기체가 자기 카메라 참조를 가진다.
- `Debug.Log` 남발 금지 (이전에 일괄 정리함). 디버그 출력이 필요하면 `[SerializeField] bool debugLog` 플래그 뒤에 둔다.
- 주석은 한국어, "왜 이렇게 했는지"를 쓴다. 기존 코드의 주석 톤을 따른다.

## 건드리면 안 되는 것

- `.unity`, `.prefab`, `.asset` 같은 직렬화 YAML 파일을 손으로 편집하지 않는다. 씬/프리팹 배선이 필요하면 (1) 런타임에 스스로 찾아 연결되게 코드로 만들거나, (2) `docs/SETUP.md`에 사용자가 에디터에서 할 단계를 정확히 적는다. 필요하면 `Assets/Editor/` 아래에 메뉴 한 번으로 세팅해 주는 에디터 스크립트를 만들어도 된다.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`는 건드리지 않는다.
- `.inputactions`는 JSON이라 편집해도 되지만, 기존 바인딩을 지우지 말고 추가만 한다. 편집 후 JSON이 유효한지 확인한다.
- 스토리·세계관 설정은 이 작업 범위가 아니다.

## 작업 방식

1. 작업 시작 전에 `docs/GAME_SPEC.md`, `docs/TASKS.md`를 읽는다.
2. 브랜치 `loketest`에서 작업한다. 없으면 만든다. **push는 하지 않는다.**
3. TASKS의 순서대로 한 태스크씩 처리한다. 태스크 하나 = 커밋 하나 이상. 커밋 메시지는 한국어로 "태스크ID: 요약" (예: `A3: 가드 규칙을 히트 기반으로 교체`).
4. 이 환경에서는 Unity 에디터를 실행할 수 없다. 그래서:
   - 존재하는 API만 쓰고, 바꾼 public 멤버를 참조하는 곳을 `grep`으로 전부 찾아 같이 고친다. 컴파일 에러를 남기지 않는 게 최우선이다.
   - 태스크마다 `docs/TEST_CHECKLIST.md`에 사용자가 에디터에서 직접 확인할 항목을 추가한다 (무엇을 하면 무엇이 보여야 하는지).
   - 인스펙터에서 새로 연결해야 하는 참조가 생기면 `docs/SETUP.md`에 적는다.
5. 스펙이 애매하거나 서로 충돌하면 임의로 정하지 말고, 가장 보수적인 기본값으로 구현한 뒤 `docs/QUESTIONS.md`에 질문과 선택한 기본값을 적는다. 작업은 멈추지 않는다.
6. 태스크가 끝날 때마다 `docs/TASKS.md`의 체크박스를 체크하고, 바뀐 파일과 남은 위험을 한두 줄 메모로 남긴다.
