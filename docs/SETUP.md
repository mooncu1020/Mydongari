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
31. (B8) `MeleeSystem` → `Tackle Half Angle`(기본 45°) 새 필드: 버튼 태클·대시 어택·자동 접촉 태클 공통 정면 부채꼴.

## Phase C

32. (C1) **메뉴 `Mydongari/Tuning/Create Default Tuning And Assign` 실행** → 저장. `Assets/Resources/BernardTuning.asset`을 코드 기본값으로 만들고(이미 있으면 그걸 씀), 씬의 모든 기체(MeleeSystem이 있는 오브젝트: Mech, Mech_P2, DummyTarget)에 `MechTuning` 컴포넌트를 붙여 이 에셋을 연결한다. Versus 세팅·Add Missing Mech Components 메뉴도 이제 같은 연결을 한다.
33. (C1) 씬에서 조정해 둔 예전 값이 있으면 **`Mydongari/Tuning/Copy P1 Values To Tuning`** 실행. GameManager Player1 기체의 예전 값(이동·부스트·히트·펀치·가드·패리·태클·대시 어택·런지·락온·앵커 클로·슬램)과 GameManager의 HitStop 값을 에셋으로 복사한다. 바뀐 항목이 다이얼로그에 나온다. Ctrl+Z로 되돌릴 수 있다.
    - 주의: A9에서 이름이 바뀐 `Sprint Cooling Reduction`처럼 씬에 한 번도 저장되지 않은 값은 코드 기본값이 복사된다.
