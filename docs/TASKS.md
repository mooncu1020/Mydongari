# 로케테스트 작업 목록

순서대로 처리한다. **Phase A = 기존 코드 수정, Phase B = 새로 만드는 것.**
각 태스크의 "완료 조건"을 전부 만족해야 체크한다. 규칙 수치는 `docs/GAME_SPEC.md`를 따른다.

---

## Phase A — 기존 코드 수정

### [x] A0. 1인 전제 구조 걷어내기 (입력·상대·카메라 참조)
뒤의 모든 태스크가 이 위에서 돌아가므로 가장 먼저 한다.

- `InputReader`
  - `.inputactions`의 Player 맵에 `Guard` 액션 추가 (Button). 패드 기본: Attack = 오른쪽 트리거, Guard = 왼쪽 트리거, Skill1 = LB, Skill2 = RB. 키보드: Attack = 마우스 왼쪽, Guard = 마우스 오른쪽(기존 동작). 기존 바인딩은 지우지 않는다.
  - Slide/Quickturn/Run/Skill1/Skill2에 패드 바인딩이 없으니 Skill1/Skill2는 추가. (스틱 제스처는 B2에서.)
  - `AttackHeld`, `GuardHeld`, `GuardPressedThisFrame` 프로퍼티 추가.
  - 사이드스텝 감지에서 `Keyboard.current` 직접 읽기를 제거하고 PlayerInput 액션 기반으로 바꾼다 (A/D 더블탭은 키보드용으로 액션을 통해 유지).
- `MeleeSystem`, `WeaponSystem`에서 `Mouse.current` 직접 읽기를 전부 `InputReader`로 교체.
  - AI(`MeleeDummy`)도 MeleeSystem을 쓰므로, 가드/공격 입력은 "입력 소스"를 통해 들어오게 한다. InputReader가 없는 기체(더미)는 가드 입력 false.
- 상대 참조 도입: 새 컴포넌트 `MechIdentity` (`MyDongari.Core`) — `PlayerIndex`(1/2), `Opponent`(MechIdentity), `Camera`(Transform). GameManager가 두 기체를 서로의 Opponent로 연결한다. 더미 1인 테스트를 위해 Opponent를 인스펙터에서 직접 지정할 수도 있게 한다.
- `CompareTag("Enemy")`/`"Player"`를 쓰는 모든 곳(LockOnSystem, MeleeSystem 펀치·태클, ImpactSlam, Projectile, SprintState 태클 접촉, WeaponSystem 근접 판정, MeleeDummy)을 Opponent 참조 기반으로 교체. 자기 자신은 절대 맞지 않게 한다.
- 카메라: `FindWithTag("MainCamera")`/`Camera.main`(LockOnSystem, WeaponSystem, Projectile, HUDController)을 각 기체의 카메라 참조로 교체. Projectile은 발사자의 카메라를 Init으로 받는다.
- `MechCamera.Start()`의 `Cursor.lockState` 설정은 유지하되, 패드 Look 입력이 프레임레이트에 독립적이 되도록 스틱 입력엔 `Time.deltaTime`×감도를 쓰고 마우스 델타는 기존대로 둔다 (InputReader가 현재 장치가 패드인지 알려준다).

완료 조건: grep으로 `Mouse.current`, `Keyboard.current`, `CompareTag("Enemy")`, `CompareTag("Player")`, `FindWithTag("MainCamera")`, `Camera.main`이 게임 로직(Scripts/, Editor 제외)에서 0건. 기존 1인 + 더미 구성이 SETUP.md 단계대로 연결하면 이전처럼 동작.

### [x] A1. 락온 거리를 기체 기준으로
- `LockOnSystem.UpdateHardLock`의 거리를 카메라 위치가 아니라 기체(`transform.position`) 기준으로. 각도 판정(±60°)은 카메라 정면 기준 유지.
- `hardLockEnterRange` 기본 6, `hardLockExitRange` 7.
- 근접 전환 판정(`WeaponSystem.IsEnemyInMeleeRange`의 5m)을 하드 락온 상태 하나로 통일한다. 거리 값이 두 군데 있으면 안 된다.
- 소프트 락온 탐지도 Opponent 하나만 검사하도록 단순화(원뿔·거리·레이캐스트 조건 유지).

