# 에디터 세팅 단계

Claude Code는 씬·프리팹 파일을 직접 편집하지 않는다. 인스펙터에서 새로 연결해야 하는 참조, 추가할 컴포넌트, 실행할 에디터 메뉴를 여기에 순서대로 적는다.
형식: `1. (태스크) 오브젝트 → 컴포넌트 → 필드에 무엇을 연결`


## Phase A

0. (공통) 새 스크립트(`MechIdentity.cs`, `ICombatInputSource.cs`, `HitTypes.cs` 등)는 `.meta`가 없다. 에디터를 한 번 열어 임포트하면 자동 생성되니 생성된 `.meta`도 커밋한다.
1. (A0) 별도 연결 없이 동작하게 만들었다: `GameManager`의 `Player1`(Mech)·`Player2`(DummyTarget)에 `MechIdentity`를 런타임에 붙이고 서로를 Opponent로 연결한다. **GameManager의 Player1/Player2 필드가 비어 있으면 아무도 안 맞는다** — 비어 있지 않은지 확인.
2. (A0, 선택) 원하면 Mech·DummyTarget에 `MechIdentity`를 직접 추가해 `Opponent`를 서로 지정해도 된다 (지정돼 있으면 GameManager가 덮어쓰지 않는다). `Camera Transform`은 비워 두면 `MechController → Camera Transform`을 쓴다.
3. (A0) HUD → `HUDController` → `View Camera`: 비워 두면 Lock On System 기체의 카메라를 쓴다. B1 화면 분할에서는 각 HUD에 자기 카메라를 넣는다.
4. (A0) Camera → `MechCamera` → `Stick Sensitivity`(기본 180°/초): 패드 오른쪽 스틱 시점 감도. 마우스는 기존 `Mouse Sensitivity` 그대로.
5. (A0) `Assets/InputSystem_Actions.inputactions`에 `Guard`, `StepLeft`, `StepRight` 액션과 패드 바인딩(RT 공격, LT 가드, LB/RB 스킬)을 추가했다. 에디터에서 에셋을 열어 저장 경고가 없는지 확인. 만약 프로젝트가 이 에셋에서 C# 클래스를 생성하도록 설정돼 있다면 재생성된다(현재 코드는 생성 클래스를 쓰지 않음).
6. (A1) Mech → `LockOnSystem`: 씬에 저장된 값이 코드 기본값보다 우선한다. 현재 씬 값은 `Hard Lock Enter Range 6`, `Exit 7`로 스펙과 같지만 **`Hard Lock Track Speed`가 10**(스펙 5)이다. 스펙대로 하려면 5로 바꾼다.
7. (A1) `WeaponSystem`의 `Melee Auto Range`(씬 값 50) 필드는 삭제됐다. 펀치 전환은 하드 락온 진입 거리(6m) 하나로만 정해진다.
8. (A4) Mech·DummyTarget → `MeleeSystem`: 가드 필드가 바뀌었다. `Guard Damage Reduction`(씬 값 0.7)·`Guard Boost Drain`은 삭제되고 `Guard Melee Reduction 0.5`, `Guard Projectile Reduction 0.7`, `Guard Heat Per Block 10`, `Guard Break Stun 0.9`, `Guard Reraise Delay 0.4`, `Guard Damping 8`, `Guard Release Damping 2`, `Clash Knockback 10`이 새로 생긴다(코드 기본값). 인스펙터에서 값만 확인.
9. (A5) Mech·DummyTarget → `MeleeSystem`: `Attacker Stun Duration`(씬 값 0.5)은 삭제. 새 필드 `Parry Reflect Ratio 1`, `Parry Reflect Knockback 8`, `Tackle Recovery Stun 0.5`(태클 후 자기 경직 — 기존 SprintState에 0.5로 박혀 있던 값, 패리당하면 미적용).
10. (A6) `MeleeSystem` → `Punch Half Angle`(기본 45°) 새 필드. DummyTarget → `MeleeDummy` → `Turn Speed`(기본 8) 새 필드: 더미가 상대를 향해 몸을 돌린다.
11. (A7) `ImpactSlam` → `Slam Knockback Lift`(기본 0.3) 새 필드: 기존 코드에 박혀 있던 넉백 위쪽 성분.
12. (A8) GameManager → `Player1 Spawn Point`·`Player2 Spawn Point`: 이제 **회전도** 스폰 포인트를 따른다. 스폰 포인트 오브젝트(` SpawnPoint_P1`, ` SpawnPoint_P2`)의 Y 회전을 기체가 바라볼 방향(서로 마주 보게)으로 맞춘다. 카메라도 그 방향으로 리셋된다.
13. (A9) `HeatGauge`의 `Sprint Cooling Penalty`(씬 값 0.2)는 `Sprint Cooling Reduction`으로 이름이 바뀌어 새 기본값 **0.7**(질주 중 냉각 속도 30%)이 적용된다. 씬 값을 따로 고칠 필요 없음.
14. (A9) `MechController` → `Hard Lock Turn Speed`(기본 10) 새 필드: 하드 락온 중 몸통이 상대 쪽으로 도는 속도.
15. (A9) `.inputactions`에서 그룹이 비어 있던 키보드 바인딩(Ctrl 슬라이드, R 퀵턴, Shift 달리기, Q/E 스킬)을 `Keyboard&Mouse` 스킴에 넣었다.

## Phase B