34. (C1) **기존 개별 필드는 삭제하지 않고 `[HideInInspector]`로 숨겼다.** 삭제하면 씬에 저장된 예전 값을 33번 복사 메뉴가 읽을 수 없기 때문이다. 숨긴 필드는 런타임에서 읽지 않는다(인스펙터에서 안 보임). 복사를 마친 뒤 원하면 다음 작업에서 숨긴 필드를 지울 수 있다.
35. (C1) 수치 조정은 이제 **`BernardTuning` 에셋 하나**에서 한다(Project 창 `Assets/Resources/BernardTuning`). 두 기체가 같은 에셋을 보므로 한 번 바꾸면 양쪽에 적용된다. 플레이 중 바꾼 값은 즉시 반영되고, **플레이를 멈춰도 에셋에 그대로 남는다**(컴포넌트 값과 다름). 실험 전 값을 되돌리려면 git으로 에셋을 되돌린다.
36. (C1) 에셋으로 옮기지 않은 수치(컴포넌트에 그대로 있음): `Health → Max Health`(더미·기타 오브젝트와 공용), `InputReader`(스틱 데드존·달리기 경계·제스처·더블탭), `MechCamera`(감도·FOV), `WeaponSystem`·`Projectile`(사격 기체용), `MeleeDummy`(더미 AI), `GameManager`(라운드), `PlayLogger`.
37. (C1) DummyTarget도 같은 에셋 값을 쓴다(연결이 없어도 `Resources/BernardTuning`을 읽음). 예전에 더미의 `MeleeSystem`에 따로 넣어 둔 값은 더 이상 쓰이지 않는다.
38. (C1) 새로 에셋으로 들어온 값(예전엔 코드에 박혀 있던 것): 사이드스텝 0.3초·20, 슬라이딩 0.8초·25, 초신지선회 0.15초, 제동 1초·슬라이딩 입력 창 0.5초, 태클 차징 시작 1.5초·최대 3초·자동 접촉 반경 2, 앵커 당김 종료 거리 3·파일드라이버 거리 4. 값은 예전과 같다.
39. (C2) `GameManager` 오브젝트에 **`CombatFeedback`** 컴포넌트(상태 팝업·색 틴트·효과음). 없으면 런타임에 자동으로 붙지만, 효과음 클립을 넣고 저장하려면 씬에 있어야 한다 → **`Mydongari/Add Missing Mech Components`** 를 다시 실행하면 GameManager에 `HitStop`·`CombatFeedback`이 붙는다(Versus 세팅 전이면 그 메뉴도 붙인다). 저장.
40. (C2) `CombatFeedback` → `효과음` 칸 7개: Punch Hit / Guard Block / Parry / Guard Break / Clash / Lunge Start / Tackle Hit(대시 어택·차징 태클 명중). 비워 두면 무음. 2D로 재생되어 화면 분할 양쪽에 같은 크기로 들린다. 볼륨은 `Sfx Volume`.
41. (C2) 팝업 문구·색, 크기(`Popup Style`: 수명 0.8초, 상승 1m, 글자 크기 0.08, 거리 10m 이상은 최대 3배까지 확대), 머리 위 여유(`Popup Head Margin` 0.5m)는 `CombatFeedback`에서 조정. 폰트는 Unity 기본 `LegacyRuntime.ttf`(영문).
42. (C2) 색 틴트: 스턴(노랑) > 과열(빨강, 초당 3회 깜빡임) > 가드(파랑), 섞는 비율 `Tint Strength` 0.5. 머티리얼 에셋은 바꾸지 않고 MaterialPropertyBlock으로 `_BaseColor`(·`_Color`)만 덮어쓴다 → 상태가 끝나거나 컴포넌트가 꺼지면 원래 색. 다른 셰이더로 바꿔 `_BaseColor`/`_Color`가 없으면 틴트가 안 보인다.
43. (C3) `PlayLogger` 저장 위치가 바뀌었다: 에디터 = **프로젝트 루트 `Logs/`** (Unity 자체 로그와 같은 폴더, .gitignore 대상이라 커밋되지 않음), 빌드 = **실행 파일 옆 `Logs/`**. 새 필드 `Log Folder Name`(기본 Logs). 예전 위치(`Assets/`)에 남은 `playlog_*.csv`·`events_*.csv`·`summary_*.csv`와 그 `.meta`는 필요 없으면 지운다.
44. (C3) `GameManager` 오브젝트에 **`AppRuntime`** (없으면 런타임 자동 부착, `Add Missing Mech Components` 메뉴도 추가): 빌드에서 ESC 1초 길게 누르기 종료(`Quit Hold Seconds`), 커서 숨김(`Hide Cursor In Build`), 시작 시 전체 화면 강제(`Force Fullscreen On Start`). 에디터에서는 기본으로 아무것도 안 한다 — 확인하려면 `Quit In Editor`를 켜면 ESC 1초로 플레이 모드가 끝난다.
45. (C3) 빌드 설정은 `docs/BUILD.md` 체크리스트대로. 특히 Player Settings → **Run In Background 켜기**(현재 꺼짐), **Force Single Instance 켜기**, Input System → **Background Behavior = Ignore Focus** 권장.
46. (C4) 로그 폴더가 `Logs/PlayLogs`로 바뀌었다 (에디터: 프로젝트 루트/Logs/PlayLogs, 빌드: 실행 파일 옆/Logs/PlayLogs). `PlayLogger` → 새 필드 `Log Subfolder Name`(기본 PlayLogs, 비우면 Logs 바로 아래). 씬에는 `Log Folder Name = Logs`가 저장돼 있어 그대로 두면 된다.
47. (C4) `CombatFeedback` → 효과음 칸 `Lunge Hit Clip` 추가 (런지가 그대로 맞았을 때). STUN 팝업은 이제 상대에게 맞아서 생긴 스턴(런지·차징 태클 명중, 앵커 클로 훅)에만 뜬다. 태클 후 자기 경직에는 안 뜬다(틴트는 그대로 노랑).

## Phase D

