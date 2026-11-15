# Mydongari (중장기갑 파트라슈) — Claude Code 작업 지침

이 파일은 Claude Code가 이 저장소에서 일할 때 항상 먼저 읽는 프로젝트 메모다.
게임 규칙의 정답은 `docs/GAME_SPEC.md`, 할 일 목록은 `docs/TASKS.md`다. 셋이 충돌하면 GAME_SPEC → TASKS → 이 파일 순으로 따른다.

## 프로젝트 한눈에

- Unity 6 (6000.3.14f1), URP 17, Input System 1.19, Cinemachine 3 (현재 미사용, 자체 MechCamera 사용)
- 장르: 1대1 메카 격투. 이동은 버추얼온식 이동기(60Hz 고정 틱, 위치 += 속도, 키네마틱 리지드바디 — `docs/REFERENCE_MOVEMENT.md`). 애니메이션 주도 이동은 쓰지 않는다.
- 목표: 약 3주 뒤 로케테스트. **한 PC, 패드 2개, 화면 분할, 버나드 표준형 미러전, 3판 2선승.**
- 씬: `Assets/Scenes/SampleScene.unity` 하나. 지금은 플레이어 기체 1대(Mech) + 더미(DummyTarget) 구성이다.

## 폴더와 주요 스크립트

구조는 "버추얼온 코어 + 우리 게임 레이어"다 (계획·근거 `docs/PLAN_CLEANUP.md`). 의존 방향: `Game → Bridge → Core/VirtualOn`. **`Core/` 아래 코드는 `MyDongari.Bridge`·`MyDongari.Game.*`을 using하지 않는다.**

| 경로 | 역할 |
| --- | --- |
| `Assets/Scripts/Core/VirtualOn/` | 버추얼온식 이동기 — 원본 사양(`docs/REFERENCE_MOVEMENT.md`) 그대로. `VoSpec`(수치, 기본값 = 원본), `VoTickDriver`(60Hz: 카메라 → 1P → 2P), `VoMotor`(한 틱: 입력 → 보조 → 자동 선회 → 메인 → 이동 적분, `VoMotor.Collision`은 지면·벽), `VoLever`, `VoCameraRig`, `States/`(서기·걷기·근접 스텝·지상 대시·대시 정지·점프·공중 대시·급강하), 바깥과의 접점 `IVoHost`·`IVoMachine`·`IVoDashAttack`·`IDashMove` |
| `Assets/Scripts/Core/` | 공용 유틸: `ErrorLog`, `GroundLayers`, `AppRuntime`, `MechFacing`, `AsteroidArenaConfig` |
| `Assets/Scripts/Bridge/` | 코어와 게임의 연결. `MotionBridge`(전투 → 외부 속도), `VoHostAdapter`·`VoInputAdapter`(`IVoHost` 구현: 입력·스턴·콤보 감속·가드 자동 선회·기체끼리 분리), `MechStateMachine`(`IVoMachine` 구현 + 게임 상태 호스팅), `MechController`(참조 모음·넉백 유지·감속 요청·몸 충돌 반동), `DashAttackRunner`, `MechCamera`, `StyleOverrides`(우리 스타일 수치), `MechIdentity`, `ExternalControlLock`, `GameState` |
| `Assets/Scripts/Core/VirtualOn/` (전투 뼈대) | 원본 전투 사양(`docs/REFERENCE_COMBAT.md`): `VoCombatSpec`(전투 수치, 기본값 = 원본)·`VoMeleeZone`(근접 구역), `VoMeleeDrive`(파고들기 구동 — 순수 계산), `States/VoMeleeState`·`VoJumpMeleeState`(지상·점프 근접의 이동), `IVoTickUnit`(전투 틱 참여자: 판단 → 1P → 2P → 판정 모으기) |
| `Assets/Scripts/Game/Combat/` | `MeleeSystem`(기체별 전투 허브: 전투 틱, 피격 정산·반응, 펀치 콤보·뛰어 베기, 가드·패리, 히트스톱), 순수 로직 `CombatLedger`(피격 기록·맞교환)·`HitRules`(충격치·반응 판정)·`ReactionTimers`(히트스톱·무적·경직)·`HitGeometry`(공격 구 vs 캡슐), `WeaponSystem`(공격 버튼 선택), `LockOnSystem`, `Health`, `ThrusterLunge`, `Projectile`, `Skills/`(`AnchorClaw`, `ImpactSlam`) |
| `Assets/Scripts/Game/States/`, `Game/Knockdown/` | 이동 상태 위에 얹는 게임 상태: `LandingState`, `LungeState`, `KnockdownState` + `KnockdownController` |
| `Assets/Scripts/Game/Heat/`, `Game/Tuning/` | `HeatGauge` / `BernardTuning`(전투·외형 수치), `MechTuning` |
| `Assets/Scripts/Game/Bot/` | 연습 봇 `MeleeDummy`, `BotInput` — 이동기 없이 다이내믹 리지드바디로 움직인다 |
| `Assets/Scripts/Game/Input/` | `InputReader` (PlayerInput 기반), `VersusInputPairing` |
| `Assets/Scripts/Game/UI/`, `Feedback/`, `Visual/` | `HUDController`·게이지·메뉴 / `CombatFeedback`·팝업·이펙트 / `MechVisualRig`(절차적 포즈) |
| `Assets/Scripts/Game/Flow/` | `GameManager`(라운드/매치), `PlayLogger`(CSV 로그), `PracticeHotkeys`, `SplitScreenLetterbox` |