16. (B1) **메뉴 `Mydongari/Setup Versus Scene` 실행** → 저장(Ctrl+S). 하는 일:
    - `Mech`를 복제해 `Mech_P2`를 P2 스폰 포인트에 만든다 (카메라가 자식이라 같이 복제됨). P2 카메라의 AudioListener 제거, 태그 Untagged.
    - 카메라 viewport: P1 왼쪽 절반, P2 오른쪽 절반.
    - 기존 `Canvas`를 Screen Space - Camera(P1 카메라)로 바꾸고 복제해 `Canvas_P2`(P2 카메라). 각 HUD의 참조를 자기 기체로 연결, 화면 위쪽 가운데에 작은 상대 체력 바(`OpponentHPBar`) 추가.
    - `GameManager`: Player2 = Mech_P2, Dummy Opponent = DummyTarget, Versus Only Objects = [Mech_P2, Canvas_P2], Player1 Camera, Versus Mode 켬. `VersusInputPairing` 컴포넌트 추가·연결.
    - `PlayLogger` Player1/Player2 연결.
    - Ctrl+Z 한 번으로 전부 되돌릴 수 있다. 이미 `Mech_P2`가 있으면 아무것도 안 한다.
17. (B1) 1인(더미) 테스트: `GameManager` → `Versus Mode` 끄기. P2 기체·P2 캔버스가 꺼지고 DummyTarget이 상대가 되며 P1 카메라가 전체 화면이 된다.
18. (B1) 패드 배정 규칙(`GameManager` 오브젝트의 `VersusInputPairing`): 패드 2개 → P1 = 먼저 연결된 패드, P2 = 두 번째. 패드 1개 → P2 = 패드, P1 = 키보드·마우스. `Allow Keyboard For P1`(기본 켬)이면 P1에 키보드·마우스도 붙는다. 로케 PC에서 P1/P2 패드가 반대로 잡히면 패드 USB를 뽑았다 순서대로 다시 꽂는다.
19. (B1) 수동 확인: `Canvas_P2`의 HUD 요소가 오른쪽 화면 안에 들어오는지. Canvas Scaler가 화면 전체 해상도 기준이라 반쪽 화면에서 HUD가 크게 보이면 두 캔버스의 Canvas Scaler → `Match Width Or Height`를 1(높이 기준)로 바꾼다.
20. (B2) `.inputactions`에 `MoveStick`(Value/Vector2, `<Gamepad>/leftStick`) 액션 추가 — 제스처 판정 전용이라 키보드 WASD는 튕기기로 오인되지 않는다.
21. (B2) `Mech`(·`Mech_P2`) → `InputReader`: `Move Deadzone 0.3`(걷기 시작), `Run Threshold 0.8`(달리기), `Gesture` 묶음(중립 0.3 / 끝 0.8 / 도달 0.15초 / 복귀 0.25초 / 부채꼴 ±30° / ↑↑ 간격 0.35초 / 슬라이딩 창 0.2초 / 슬라이딩 각도 20~55°) 새 필드. Versus 세팅을 이미 했다면 두 기체 모두 같은 값인지 확인.
22. (B3) **메뉴 `Mydongari/Add Missing Mech Components` 실행** → 저장. PlayerInput이 있는 기체(Mech, Mech_P2)에 `ThrusterLunge`를 붙인다. (Versus 세팅 메뉴도 복제 전에 P1에 붙인다. 안 붙여도 런타임에 기본값으로 자동 부착되지만 수치 조정·저장은 씬에 붙어 있어야 한다.)
23. (B3) `ThrusterLunge` 수치: 거리 8 / 속도 30 / 종료 속도 비율 0.2 / 부스트 15 / 피해 15 / 스턴 0.5 / 넉백 0 / 판정 반경 1.2·전방 1 / 빗나감 락 지연 0.6. 돌진 거리·속도는 테스트로 정한다.
24. (B3) `WeaponSystem` → `Ranged Mode`: 버나드는 `Lunge`(기본). 사격 기체 테스트 때만 `Projectile`.
25. (B4) `MeleeSystem` → `Dash Attack Damage 20`, `Dash Attack Knockback 6`, `Dash Attack Stun 0` 새 필드 (스펙에 수치가 없어 임시값). 두 기체 모두 같은 값인지 확인.
26. (B5) `.inputactions`에 `Start` 액션 추가(패드 Start, 키보드 Enter) — 매치 종료 후 재매치용.
27. (B5) 가운데 공용 라운드 UI: `Mydongari/Setup Versus Scene`을 이미 실행했다면 **`Mydongari/Setup Round UI`** 를 따로 실행 → 저장. (아직 안 했다면 Versus 세팅이 같이 만든다.) `RoundUI` Overlay 캔버스에 카운트다운/FIGHT/승자 텍스트와 위쪽 가운데 승수(●○)가 생긴다.
28. (B5) `GameManager` → `Countdown Duration 3`, `Fight Banner Duration 1`, `Respawn Delay 3`(라운드 종료 후 대기), `Rounds To Win 2`.
29. (B6) `PlayLogger`: Player1/Player2를 비워 두면 GameManager의 현재 대결 기체(1인 모드면 P2 = 더미)를 쓴다. 새 필드 `Event File Name`(events), `Summary File Name`(summary), `Near Range 6`, `Mid Range 12`. 로그 파일은 기존처럼 `Assets/` 폴더(빌드에선 `*_Data/`)에 `playlog_*.csv`, `events_*.csv`, `summary_*.csv`로 생긴다. 에디터에서 로그가 Assets에 쌓이면 임포트되니 테스트 후 정리.
30. (B7) `GameManager` 오브젝트에 `HitStop` 컴포넌트(Versus 세팅 메뉴가 추가, 없으면 런타임 자동 부착). `Enable Hit Stop`, 길이: 펀치 0.05 / 런지·태클·대시 어택 0.08 / 맞딜 0.12 / 그 외 0(슬램·파일드라이버). 이미 Versus 세팅을 했다면 GameManager에 `HitStop`을 직접 Add Component 해야 수치가 저장된다.
