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