## 코드 규칙

- 네임스페이스는 `MyDongari.<폴더>`를 따른다 (`MyDongari.Core.VirtualOn`, `MyDongari.Bridge`, `MyDongari.Game.Combat` …).
- **이동기 수치: `Core/VirtualOn/VoSpec`의 기본값은 `docs/REFERENCE_MOVEMENT.md` 원본값으로 고정한다(고치지 않는다).** 우리 스타일로 바꾸는 값은 `Bridge/StyleOverrides`(에셋 `Assets/Resources/StyleOverrides.asset`)에 칸을 만들어 거기서만 덮어쓴다. 원본과 다른 동작이 필요하면 코어에 "기본값이 원본 동작인 스위치"를 두고 StyleOverrides가 켠다. 새로 다르게 한 것은 `docs/PLAN_CLEANUP.md` 3절·7절 표에 추가한다.
- 코어(`Core/VirtualOn`)가 게임에 물어야 할 것이 생기면 `IVoHost`/`IVoMachine`에 멤버를 추가하고 `Bridge`에서 구현한다. 코어에서 전투·입력 컴포넌트를 직접 부르지 않는다.
- 조정할 수치는 전부 `[SerializeField] private` + `[Header("…")]`, 필요하면 `[Tooltip("…")]`. 매직 넘버를 코드에 박지 않는다. 기존 스타일대로 Header/Tooltip은 한국어로 쓴다.
- **전투의 모든 시간(공격 단계·쿨타임·무적·경직·히트스톱·입력 창)은 `Time.time`·`deltaTime`이 아니라 전투 틱 수로 센다.** 전투는 `VoTickDriver`의 60Hz 틱에서만 돈다(`MeleeSystem.CombatTick`·`CollectHits`가 스킬·런지·넉다운의 틱을 부른다). 새 입력은 누름 횟수(`…PressCount`)로 본다. 우리 값도 틱(`…Frames`)으로 적는다.
- **전투 수치: `Core/VirtualOn/VoCombatSpec`의 기본값은 `docs/REFERENCE_COMBAT.md` 원본값으로 고정한다.** 우리 값은 `StyleOverrides`(충격치·명중 판정·뛰어 베기 묶음)에서만 덮어쓰고, 원본과 다른 것은 `docs/PLAN_CLEANUP.md` 3절 (다)에 추가한다. 피해량 같은 게임 값은 `BernardTuning`.
- 맞히는 쪽은 `MeleeSystem.QueueHit`로 상대의 피격 기록에 쌓기만 한다(판단·반응은 다음 틱 — 1P·2P 대칭). 그 자리에서 결과를 받아 처리하지 않는다. 결과가 필요하면 `MeleeSystem.OnAttackResolved`.
- 순수 로직(`Core`의 `VoMeleeDrive`·`VoMeleeZone`, `Game/Combat`의 `CombatLedger`·`HitRules`·`ReactionTimers`·`HitGeometry`)을 고치면 `Assets/Editor/Tests/CombatRulesTests.cs`(EditMode)를 같이 고치고 돌린다.
- Unity 6 API를 쓴다: `Rigidbody.linearVelocity`, `linearDamping` (`velocity`, `drag` 금지).
- 리지드바디 감속은 `MechController.RequestDamping(value, priority)` / `SetBaselineDamping()`을 거친다. 직접 `linearDamping`을 쓰지 않는다.
- 전투 코드가 기체를 밀 때는 `MotionBridge`(SetVelocity·AddImpulse·Teleport)를 거친다. 기체 리지드바디는 키네마틱이라 `linearVelocity`·`AddForce`를 직접 쓰면 안 움직인다 (연습 봇만 다이내믹 — MotionBridge가 알아서 나눈다).
- 외부에서 상대 기체를 움직일 땐 `ExternalControlLock.Acquire()/Release()` 짝을 반드시 맞춘다.
- **입력은 반드시 `InputReader`를 거친다.** `Mouse.current`, `Keyboard.current`, `Gamepad.current`를 게임 로직에서 직접 읽지 않는다 (2인 플레이가 깨진다).
- **상대는 태그(`"Enemy"`, `"Player"`)로 찾지 않는다.** 각 기체가 가진 상대 참조를 쓴다 (TASKS A0에서 도입).
- **카메라는 `GameObject.FindWithTag("MainCamera")`나 `Camera.main`으로 찾지 않는다.** 각 기체가 자기 카메라 참조를 가진다.
- `Debug.Log` 남발 금지 (이전에 일괄 정리함). 디버그 출력이 필요하면 `[SerializeField] bool debugLog` 플래그 뒤에 둔다.
- 주석은 한국어, "왜 이렇게 했는지"를 쓴다. 기존 코드의 주석 톤을 따른다.