48. (D1) DummyTarget → `MeleeDummy`가 연습 봇이 됐다. 새 필드 `Mode`(Idle / AlwaysGuard / Approach / Lunger / Parrier / Mixed, 기본 Approach = 예전 동작). 기존 `Detection Range`·`Attack Range`·`Attack Cooldown`·`Move Speed`·`Turn Speed` 값은 그대로 쓰인다.
    - Lunger: `Lunger Min/Max Distance` 6/12m, `Lunge Interval Min/Max` 1.5~3.5초, `Lunge Aim Angle` 10°.
    - Parrier: `Parry Chance` 0.6, `Reaction Time` 0.2초(런지에만 의미 있음), `Parry Hold Time` 0.3초, `Counter After Parry` 켬, `Counter Delay` 0.1초.
    - Mixed: `Mixed Segment Min/Max` 4~8초마다 AlwaysGuard/Approach/Lunger/Parrier 중 하나로 바뀜.
    - 플레이 중 인스펙터에서 `Mode`를 바꿔도 바로 반영된다.
49. (D1) 씬 편집 없음: 봇에 필요한 `BotInput`·`HeatGauge`·`BoostGauge`·`ThrusterLunge`는 `MeleeDummy`가 플레이 시작 때 없으면 붙인다(봇도 플레이어와 같은 히트·부스트·런지 규칙). 수치는 모두 `BernardTuning` 에셋에서 읽으므로 따로 넣을 값이 없다.
50. (D2) 실행하면 **타이틀**(VERSUS / PRACTICE)부터 시작한다. 씬 편집·메뉴 실행 없이 동작: 타이틀·메뉴·연습 표시 캔버스는 `MenuUI`가 런타임에 만든다(GameManager에 자동 부착). 문구(`Game Title` 기본 MYDONGARI 등)·색을 바꾸려면 GameManager 오브젝트에 `MenuUI`를 직접 Add Component 해서 저장.
51. (D2) `GameManager` 새 필드: `Show Title On Start`(기본 켬), `Practice Respawn Delay`(1초). **SETUP 17의 "Versus Mode 끄기로 1인 테스트"는 이제 타이틀에서 PRACTICE를 고르면 된다.** `Versus Mode`는 `Show Title On Start`를 끈 경우에만 쓰인다(켬 = 바로 2인, 끔 = 바로 연습).
52. (D2) 모드를 바꿀 때마다 GameManager가 상대 연결(`MechIdentity.Opponent`)을 다시 한다. 인스펙터에서 Opponent를 직접 지정해 둔 값은 덮어쓴다(A0의 "지정돼 있으면 덮어쓰지 않음"은 더 이상 유효하지 않음).
53. (D2) 패드 배정: VERSUS는 예전 규칙(SETUP 18). PRACTICE는 P1 = 패드 0(있으면) + 키보드·마우스. 모드가 바뀔 때마다 다시 배정한다.
54. (D2) `.inputactions`에 `Submit`(패드 A/South)·`MenuNavigate`(패드 D패드) 추가. 메뉴 이동은 이동 스틱·D패드·W/S·화살표, 결정은 패드 A·Start, 키보드 Enter.
55. (D2) `RoundUI`의 `Rematch Hint` 필드는 지웠다(매치 종료 선택지는 MenuUI가 그림). 새 필드 `Practice Label`(PRACTICE)·`Knockout Label`(K.O.).
56. (D3) 연습 단축키(PRACTICE의 싸우는 중에만, P1 입력): 패드 Select 짧게 / Tab = 봇 모드 순환, 패드 Select 길게(0.6초) / R = 두 기체 즉시 리셋, 키보드 1~4 = 내 히트 0/50/90/100%. `.inputactions`에 `Select`·`BotCycle`·`PracticeReset`·`HeatPreset1~4` 추가. 길게 누르기 시간은 `InputReader → Select Hold Time`, 히트 비율은 GameManager 오브젝트의 `PracticeHotkeys → Heat Presets`(런타임 자동 부착 — 바꾸려면 직접 Add Component).
57. (D3) 키보드 **R은 원래 초신지선회 키**다. PRACTICE에서는 R이 리셋도 같이 하며, 리셋이 그 프레임 입력을 비우고 카메라·상태를 되돌리므로 퀵턴이 남지 않는다. VERSUS에서는 R = 초신지선회 그대로.
58. (D4) 연습 리셋 키보드 키를 **R → Backspace**로 옮겼다(`.inputactions` `PracticeReset` 바인딩 수정). R은 모든 모드에서 초신지선회만 한다. SETUP 56·57의 R 설명은 Backspace로 읽는다. 패드 Select 길게(0.6초)는 그대로.
59. (D4) 타이틀 문구 기본값 `DRAFT HOUND PATRASCHE` (`MenuUI → Game Title`).
60. (C5) 값 복사 확인이 끝나 **숨겨 둔 예전 개별 필드(`[HideInInspector]`)와 `Mydongari/Tuning/Copy P1 Values To Tuning` 메뉴를 삭제**했다. SETUP 33·34는 더 이상 해당 없음. 수치는 `BernardTuning` 에셋에서만 조정한다. 씬 파일에 남아 있는 예전 값(YAML)은 쓰이지 않으며, 다음에 씬을 저장할 때 Unity가 지운다(씬 변경으로 표시될 수 있음 — 그대로 커밋하면 된다).