### [x] A2. 부스트 규칙
- 탈진 해제 기준: `CurrentBoost >= maxBoost * exhaustRecoverRatio`(기본 0.5).
- 가드 중(`MeleeSystem.IsGuarding`)에는 회복 정지(회복 지연 타이머도 리셋).
- 가드가 부스트를 소모하던 코드(`guardBoostDrain`) 제거. 탈진이어도 가드는 가능.
- 이름 정리: `BoostGauge.IsOverheated` → `IsExhausted` (참조 전부 수정). `HeatGauge.IsOverheated`는 유지.

### [x] A3. 히트 규칙
- `HeatGauge`에 `CanPunch()` 추가(과열이 아니면 true). 과열 해제는 0%까지 식었을 때(현재 동작 유지).
- 펀치 발동 시 `AddHeat(heatPerPunch)` (기본 6). 과열 중이면 펀치가 나가지 않는다.
- `IsSkillActive`로 냉각을 멈추는 기존 동작이 어디서 쓰이는지 확인하고, 스킬이 히트를 올리지 않는다는 규칙과 충돌하면 QUESTIONS.md에 적는다.

### [ ] A4. 가드 규칙 교체 (`MeleeSystem`)
- 가드 비용 없음. 근접 계열 피해 50% 감쇄(`guardMeleeReduction` 0.5), 투사체 70% 감쇄(`guardProjectileReduction` 0.7) — `ApplyDamage`에 공격 종류 인자(enum `AttackType { Punch, DashAttack, Tackle, Lunge, Slam, PileDriver, Projectile }`)를 추가해 분기.
- 가드로 받을 때마다 피격자 히트 +10(`guardHeatPerBlock`).
- 과열 상태에서 가드로 받으면: 가드 깨짐 → 감쇄 없이 전체 피해, 스턴 `guardBreakStun`(0.9초).
- Lunge, (차징된) Tackle은 가드를 뚫는다: 감쇄 없음, 가드 히트 증가 없음.
- 가드 해제 후 재사용 대기 `guardReraiseDelay`(0.4초). 패리 성공 직후엔 대기 없음.
- 공격 판정 흐름을 한 곳으로 모은다: 공격자 쪽에서 `defender.ReceiveHit(HitInfo)`를 부르면 MeleeSystem이 패리/가드/깨짐/관통을 판단하고 결과(enum `HitResult { Hit, Guarded, GuardBroken, Parried, Clash }`)를 돌려준다. 펀치·태클·슬램·파일드라이버·투사체·(B3의) 런지가 전부 이 함수를 쓴다.

### [ ] A5. 패리 규칙
- 패리 성공 시 피해 0, 히트 증가 없음, 가드 재사용 대기 면제.
- 태클(과 B3의 런지)이 패리당하면: 공격자가 그 공격 피해의 100%(`parryReflectRatio` 1.0)를 받고 넉백. **기존의 공격자 스턴(`attackerStunDuration` 사용 부분) 제거.**
- 펀치가 패리당하면 피해 0만 (반사 없음).

### [ ] A6. 펀치 판정 정리
- 사거리 3m + 정면 부채꼴 ±45°(`punchHalfAngle`) + Opponent만 판정.
- 맞딜 판정 유지.
- 판정 결과를 HitResult로 받아 이벤트를 남길 수 있게 한다 (B6에서 로그 연결).

### [ ] A7. 임팩트 슬램 버그 수정
- 슬램 피해를 `ReceiveHit(AttackType.Slam)`으로 통과시켜 가드(50%)·패리·깨짐이 적용되게 한다. 패리 시 넉백도 없음.