## 건드리면 안 되는 것

- `.unity`, `.prefab`, `.asset` 같은 직렬화 YAML 파일을 손으로 편집하지 않는다. 씬/프리팹 배선이 필요하면 (1) 런타임에 스스로 찾아 연결되게 코드로 만들거나, (2) `docs/SETUP.md`에 사용자가 에디터에서 할 단계를 정확히 적는다. 필요하면 `Assets/Editor/` 아래에 메뉴 한 번으로 세팅해 주는 에디터 스크립트를 만들어도 된다.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`는 건드리지 않는다.
- 빌드 뒤 `ProjectSettings/ProjectSettings.asset`에 `preloadedAssets`가 비어 있는(`[]`) 차이만 생기면 Input System 빌드 부수 효과이니 커밋하지 말고 `git checkout -- ProjectSettings/ProjectSettings.asset`으로 되돌린다 (커밋된 2개 항목이 정답).
- `.inputactions`는 JSON이라 편집해도 되지만, 기존 바인딩을 지우지 말고 추가만 한다. 편집 후 JSON이 유효한지 확인한다.
- 스토리·세계관 설정은 이 작업 범위가 아니다.

## 작업 방식

1. 작업 시작 전에 `docs/GAME_SPEC.md`, `docs/TASKS.md`를 읽는다.
2. 브랜치 `v3`에서 작업한다. **push는 하지 않는다.**
3. TASKS의 순서대로 한 태스크씩 처리한다. 태스크 하나 = 커밋 하나 이상. 커밋 메시지는 한국어로 "태스크ID: 요약" (예: `A3: 가드 규칙을 히트 기반으로 교체`).
4. 이 환경에서는 Unity 에디터를 실행할 수 없다. 그래서:
   - 존재하는 API만 쓰고, 바꾼 public 멤버를 참조하는 곳을 `grep`으로 전부 찾아 같이 고친다. 컴파일 에러를 남기지 않는 게 최우선이다.
   - 태스크마다 `docs/TEST_CHECKLIST.md`에 사용자가 에디터에서 직접 확인할 항목을 추가한다 (무엇을 하면 무엇이 보여야 하는지).
   - 인스펙터에서 새로 연결해야 하는 참조가 생기면 `docs/SETUP.md`에 적는다.
5. 스펙이 애매하거나 서로 충돌하면 임의로 정하지 말고, 가장 보수적인 기본값으로 구현한 뒤 `docs/QUESTIONS.md`에 질문과 선택한 기본값을 적는다. 작업은 멈추지 않는다.
6. 태스크가 끝날 때마다 `docs/TASKS.md`의 체크박스를 체크하고, 바뀐 파일과 남은 위험을 한두 줄 메모로 남긴다.