## Phase E

61. (E1) **메뉴 `Mydongari/Visual/Create Placeholder Parts`** → 저장. 씬의 모든 기체(Mech, Mech_P2, DummyTarget — MeleeSystem이 있는 오브젝트)에 `VisualPlaceholder` 임시 상자 부품(Body, Head, 양팔 3마디, 양다리)을 만들고, 루트 캡슐 렌더러를 끄고(콜라이더는 그대로), `MechVisualRig`를 붙여 슬롯을 연결한다. 이미 있는 기체는 건너뛴다. Ctrl+Z 한 번으로 되돌림.
    - Versus 세팅(`Mech_P2` 복제) 전에 실행해도 되고 후에 실행해도 된다(복제하면 부품·연결도 같이 복제됨).
62. (E1) `MechVisualRig`: `Animate` 끄면 정지. 슬롯 10개(부품 + `Rotation Correction`). 동작 크기·속도는 `BernardTuning` → **Visual** 묶음(스프링 8Hz·감쇠 0.6, 예비 동작 0.033초·25°, 펀치, 가드·패리, 런지, 태클, 클로, 파일드라이버·슬램, 스턴, 가드 깨짐, 이동). 플레이 중 바꾸면 바로 반영(에셋에 남음).
63. (E1) `AnchorClaw`에 `OnPileDriverStarted`, `ImpactSlam`에 `OnSlamStarted` 이벤트를 추가했다(외형용, 판정 변화 없음).
64. (E2) 모델이 오면 `docs/MODEL_IMPORT.md` 순서대로: FBX를 `Assets/Models/`에 넣고 → (기체 선택, 또는 아무것도 선택 안 하면 전체) **`Mydongari/Visual/Apply Model To Mechs`** → FBX 선택. `VisualModel`로 붙고 부품 10개가 리그에 연결되며 임시 상자는 꺼진다. 못 찾은 부품 이름은 다이얼로그에 나온다. 축이 다른 부품은 `MechVisualRig` → 슬롯 → `Rotation Correction`.
65. (E3) 예외 기록: PlayLogger·CombatFeedback·StatusPopup·HitStop·MechVisualRig·연습 봇의 이벤트 처리와 매 프레임 처리를 감쌌다. 예외가 나도 게임과 전투 판정은 계속되고, 내용은 `Logs/PlayLogs/errors.txt`(빌드는 실행 파일 옆)에 쌓인다(같은 예외는 1·2·4·8…번째만). 처리 안 된 다른 예외도 같은 파일에. 에디터에선 Console에도 보인다.
66. (E3) `GameManager` → `Idle Return To Title`(기본 끔), `Idle Timeout`(120초): 켜면 타이틀이 아닌 화면에서 2분 동안 아무 플레이어 입력이 없을 때 타이틀로 돌아간다.
67. (E3) 10/27 최종 빌드 전 확인은 `docs/RELEASE_CHECKLIST.md`.
68. (E4) `BernardTuning` → **공격 발생 시간** 묶음: `Punch` 0.1, `Dash Attack` 0.12, `Charged Tackle` 0.1(초), `Clash Window` 0.1. 각 값을 0으로 하면 그 공격은 예전처럼 누른 프레임에 즉시 판정. 외형 예비 동작 길이는 이 값을 따른다(0일 때만 `Visual → Anticipation Time`).
69. (E4) DummyTarget → `MeleeDummy` → `Reaction Time` 기본값이 0.08초로 바뀌었다(씬에 저장돼 있지 않아 새 기본값 적용). 패리 연습 난이도는 이 값과 `Parry Chance`로 조절.
70. (E4.1) 공격은 이제 **버튼을 누른 순간에만** 나간다(누르고 있으면 반복 안 함). `BernardTuning → 공격 발생 시간·입력 버퍼 → Input Buffer`(0.15초): 못 나갈 때 누른 입력을 이 시간 동안 기억. 0이면 버퍼 없음(누른 프레임에 못 나가면 버림). 로그 요약에 `buffered_attack`(종류별 `buffered_punch` 등).
71. (E5) 리깅된 모델(Generic): `docs/MODEL_IMPORT.md` 8장 순서. Apply Model To Mechs가 뼈를 슬롯에 연결하고 Animator를 리그에 연결한다. **`Mydongari/Visual/Create Locomotion Animator Controller`** → `Assets/Animations/MechLocomotion.controller`(파라미터 Speed·MoveState, 상태 Idle/Walk/Run/Sprint, Motion 비어 있음)를 만들어 모델 Animator에 붙인다. 클립은 직접 넣는다. `MechVisualRig` → 새 필드 `Animator`(비우면 모델 아래에서 자동으로 찾음).
72. (E5) `MechVisualRig` 회전 오프셋은 이제 처음 자세의 부품 방향으로 자동 변환된다. 임시 상자에서는 동작이 이전과 사실상 같다(어긋나면 `Rotation Correction`).
73. (E5.2) **메뉴 `Tools/Apply Bernard Model`** → 저장. `Assets/Models/bernard.fbx`를 `Mech`·`Mech_P2`의 `VisualPlaceholder` 아래 `Bernard`로 넣는다. 임시 부품은 렌더러만 끄고(콜라이더·Rigidbody 그대로), FBX 루트의 회전·배율은 그대로 두고(Bake Axis Conversion을 끈 FBX의 축 변환 회전 유지), 선 자세의 렌더러 바운드로 잰 높이를 임시 부품 전체 높이에 맞춰 배율을 곱하고(콘솔에 두 높이·배율 출력), 발바닥을 캡슐 바닥에 맞추고, `MechVisualRig` 슬롯을 bernard의 같은 이름 뼈로 다시 연결한다(못 찾은 슬롯은 콘솔 경고). 다시 누르면 이전 bernard를 지우고 새로. Ctrl+Z 한 번으로 되돌림. 씬에 직접 끌어다 놓은 bernard는 건드리지 않으니 따로 지운다.
74. (F1) GameManager의 `CombatFeedback` → 새 효과음 칸 `Anchor Fire Clip`(발사)·`Anchor Hit Clip`(명중)·`Anchor Miss Clip`(빗나감). 비우면 무음. 앵커 클로 투사체 수치는 `BernardTuning → 스킬: 앵커 클로 → 발사 / 와이어·발광 표시`(에셋에 없던 칸이라 코드 기본값으로 채워짐).
75. (F3) `MechVisualRig` → 새 칸 `Shin L`·`Shin R`(선택). 비워 두면 플레이 때 기체 아래에서 `Shin.L`·`Shin.R` 이름의 뼈를 찾고, 없으면 무릎 동작만 빠진다. 대기 자세·숨쉬기 수치는 `BernardTuning → 외형 애니메이션 → 대기 자세 / 숨쉬기`.