### [ ] A8. 라운드 리셋 완전하게
- 각 시스템에 `ResetForRound()` 추가: BoostGauge, HeatGauge, MeleeSystem(스턴·가드·패리·타이머), AnchorClaw(당김 중단, 잠금 해제, 쿨타임 초기화, 파일드라이버 준비 해제), ImpactSlam(쿨타임), LockOnSystem(ForceRelease), MechStateMachine(Idle), Rigidbody(속도·각속도 0), ExternalControlLock(카운트 0), 회전도 스폰 포인트 회전으로.
- `GameManager.StartRound`에서 두 기체 모두 위의 리셋을 호출.
- 동시 사망 시 무승부 라운드(승수 증가 없이 재시작).

---

## Phase B — 새로 만드는 것

### [ ] B1. 2인 화면 분할 구성
- 패드 2개를 각각 P1/P2에 고정 배정 (PlayerInput 두 개 + 장치 페어링, 또는 PlayerInputManager의 수동 조인). 키보드·마우스는 P1 디버그용으로 허용.
- 카메라 2대, 좌우 화면 분할(viewport rect). 각 MechCamera는 자기 기체만 따라간다. 오디오 리스너는 하나만.
- HUD 2벌(각 화면). HUDController는 자기 기체·자기 카메라 참조만 쓴다. 상대 체력 바(작게)도 각 화면에 표시.
- 씬 YAML은 직접 편집하지 말 것. `Assets/Editor/VersusSceneSetup.cs` 메뉴(`Mydongari/Setup Versus Scene`)를 만들어 현재 씬의 Mech를 복제해 P2를 만들고, 카메라·HUD·스폰 포인트·GameManager·PlayLogger 참조를 연결하게 한다. 실행 결과를 되돌릴 수 있게 Undo 등록. 메뉴로 안 되는 부분은 SETUP.md에 수동 단계로.
- 더미 1인 모드도 남긴다 (GameManager에 `versusMode` 토글).

### [ ] B2. 패드 스틱 제스처
- `InputReader`에 스틱 튕기기 감지기 추가 (GAME_SPEC 2장 정의). 출력: `SprintTriggered`(↑↑), `QuickTurnTriggered`(↓), `SideStepDirection`(←/→), `SlideTriggered`(전력질주 중 중립 후 0.2초 내 ↗/↖ + 방향).
- 카메라 기준이 아니라 **스틱 원래 방향** 기준으로 판정한다.
- 걷기·달리기 판정(크기 0.8)과 튕기기 판정이 서로 오작동하지 않게 한다: 튕기기는 "중립 → 끝 → 중립"이 짧은 시간 안에 일어난 경우만.
- 기존 키보드 경로(Space, Ctrl, R, A/D 더블탭)는 같은 프로퍼티로 합쳐 유지.
- 모든 임계값·시간은 SerializeField.

### [ ] B3. 스러스터 런지
- 새 컴포넌트 `ThrusterLunge` (`MyDongari.Combat.Skills` 또는 `Combat`). 동작은 GAME_SPEC 5장.
- 새 상태 `LungeState`를 MechStateMachine에 추가(돌진 중 이동 입력 무시, ExternalControlLock 존중). 돌진은 AddForce/속도 지정 중 기존 대시 상태(SideStep/Slide)와 같은 방식을 따른다.
- 돌진 중 `LockOnSystem`에 하드 락온 금지 플래그. 빗나간 뒤 `lungeMissLockDelay` 동안 유지.
- 명중 판정: 돌진 경로 앞쪽 캡슐/스피어로 Opponent 접촉 시 `ReceiveHit(AttackType.Lunge)`. 결과가 Parried면 반사 피해 + 넉백, Hit면 상대 스턴 0.5초(히트스톱 이후 시작) 후 돌진 종료.
- `WeaponSystem`의 공격 버튼 분기를 GAME_SPEC 4장 표대로 다시 짠다. 기체별 `rangedMode`(Lunge/Projectile) — 버나드는 Lunge. `TryFire`는 Projectile 모드에서만.

### [ ] B4. 대시 어택 / 버튼 태클
- 전력질주 중 공격 버튼 → 어깨 태클. 차징 1.5초 미만은 `AttackType.DashAttack`(가드에 막힘), 이상은 `AttackType.Tackle`(가드 관통 + 스턴, 기존 TackleChargePower 반영).
- 기존 자동 접촉 태클과 중복 발동되지 않게 한 번만 처리.
- 어깨 태클은 히트를 올리지 않는다.

### [ ] B5. 라운드 연출과 매치 흐름
- 라운드 시작 카운트다운 3초(입력 잠금) → FIGHT. 라운드 종료 표시(승자), 양쪽 승수 표시.
- 매치 종료 화면 → 시작 버튼(아무 패드)으로 재매치(씬 리로드 대신 GameManager 상태 초기화 권장).
- UI는 기존 Canvas 구조에 맞춰 간단히. 화면 분할이므로 중앙 공용 UI 하나 + 각 화면 HUD.

### [ ] B6. PlayLogger 확장
- 2인 기준으로 참조 정리, 프레임 로그에 거리 구간 컬럼 추가.
- 이벤트 로그 `events_*.csv`와 매치 요약(플레이어별 카운트, 거리 구간 체류 시간)을 GAME_SPEC 9장대로.
- 이벤트는 정적 이벤트 버스나 MeleeSystem 이벤트(`OnHitResolved` 등)를 구독하는 방식으로, 전투 코드에 로거 의존을 넣지 않는다.

### [ ] B7. 히트스톱 (여유가 있으면)
- `Time.timeScale` 방식은 화면 분할에서 두 플레이어를 같이 멈추므로 1대1에서는 허용. 명중 결과별 길이(펀치 0.05, 런지·태클 0.08, 맞딜 0.12초 — 조정값).
- 런지 스턴 타이머가 히트스톱 이후부터 흐르도록 unscaled/scaled 시간을 구분.

---

## 작업 메모 (Claude Code가 태스크마다 추가)

- **A0** 완료. 바뀐 파일: `InputSystem_Actions.inputactions`(Guard/StepLeft/StepRight 액션 + 패드 바인딩 추가), `InputReader`, 신규 `Core/MechIdentity`·`Input/ICombatInputSource`, `MeleeSystem`, `WeaponSystem`, `Projectile`(Init 시그니처), `LockOnSystem`, `ImpactSlam`, `SprintState`, `MechController`(Identity), `MeleeDummy`, `HUDController`, `MechCamera`, `GameManager`(LinkOpponents). 위험: `MechIdentity`는 런타임 자동 부착이라 GameManager의 Player1/2가 비어 있으면 상대가 없어 아무 공격도 안 맞는다. 패드 판별(`IsLookFromGamepad`)은 Look 액션의 activeControl 기준.
- **A1** 완료. 바뀐 파일: `LockOnSystem`(Opponent 단일 검사, 기체 기준 거리, 목표 상실 시 카메라 추적 해제), `WeaponSystem`(`meleeAutoRange` 삭제 — 펀치 조건은 `IsHardLocked`만). 위험: 레이캐스트가 트리거를 무시하도록 바뀜, 상대 중심은 루트 콜라이더 bounds 기준(없으면 위치).
- **A2** 완료. 바뀐 파일: `BoostGauge`(`IsOverheated`→`IsExhausted`, `exhaustRecoverRatio` 0.5, 가드 중 회복 정지), `MeleeSystem`(`guardBoostDrain`·탈진 가드 금지 제거), `WalkState`·`RunState`·`SprintState`·`HUDController`(이름 변경 반영). 위험: 낮음. 걷기 중 "탈진 이동 속도 저하"는 기존대로 달리기 이상 봉쇄로만 구현돼 있음(걷기 속도 자체는 그대로).
- **A3** 완료. 바뀐 파일: `HeatGauge`(`CanPunch()`), `MeleeSystem`(`heatPerPunch` 6, 과열 시 펀치 불가, 발동 시 히트 증가). `IsSkillActive`는 아무 데서도 안 켜짐 → QUESTIONS. 위험: 더미는 HeatGauge가 없어 펀치 제한 없음(의도).
