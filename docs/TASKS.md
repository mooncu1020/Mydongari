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

### [x] A4. 가드 규칙 교체 (`MeleeSystem`)
- 가드 비용 없음. 근접 계열 피해 50% 감쇄(`guardMeleeReduction` 0.5), 투사체 70% 감쇄(`guardProjectileReduction` 0.7) — `ApplyDamage`에 공격 종류 인자(enum `AttackType { Punch, DashAttack, Tackle, Lunge, Slam, PileDriver, Projectile }`)를 추가해 분기.
- 가드로 받을 때마다 피격자 히트 +10(`guardHeatPerBlock`).
- 과열 상태에서 가드로 받으면: 가드 깨짐 → 감쇄 없이 전체 피해, 스턴 `guardBreakStun`(0.9초).
- Lunge, (차징된) Tackle은 가드를 뚫는다: 감쇄 없음, 가드 히트 증가 없음.
- 가드 해제 후 재사용 대기 `guardReraiseDelay`(0.4초). 패리 성공 직후엔 대기 없음.
- 공격 판정 흐름을 한 곳으로 모은다: 공격자 쪽에서 `defender.ReceiveHit(HitInfo)`를 부르면 MeleeSystem이 패리/가드/깨짐/관통을 판단하고 결과(enum `HitResult { Hit, Guarded, GuardBroken, Parried, Clash }`)를 돌려준다. 펀치·태클·슬램·파일드라이버·투사체·(B3의) 런지가 전부 이 함수를 쓴다.

### [x] A5. 패리 규칙
- 패리 성공 시 피해 0, 히트 증가 없음, 가드 재사용 대기 면제.
- 태클(과 B3의 런지)이 패리당하면: 공격자가 그 공격 피해의 100%(`parryReflectRatio` 1.0)를 받고 넉백. **기존의 공격자 스턴(`attackerStunDuration` 사용 부분) 제거.**
- 펀치가 패리당하면 피해 0만 (반사 없음).

### [x] A6. 펀치 판정 정리
- 사거리 3m + 정면 부채꼴 ±45°(`punchHalfAngle`) + Opponent만 판정.
- 맞딜 판정 유지.
- 판정 결과를 HitResult로 받아 이벤트를 남길 수 있게 한다 (B6에서 로그 연결).

### [x] A7. 임팩트 슬램 버그 수정
- 슬램 피해를 `ReceiveHit(AttackType.Slam)`으로 통과시켜 가드(50%)·패리·깨짐이 적용되게 한다. 패리 시 넉백도 없음.

### [x] A8. 라운드 리셋 완전하게
- 각 시스템에 `ResetForRound()` 추가: BoostGauge, HeatGauge, MeleeSystem(스턴·가드·패리·타이머), AnchorClaw(당김 중단, 잠금 해제, 쿨타임 초기화, 파일드라이버 준비 해제), ImpactSlam(쿨타임), LockOnSystem(ForceRelease), MechStateMachine(Idle), Rigidbody(속도·각속도 0), ExternalControlLock(카운트 0), 회전도 스폰 포인트 회전으로.
- `GameManager.StartRound`에서 두 기체 모두 위의 리셋을 호출.
- 동시 사망 시 무승부 라운드(승수 증가 없이 재시작).

---

## Phase B — 새로 만드는 것

### [x] B1. 2인 화면 분할 구성
- 패드 2개를 각각 P1/P2에 고정 배정 (PlayerInput 두 개 + 장치 페어링, 또는 PlayerInputManager의 수동 조인). 키보드·마우스는 P1 디버그용으로 허용.
- 카메라 2대, 좌우 화면 분할(viewport rect). 각 MechCamera는 자기 기체만 따라간다. 오디오 리스너는 하나만.
- HUD 2벌(각 화면). HUDController는 자기 기체·자기 카메라 참조만 쓴다. 상대 체력 바(작게)도 각 화면에 표시.
- 씬 YAML은 직접 편집하지 말 것. `Assets/Editor/VersusSceneSetup.cs` 메뉴(`Mydongari/Setup Versus Scene`)를 만들어 현재 씬의 Mech를 복제해 P2를 만들고, 카메라·HUD·스폰 포인트·GameManager·PlayLogger 참조를 연결하게 한다. 실행 결과를 되돌릴 수 있게 Undo 등록. 메뉴로 안 되는 부분은 SETUP.md에 수동 단계로.
- 더미 1인 모드도 남긴다 (GameManager에 `versusMode` 토글).

### [x] B2. 패드 스틱 제스처
- `InputReader`에 스틱 튕기기 감지기 추가 (GAME_SPEC 2장 정의). 출력: `SprintTriggered`(↑↑), `QuickTurnTriggered`(↓), `SideStepDirection`(←/→), `SlideTriggered`(전력질주 중 중립 후 0.2초 내 ↗/↖ + 방향).
- 카메라 기준이 아니라 **스틱 원래 방향** 기준으로 판정한다.
- 걷기·달리기 판정(크기 0.8)과 튕기기 판정이 서로 오작동하지 않게 한다: 튕기기는 "중립 → 끝 → 중립"이 짧은 시간 안에 일어난 경우만.
- 기존 키보드 경로(Space, Ctrl, R, A/D 더블탭)는 같은 프로퍼티로 합쳐 유지.
- 모든 임계값·시간은 SerializeField.

### [x] B3. 스러스터 런지
- 새 컴포넌트 `ThrusterLunge` (`MyDongari.Combat.Skills` 또는 `Combat`). 동작은 GAME_SPEC 5장.
- 새 상태 `LungeState`를 MechStateMachine에 추가(돌진 중 이동 입력 무시, ExternalControlLock 존중). 돌진은 AddForce/속도 지정 중 기존 대시 상태(SideStep/Slide)와 같은 방식을 따른다.
- 돌진 중 `LockOnSystem`에 하드 락온 금지 플래그. 빗나간 뒤 `lungeMissLockDelay` 동안 유지.
- 명중 판정: 돌진 경로 앞쪽 캡슐/스피어로 Opponent 접촉 시 `ReceiveHit(AttackType.Lunge)`. 결과가 Parried면 반사 피해 + 넉백, Hit면 상대 스턴 0.5초(히트스톱 이후 시작) 후 돌진 종료.
- `WeaponSystem`의 공격 버튼 분기를 GAME_SPEC 4장 표대로 다시 짠다. 기체별 `rangedMode`(Lunge/Projectile) — 버나드는 Lunge. `TryFire`는 Projectile 모드에서만.

### [x] B4. 대시 어택 / 버튼 태클
- 전력질주 중 공격 버튼 → 어깨 태클. 차징 1.5초 미만은 `AttackType.DashAttack`(가드에 막힘), 이상은 `AttackType.Tackle`(가드 관통 + 스턴, 기존 TackleChargePower 반영).
- 기존 자동 접촉 태클과 중복 발동되지 않게 한 번만 처리.
- 어깨 태클은 히트를 올리지 않는다.

### [x] B5. 라운드 연출과 매치 흐름
- 라운드 시작 카운트다운 3초(입력 잠금) → FIGHT. 라운드 종료 표시(승자), 양쪽 승수 표시.
- 매치 종료 화면 → 시작 버튼(아무 패드)으로 재매치(씬 리로드 대신 GameManager 상태 초기화 권장).
- UI는 기존 Canvas 구조에 맞춰 간단히. 화면 분할이므로 중앙 공용 UI 하나 + 각 화면 HUD.

### [x] B6. PlayLogger 확장
- 2인 기준으로 참조 정리, 프레임 로그에 거리 구간 컬럼 추가.
- 이벤트 로그 `events_*.csv`와 매치 요약(플레이어별 카운트, 거리 구간 체류 시간)을 GAME_SPEC 9장대로.
- 이벤트는 정적 이벤트 버스나 MeleeSystem 이벤트(`OnHitResolved` 등)를 구독하는 방식으로, 전투 코드에 로거 의존을 넣지 않는다.

### [x] B7. 히트스톱 (여유가 있으면)
- `Time.timeScale` 방식은 화면 분할에서 두 플레이어를 같이 멈추므로 1대1에서는 허용. 명중 결과별 길이(펀치 0.05, 런지·태클 0.08, 맞딜 0.12초 — 조정값).
- 런지 스턴 타이머가 히트스톱 이후부터 흐르도록 unscaled/scaled 시간을 구분.

---

## Phase C — 로케테스트 다듬기

### [x] C1. 튜닝 에셋 통합
- 조정 가능한 전투·이동 수치(이동 속도, 부스트, 히트, 가드, 패리, 런지, 태클, 대시 어택, 펀치, 스킬, 락온, 히트스톱)를 ScriptableObject `BernardTuning` 하나로 옮긴다.
- 두 기체가 같은 에셋을 참조하고, 플레이 중에 값을 바꾸면 바로 반영된다 (매 프레임 읽거나 OnValidate로 갱신).
- 기본 에셋 생성 메뉴, 현재 P1 기체 인스펙터 값을 에셋으로 복사하는 메뉴. `Setup Versus Scene`도 이 에셋을 두 기체에 연결.
- 컴포넌트에 남은 같은 이름의 개별 필드는 제거하거나 `[HideInInspector]` 처리하고, 어느 쪽인지 SETUP.md에 적는다.

### [x] C2. 상태 피드백
- 기체 머리 위 짧은 월드 텍스트 팝업: PARRY, GUARD BREAK, CLASH, STUN, OVERHEAT, EXHAUSTED. 각 플레이어 카메라를 향하고 화면 분할 양쪽에 다 보인다.
- 가드 중·스턴 중·과열 중 상태를 기체 머티리얼 색 틴트로 표시 (원래 색 복귀 보장).
- 효과음 훅: 펀치 명중, 가드로 막힘, 패리, 가드 깨짐, 맞딜, 런지 발동, 태클 명중. AudioClip 필드만, 비어 있으면 무음.
- 전투 코드는 기존 이벤트를 구독만 해서 처리하고, 전투 코드에 피드백 의존을 넣지 않는다.

### [x] C3. 로케테스트 빌드 준비
- PlayLogger 저장 경로: 에디터는 프로젝트 루트/Logs, 빌드는 실행 파일 옆 Logs 폴더.
- 빌드에서 ESC 길게 누르기(1초)로 종료, 마우스 커서 숨김, 해상도는 전체 화면 기본.
- Windows 빌드 설정 체크리스트와 빌드 후 확인 항목을 `docs/BUILD.md`에. 빌드 실행은 사용자가 한다.

---

## Phase D — 연습 모드

### [x] D1. 연습 봇
- `MeleeDummy`에 행동 모드(enum): Idle(가만히), AlwaysGuard(제자리 가드 유지), Approach(다가와서 펀치, 기존 동작), Lunger(6~12m 유지하다 런지), Parrier(상대 공격 직전에 일정 확률로 패리), Mixed(위 행동을 섞어서).
- 더미도 실제 기체와 같은 규칙(`MeleeSystem.ReceiveHit`, 히트, 가드 깨짐, 패리 반사)을 그대로 거친다. 더미 전용 판정을 새로 만들지 않는다.
- 더미 입력은 `ICombatInputSource`를 구현한 봇 입력 컴포넌트로. 런지는 `ThrusterLunge`를 그대로 쓴다.
- Parrier의 패리 확률·반응 시간, Lunger의 거리 같은 수치는 SerializeField.

### [x] D2. 타이틀과 모드 선택
- 실행하면 간단한 타이틀: VERSUS / PRACTICE. 패드 스틱·버튼과 키보드로 선택.
- VERSUS = 지금의 2인 매치. PRACTICE = P1 vs 연습 봇, 한 화면, 라운드 제한 없이 계속. 봇 모드는 화면 구석에 표시.
- 매치가 끝나면 타이틀로 돌아가는 선택지.
- 씬은 하나로 유지하고 GameManager 상태로 전환. `.unity` 직접 편집 금지, 필요한 UI는 기존 Setup 메뉴나 런타임 생성.

### [x] D3. 연습 단축키 (PRACTICE에서만)
- 패드 Select(Back/Share) 또는 키보드 Tab: 봇 행동 모드 순환.
- 패드 Select 길게 또는 키보드 R: 두 기체 체력·부스트·히트·위치 즉시 리셋.
- 키보드 1~4: 내 히트를 0/50/90/100%로 세팅 (과열·가드 깨짐 테스트용).

---

## Phase E — 외형·안정화 (10/27 최종 빌드 확정, 10/29 로케테스트. 확정 후엔 버그 수정만)

### [x] E1. 절차적 애니메이션 (MechVisualRig)
- 기체 루트 아래 시각 전용 부품 계층: Body, Head, Arm_L_Upper/Arm_L_Lower/Fist_L, Arm_R_Upper/Arm_R_Lower/Fist_R, Leg_L, Leg_R. 콜라이더·물리는 루트 그대로, 부품은 보이기만.
- 모델이 오기 전까지 쓸 임시 상자 부품을 만드는 에디터 메뉴 (두 기체, 더미 포함).
- 이벤트 구독으로 부품을 움직인다(전투 코드 의존 금지): 펀치(오른팔 뻗기, 원투면 좌우 교대), 가드(양팔 올림, 패리 성공 시 튕김), 런지(상체 숙이고 양팔 뒤로), 대시 어택·태클(어깨 앞으로), 앵커 클로(왼팔 뻗기), 파일드라이버(오른팔 내리찍기), 슬램(양팔 내리찍기), 스턴(흔들림), 가드 깨짐(팔이 튕겨 벌어짐), 이동(속도에 따라 상체 기울기, 걷기에 맞춰 다리 교차·몸통 상하), 과열 김 파티클(선택).
- 모든 동작은 위치·회전 보간(스프링·감쇠), 크기와 속도는 튜닝 에셋에 묶음 하나.
- 공격 직전 1~2프레임 예비 동작(팔을 살짝 뒤로). 판정 타이밍은 바꾸지 않는다.

### [x] E2. 모델 교체 메뉴
- FBX를 고르면 위 이름의 부품을 찾아 슬롯에 연결하고 임시 상자를 끄는 에디터 메뉴. 이름이 없는 부품은 목록으로 경고.
- 피벗이 관절에 있다고 가정, 슬롯마다 회전 보정 값을 인스펙터에서.
- `docs/MODEL_IMPORT.md`: Blender 내보내기 설정(1단위=1m, Apply Transform, 전방 -Z, 위쪽 Y), 콜라이더 높이·폭, 부품 이름 목록, 피벗 규칙.

### [x] E3. 안정화
- 빌드에서 예외가 나도 멈추지 않게 PlayLogger·피드백·비주얼 코드 방어 처리. 예외는 `Logs/PlayLogs/errors.txt`에 기록.
- 2분 무입력 시 타이틀 복귀 옵션(기본 꺼짐).
- `docs/RELEASE_CHECKLIST.md`: 10/27 최종 빌드 전 확인 항목(메뉴 실행, 튜닝 값, Player Settings, 패드 2개, 로그 생성, 3판 2선승 완주, 타이틀 복귀).

### [x] E6. 전력질주·슬라이딩 두 스틱 조작 (원안 정정)
- 전력질주: 달리기 중 오른쪽 스틱(가속)을 중립에서 0.15초 안에 0.8 이상, 왼쪽 스틱과 ±20° 안으로 꺾으면 진입. 두 스틱 0.8 이상·20° 이하 동안 유지. 각도 초과·어느 스틱이든 놓음·탈진이면 해제. 질주 중 R스틱은 카메라에 안 씀. 수치는 튜닝 에셋.
- 슬라이딩: 스틱을 놓아 질주가 풀린 뒤 0.2초 안에 L ↗(45°±25°) + R ↖(135°±25°) 둘 다 0.8 이상. 대기 중엔 제동으로 안 넘어감.
- 왼쪽 스틱 ↑↑ 질주·왼쪽 스틱만 쓰는 슬라이딩 삭제. 초신지선회·사이드스텝·키보드 조작은 그대로.
- PlayLogger `sprint_enter`, `sprint_exit_*`(이유별).

### [x] E6.1. E6 질문 답 반영
- 안쪽 굴리기 슬라이딩: 각도 해제 때 L이 시계(↗ 쪽)·R이 반시계(↖ 쪽)로 모이며 벌어졌으면 0.2초 슬라이딩 대기, 다른 방향은 바로 제동.
- Sprint 액션 패드 L3 바인딩 삭제 (키보드 Space 유지).

## Phase F — 로케 피드백 반영

### [x] F1. 앵커 클로를 피할 수 있게 (투사체)
- 예비 동작 0.2초(팔 당김 + 손끝 발광) → 발사 순간 상대 위치로 일직선 40m/s·사거리 12m, 와이어 LineRenderer. 유도 없음.
- 빗나가면 감겨 돌아옴. 쿨다운 5초는 발사 순간부터. 발사·명중·빗나감 SFX 슬롯(CombatFeedback).

### [x] F2. 전력질주 풀림과 카메라 튐 수정
- 해제 이유·스틱 값·각도 로그, 원시 스틱끼리 각도 비교, 진입 판정·질주 중·해제 후 R스틱 중립 복귀까지 카메라 회전 입력 차단(카메라는 진행 방향 뒤로), 해제 조건 0.1초 연속, 유지 스틱 크기 기준 분리.

### [x] F3. 대기 자세와 숨쉬기
- 대기 포즈 오프셋, 숨쉬기(Body 상하·가슴, 팔 지연), 걷기 이상이면 0.2초에 걸쳐 끔, 무릎 뼈(Shin.L/R) 선택.

## Phase H — bernard 런타임 구조 (Blender 재내보내기 없음. 이후 G는 이 구조 기준)

### [x] H1. 런타임 뼈 구조 변경 (Root → Pelvis → Body)
### [x] H2. 파츠 누락 점검 (메뉴가 bernard 렌더러를 건드리지 않고 전부 켬)
### [x] H3. 접지 보정 (낮은 발바닥을 콜라이더 바닥에)
### [x] H4. 대기 자세 다리 (벌림·무릎·발목 평행·앞뒤 엇갈림, 숨쉬기 무릎)

## Phase G — 이동 연출 (H 구조 기준)

### [x] G1. 새 선택 슬롯 (Pelvis, Hip, Shin, Ankle, Skirt)
### [x] G2. 상하체 분리 (하체 = 이동 방향, 상체 = 카메라/상대, 비틀림 ±100°, 공격 방향 = 상체 방향, 머리 ±30° 지연)
### [x] G3. 무게감 있는 걸음 (거리 기반 위상, 착지 연출·발소리·카메라 흔들림, 팔 스윙, 가감속 숙임, 스커트)
### [x] G4. 제자리 회전 스텝 (60° 이상 벌어지면 두세 걸음)

## Phase J — 2인 대전 점검

### [x] J1. 뼈/슬롯 탐색이 자기 기체 안에서만 되는지
### [x] J2. 공유 에셋·static에 런타임 상태가 있는지
### [x] J3. P2 락온 떨림

## Phase L — 로케 직전 버그

### [x] L1. VERSUS에서 P2 조작이 안 먹힘

## Phase M — 전신 공격 모션 (MechVisualRig 절차적 포즈, 원래 자세 기준 오프셋·G2·H3 유지, 임시 상자는 예전 동작)

### [x] M0. 공격별 발동/판정/회복 시간 조사 (`docs/MOTION_TIMING.md`)
### [x] M1. 포즈 라이브러리(MechPoseLibrary)와 캡처 도구
### [x] M2. 기본 자세(로우 가드)·전력질주 돌격 자세
### [ ] M3. 기본 펀치(관절 지연 사슬·팔로스루·회복·좌우·앞발 디딤)
### [ ] M4. 스러스터 런지 모션 + 새 판정 방식(정면 타격 거리에서 박기+펀치)
### [ ] M5. 가드·막음·패리·가드 브레이크
### [ ] M6. 앵커 클로 → 파일 드라이버(어퍼컷·피스톤·히트스톱)
### [ ] M7. 대시 공격·태클·슬램

---

## 작업 메모 (Claude Code가 태스크마다 추가)

- **A0** 완료. 바뀐 파일: `InputSystem_Actions.inputactions`(Guard/StepLeft/StepRight 액션 + 패드 바인딩 추가), `InputReader`, 신규 `Core/MechIdentity`·`Input/ICombatInputSource`, `MeleeSystem`, `WeaponSystem`, `Projectile`(Init 시그니처), `LockOnSystem`, `ImpactSlam`, `SprintState`, `MechController`(Identity), `MeleeDummy`, `HUDController`, `MechCamera`, `GameManager`(LinkOpponents). 위험: `MechIdentity`는 런타임 자동 부착이라 GameManager의 Player1/2가 비어 있으면 상대가 없어 아무 공격도 안 맞는다. 패드 판별(`IsLookFromGamepad`)은 Look 액션의 activeControl 기준.
- **A1** 완료. 바뀐 파일: `LockOnSystem`(Opponent 단일 검사, 기체 기준 거리, 목표 상실 시 카메라 추적 해제), `WeaponSystem`(`meleeAutoRange` 삭제 — 펀치 조건은 `IsHardLocked`만). 위험: 레이캐스트가 트리거를 무시하도록 바뀜, 상대 중심은 루트 콜라이더 bounds 기준(없으면 위치).
- **A2** 완료. 바뀐 파일: `BoostGauge`(`IsOverheated`→`IsExhausted`, `exhaustRecoverRatio` 0.5, 가드 중 회복 정지), `MeleeSystem`(`guardBoostDrain`·탈진 가드 금지 제거), `WalkState`·`RunState`·`SprintState`·`HUDController`(이름 변경 반영). 위험: 낮음. 걷기 중 "탈진 이동 속도 저하"는 기존대로 달리기 이상 봉쇄로만 구현돼 있음(걷기 속도 자체는 그대로).
- **A3** 완료. 바뀐 파일: `HeatGauge`(`CanPunch()`), `MeleeSystem`(`heatPerPunch` 6, 과열 시 펀치 불가, 발동 시 히트 증가). `IsSkillActive`는 아무 데서도 안 켜짐 → QUESTIONS. 위험: 더미는 HeatGauge가 없어 펀치 제한 없음(의도).
- **A4** 완료. 바뀐 파일: 신규 `Combat/HitTypes.cs`(AttackType·HitResult·HitInfo), `MeleeSystem`(`ReceiveHit`/`DeliverHit`/`OnHitResolved`, `ApplyDamage(float, AttackType)`, 가드 재사용 대기, 스턴 중 가드 해제), `Projectile`·`AnchorClaw`(DeliverHit 사용). 위험: `ApplyDamage(float)` 단일 인자 시그니처 삭제(참조 전부 교체 확인). 맞딜 판정을 피격자 쪽 ReceiveHit으로 옮김(결과 동일).
- **A5** 완료. 바뀐 파일: `MeleeSystem`(`parryReflectRatio`·`parryReflectKnockback`, 런지·태클 패리 반사, `attackerStunDuration` 삭제, `TryTackle` → `HitResult?` 반환, `CanTackle`·`TackleRecoveryStun`), `SprintState`(결과가 Parried면 자기 경직 없음, 쿨다운 중 접촉은 질주 유지). 위험: 쿨다운 중 접촉 시 예전엔 멈추고 경직됐는데 이제 질주가 이어짐.
- **A6** 완료. 바뀐 파일: `MeleeSystem`(`punchHalfAngle` 45°, Opponent만, `TryMeleeAttack` → `HitResult?` 반환, `OnAttackStarted` 이벤트), `MeleeDummy`(상대 쪽으로 회전). 위험: 기체 정면(transform.forward) 기준이라, 하드 락온이 아닌 상태에서 카메라만 돌리고 기체가 덜 돌았으면 빗나갈 수 있음.
- **A7** 완료. 바뀐 파일: `ImpactSlam`(`DeliverHit(AttackType.Slam)` 경유 — 가드 50%·패리·깨짐 적용, 패리 시 넉백 없음, `TryActivate` → `HitResult?` 반환, `slamKnockbackLift`). 위험: 낮음. WeaponSystem은 반환값을 무시(호환).
- **A8** 완료. 바뀐 파일: `ResetForRound()` 추가 — `BoostGauge`, `HeatGauge`, `MeleeSystem`, `AnchorClaw`, `ImpactSlam`, `MechStateMachine`, `ExternalControlLock.ResetLock()`, `MechCamera.ResetView(yaw)`. `GameManager`(클로 먼저 해제 → 위치·회전·속도·잠금·각 시스템 리셋, 동시 사망 무승부, `OnRoundOver(0=무승부)` 이벤트). 위험: 스폰 포인트 회전이 0이면 두 기체가 같은 방향을 봄(SETUP 12). 라운드 사이 3초 동안 이동 입력은 아직 막혀 있지 않음(B5 카운트다운에서 처리).
- **A9** (질문 반영, TASKS 외 추가 작업) 완료. 바뀐 파일: `.inputactions`(그룹 없던 키보드 바인딩 5개 → Keyboard&Mouse), `HeatGauge`(`IsSkillActive` 삭제, `sprintCoolingPenalty` → `sprintCoolingReduction` 0.7), `HitTypes`·`MeleeSystem`·`AnchorClaw`(파일드라이버 가드 관통), `MechController`·`MechStateMachine`(하드 락온 몸통 자동 회전), `GAME_SPEC.md`(3·1·6·7장). 위험: 몸통 자동 회전 중엔 `RotateToCamera`가 양보하므로 하드 락온 중 카메라와 몸통 방향이 다를 수 있음(의도).
- **B1** 완료. 바뀐 파일: 신규 `Input/VersusInputPairing`, 신규 `Editor/VersusSceneSetup`(메뉴), `GameManager`(versusMode·dummyOpponent·versusOnlyObjects·player1Camera, Player1/Player2/VersusMode getter), `HUDController`(상대 체력 바, Screen Space - Camera 락온 마커 좌표, 최대 HP 기준), `Health`(`MaxHealth`). 위험: 장치 페어링은 InputUser API + `actions.devices` 제한으로 처리 — 에디터에서 두 패드 분리 동작을 꼭 확인. 반쪽 화면 HUD 크기는 Canvas Scaler 설정에 따라 달라질 수 있음(SETUP 19).
- **B2** 완료. 바뀐 파일: 신규 `Input/StickGestureDetector`(설정 클래스 포함), `InputReader`(MoveStick 기반 제스처, RunInput = Shift || 스틱 0.8, `IsMoveInputActive`, `SlideDirection`, 시간은 unscaled), `.inputactions`(`MoveStick`), 상태 7개(이동 판정 하드코딩 0.64 → `IsMoveInputActive`), `SprintState`·`BrakeState`(퀵턴·사이드스텝 허용). 위험: 제스처 수치는 실기 패드로 튜닝 필요. 첫 ↑ 튕기기 동안 잠깐 걷기/달리기 상태가 끼어든다.
- **B3** 완료. 바뀐 파일: 신규 `Combat/ThrusterLunge`, 신규 `Movement/LungeState`, 신규 `Editor/MechComponentSetup`(메뉴), `MechStateMachine`(LungeState·`CanStartLunge`·`IsLunging`), `LockOnSystem`(하드 락온 금지 플래그·타이머, 전체 락온 금지, `ResetForRound`), `WeaponSystem`(GAME_SPEC 4장 우선순위, `RangedMode`), `InputReader`(`AttackPressedThisFrame`), `SlideState`(상대 락온 해제), `GameManager`·`VersusSceneSetup`. 위험: 대시 어택 분기는 B4에서 연결(지금은 질주 중 공격 버튼 무반응). 런지 스턴의 히트스톱 이후 시작은 B7.
- **B4** 완료. 바뀐 파일: `MeleeSystem`(`TryTackle(chargePower, AttackType)` — 참조는 SprintState 1곳, 대시 어택 수치, 대시 어택 패리 반사), `SprintState`(`RequestTackle()`, 버튼·자동 접촉을 FixedUpdate 한 곳에서 한 번만 처리), `WeaponSystem`(우선순위 2 연결). 위험: 대시 어택 수치는 임시값(QUESTIONS).
- **B5** 완료. 바뀐 파일: `GameManager`(`MatchPhase` Countdown/Fight/RoundOver/MatchOver, 입력 잠금, Start로 재매치 `StartMatch`, `OnRoundStarted`·`OnFight`·`OnMatchStarted` 이벤트, `Invoke` 제거), `InputReader`(`SetInputLocked`, `StartPressed`), `.inputactions`(`Start`), `MeleeDummy`(Fight 아닐 땐 정지), 신규 `UI/RoundUI`, 신규 `Editor/RoundUISetup`(메뉴, Versus 세팅에서도 호출). 위험: 라운드 종료 후 재매치 입력은 각 플레이어 InputReader의 Start만 본다(1인 모드는 P1만).
- **B6** 완료. 바뀐 파일: `PlayLogger`(재작성: 이벤트 구독, round·dist_band 컬럼, events/summary CSV, 매치 종료 시 저장, 재매치 시 카운트 리셋), `AnchorClaw`(`OnGrabbed`), `MechStateMachine`(`OnStateChanged`). 전투 코드에서 로거 참조 없음. 위험: 로그 파일을 Assets에 쓰는 기존 방식 유지 — 에디터에서 임포트 대상이 된다.
- **B7** 완료. 바뀐 파일: 신규 `Core/HitStop`(OnHitResolved 구독, timeScale 0을 실제 시간으로 유지), `GameManager`(없으면 자동 부착), `VersusSceneSetup`(GameManager에 추가). 스턴·쿨다운은 scaled 시간이라 히트스톱 동안 멈춤 → 런지 스턴이 히트스톱 뒤부터 흐름. 입력 타이밍(제스처·더블탭)은 이미 unscaled. 위험: 히트스톱 중에도 마우스 시점 회전은 움직인다(카메라 입력은 시간 배율 무관).
- **B8** (질문 반영, TASKS 외 추가 작업) 완료. 바뀐 파일: `MeleeSystem`(`tackleHalfAngle` 45°, `IsOpponentInTackleArc()`, 펀치·태클 부채꼴 판정 공용화), `SprintState`(자동 접촉 태클도 부채꼴 안일 때만), `GAME_SPEC.md`(5장 태클 판정 범위), `QUESTIONS.md`(B 질문 답 기록). 위험: 낮음. 정면 판정은 기체 몸통 방향(transform.forward) 기준이라 질주 중 카메라와 몸통이 어긋나 있으면 체감 범위가 다를 수 있음.
- **C1** 완료. 바뀐 파일: 신규 `Mech/BernardTuning`(ScriptableObject, 묶음 13개), 신규 `Mech/MechTuning`(기체 참조 + `TuningRef` 지연 조회), 신규 `Editor/TuningSetup`(메뉴 2개: 생성·연결, P1 값 복사), `MechController`·상태 5개(SideStep/Slide/QuickTurn/Brake/Sprint의 const 포함), `BoostGauge`, `HeatGauge`(`MaxHeat`), `MeleeSystem`, `ThrusterLunge`, `LockOnSystem`, `AnchorClaw`(박혀 있던 3m/4m도), `ImpactSlam`, `HitStop`, `HUDController`(히트 게이지 /100 → MaxHeat), `VersusSceneSetup`·`MechComponentSetup`(에셋 연결). 기존 필드는 `[HideInInspector]`로 숨김(복사 메뉴용, 런타임 미사용 — 스크립트로 확인). 위험: 에셋 연결 전엔 코드 기본값으로 동작하므로 씬에서 튜닝한 값(예: 하드 락온 추적 속도 10)이 Copy 메뉴 실행 전까지 적용되지 않는다. 플레이 중 바꾼 에셋 값은 멈춰도 남는다.
- **C2** 완료. 바뀐 파일: 신규 `Feedback/CombatFeedback`(이벤트 구독 + 상태 프로퍼티 변화 감지, 팝업·틴트·효과음), 신규 `Feedback/StatusPopup`(TextMesh 월드 텍스트, 카메라별 렌더 직전 회전으로 화면 분할 양쪽 정면), 신규 `Feedback/MechTint`(MaterialPropertyBlock), `GameManager`(자동 부착 한 줄), `MechComponentSetup`·`VersusSceneSetup`(GameManager에 HitStop·CombatFeedback 추가). 전투 코드는 수정 없음. 위험: TextMesh 기본 폰트 머티리얼(GUI/Text Shader)이 URP에서 그려지는지 에디터 확인 필요. 틴트는 `_BaseColor`가 있는 셰이더에서만 보임.
- **C3** 완료. 바뀐 파일: `PlayLogger`(저장 경로 = `Application.dataPath`의 부모/Logs — 에디터는 프로젝트 루트, 빌드는 실행 파일 옆, 폴더 자동 생성, 쓰기 실패 시 경고만), 신규 `Core/AppRuntime`(ESC 1초 홀드 종료 + 진행 표시, 빌드 커서 숨김·잠금, 시작 시 FullScreenWindow 네이티브 해상도), `GameManager`(자동 부착), `MechComponentSetup`(GameManager에 AppRuntime), 신규 `docs/BUILD.md`. 위험: Run In Background가 꺼져 있어 BUILD.md대로 켜지 않으면 포커스를 잃을 때 게임이 멈춘다. 빌드 결과는 아직 확인 안 됨(빌드는 사용자).
- **C4** (질문 반영, TASKS 외 추가 작업) 완료. 바뀐 파일: `PlayLogger`(`logSubfolderName` = PlayLogs — 씬에 `logFolderName`이 저장돼 있어 기본값 변경 대신 새 필드), `CombatFeedback`(STUN 팝업을 프로퍼티 감시 → 피격 이벤트(Hit + 스턴)·앵커 잡기 이벤트로, `lungeHitClip`), `BUILD.md`. 위험: 낮음. 가드 깨짐 스턴은 GUARD BREAK로만 표시.
- **D1** 완료. 바뀐 파일: 신규 `Input/BotInput`(ICombatInputSource), `MeleeDummy`(연습 봇 두뇌, `BotMode` 6종, 실행 순서 -50, 필요한 부품 런타임 부착, 상대 공격 이벤트 구독으로 패리), `MeleeSystem`(`SetInputSource`, `RebindComponents`, `ApplyGuardInputNow` — 모두 범용, 판정 로직 변경 없음), `ThrusterLunge`(상태 머신 없는 기체는 스스로 돌진 진행), `HeatGauge`·`BoostGauge`(상태 머신 없이도 냉각·회복). 위험: 봇의 런지 중에도 부스트가 회복된다(상태 머신이 없어 "정지/걷기"로 봄). 봇이 질주를 안 하므로 질주 관련 규칙은 봇에 해당 없음.
- **D2** 완료. 바뀐 파일: `GameManager`(`GameMode`, `MatchPhase.Title`, 모드별 배치 `ApplyLayout`·패드 재배정·상대 재연결, 타이틀/매치 종료/연습 메뉴, 연습 KO 무제한 루프, `ResetPracticeNow`, `AllMechs`, `OnReturnedToTitle`), 신규 `UI/MenuUI`(런타임 캔버스), `RoundUI`(타이틀 숨김·연습 표시), `InputReader`(`MenuVertical`, `MenuConfirmPressed`, `ClearFrameInput`), `VersusInputPairing`(`Apply(versus)`, 연습은 패드 0 → P1), `HitStop`·`CombatFeedback`·`PlayLogger`(P2·봇 모두 구독, 모드별 요약), `.inputactions`(`Submit`, `MenuNavigate` 추가). 위험: 봇이 꺼진 채 시작하면(2인 배치) 봇 부품 런타임 부착이 Awake 순서(-50)에 의존 — 연습 모드에서 봇 런지 팝업·로그가 안 나오면 이 부분. 2인 패드 확인은 아직.
- **D3** 완료. 바뀐 파일: `.inputactions`(`Select`, `BotCycle`, `PracticeReset`, `HeatPreset1~4` 추가만), `InputReader`(`BotCyclePressed`, `PracticeResetPressed` — Select 짧게/길게 구분, `HeatPresetPressed`), 신규 `Core/PracticeHotkeys`(PRACTICE의 Fight 중에만 실행), `HeatGauge`(`SetHeatRatio` 연습용), `GameManager`(자동 부착, 리셋은 D2의 `ResetPracticeNow`), `MenuUI`(단축키 안내). 위험: 키보드 R 겹침(QUESTIONS). 패드 Select 버튼 이름은 패드마다 다름(Back/View/Share) — Input System의 `<Gamepad>/select`로 잡히는지 실기 확인 필요.
- **D4** (질문 반영, TASKS 외 추가 작업) 완료. 바뀐 파일: `.inputactions`(`PracticeReset` 키보드 R → Backspace), `MenuUI`(타이틀 DRAFT HOUND PATRASCHE, 안내 문구), 주석(`InputReader`·`PracticeHotkeys`·`GameManager`), `QUESTIONS.md`(C1·D1~D3 답 기록). 위험: 낮음.
- **C5** (정리, TASKS 외 추가 작업) 완료. 바뀐 파일: `MechController`, `BoostGauge`, `HeatGauge`, `MeleeSystem`, `ThrusterLunge`, `LockOnSystem`, `AnchorClaw`, `ImpactSlam`, `HitStop`(숨긴 예전 필드 블록 삭제), `Editor/TuningSetup`(복사 메뉴·필드 대응표 삭제, 생성·연결 메뉴만 남김), `BUILD.md`. 위험: 낮음. 씬 YAML의 예전 값은 다음 저장 때 Unity가 정리.
- **E1** 완료. 바뀐 파일: 신규 `Visual/MechVisualRig`(슬롯 10개, 감쇠 스프링, 이벤트 구독 + 상태 읽기, 쉴 때 자세 위에 오프셋, 슬롯별 회전 보정), 신규 `Editor/VisualRigSetup`(임시 상자 메뉴, 슬롯 연결 함수 — E2에서 재사용), `BernardTuning`(`Visual` 묶음), `AnchorClaw`(`OnPileDriverStarted`), `ImpactSlam`(`OnSlamStarted`). 전투 판정 변경 없음. 위험: 동작 방향(각도 부호)은 상자 기준으로 맞췄고 화면 확인 전 — 어색하면 Visual 묶음 각도로 조정. 과열 파티클 생략.
- **E2** 완료. 바뀐 파일: 신규 `Editor/ModelImportSetup`(`Apply Model To Mechs` — FBX 선택, `VisualModel` 인스턴스, 이름으로 슬롯 연결, 못 찾은 이름 경고, 임시 상자 끔, 모델 콜라이더 끔, 재실행 시 교체), 신규 `docs/MODEL_IMPORT.md`(크기·이름·계층·피벗·축·Blender/Unity 설정·문제 해결). 위험: Blender 축 설정에 따라 정면이 뒤집힐 수 있어 문서에 확인 단계를 넣음. 실제 모델로는 아직 확인 안 됨.
- **E3** 완료. 바뀐 파일: 신규 `Core/ErrorLog`(잡은 예외·처리 안 된 예외를 `errors.txt`에, 반복은 2의 거듭제곱 번째만), `PlayLogger`·`HitStop`·`CombatFeedback`·`StatusPopup`·`MechVisualRig`·`MeleeDummy`(이벤트 처리·매 프레임 처리 try/catch — 구독 쪽 예외가 전투 코드로 번지지 않게), `GameManager`(`ErrorLog.Hook`, 무입력 타이틀 복귀 옵션), `InputReader`(`AnyInputThisFrame`), 신규 `docs/RELEASE_CHECKLIST.md`. 위험: 낮음. 예외가 나면 해당 기능만 조용히 빠지므로 빌드 확인 때 `errors.txt`를 꼭 본다.
- **E4** (질문 반영 — 공격 발생 시간, TASKS 외 추가 작업) 완료. 바뀐 파일: `BernardTuning`(`Startup` 묶음: 펀치 0.1·대시 어택 0.12·차징 태클 0.1·맞딜 창 0.1), `MeleeSystem`(펀치 시작/판정 분리, 발생 중 스턴 취소 `OnAttackCanceled`, 스턴 중 펀치 시작 불가, 맞딜 = 판정 시점 0.1초 이내, 태클 `BeginTackle`/`ResolveTackle`/`TackleStartup`/`CancelTackle`), `SprintState`(태클 발생 시간 동안 질주 유지 후 판정, 퇴장 시 취소), `MechVisualRig`(예비 동작 길이 = 발생 시간, 취소 시 뻗기 생략), `MeleeDummy`(Parrier 반응 시간 방식, 기본 0.08초), `PlayLogger`(`*_canceled`), `GAME_SPEC.md`(5장 발생 시간, 맞딜), `QUESTIONS.md`(E 답, 런지 후속타 계산). 위험: 판정이 0.1초 늦어져 체감이 달라진다 — 어색하면 값을 0으로 끄면 예전과 같다.
- **E4.1** (공격 입력 = 누른 순간 + 입력 버퍼, TASKS 외 추가 작업) 완료. 바뀐 파일: 신규 `Combat/AttackInputBuffer`(누른 시각 기억, 매 프레임 발동 시점 상황으로 재판단), `WeaponSystem`(AttackHeld 반복 제거, 버퍼, 스턴 중 공격 불가, `OnBufferedAttack`, Projectile 사격만 연사 예외), `MeleeSystem`(`TryMeleeAttack` → bool), `AnchorClaw`(`TryPileDriver` → bool), `SprintState`(`RequestTackle` → bool), `BotInput`(`PressAttack`), `MeleeDummy`(같은 버퍼·같은 펀치/런지 판단), `PlayLogger`(`buffered_attack`), `BernardTuning`(`inputBuffer` 0.15), `GAME_SPEC.md`(4장 입력 규칙). 위험: 플레이 감각이 바뀐다(꾹 누르기 안 됨). 버퍼 0이면 버퍼 없이 누른 순간만.
- **E5** (리깅 모델 지원, TASKS 외 추가 작업) 완료. 바뀐 파일: `MechVisualRig`(Animator 칸·자동 탐색, 기준 자세 = Animator 현재 자세 / 안 건드린 뼈는 쉴 때 자세(정확 비교로 누적 방지), Speed·MoveState 파라미터 구동, 클립 재생 중이면 코드 걷기 끔, 오프셋 축 자동 변환), `Editor/VisualRigSetup`(뼈 우선 탐색 `FindSlotTransform`), `Editor/ModelImportSetup`(Animator 연결·컨트롤러 부착), 신규 `Editor/AnimatorSetup`(`Create Locomotion Animator Controller`), `MODEL_IMPORT.md`(8장 리깅 방식). 위험: 실제 리깅 FBX로는 아직 확인 안 됨 — 특히 Optimize Game Objects·Only Deform Bones 설정이 틀리면 뼈를 못 찾음(문서 8-6).
- **E6** (스틱 조작 원안 정정 — 전력질주·슬라이딩 두 스틱) 완료. 바뀐 파일: `InputSystem_Actions.inputactions`(오른쪽 스틱만 바인딩된 `LookStick` 액션 추가), `InputReader`(`MoveStickRaw`·`LookStickRaw`, Sprint/SlideInput은 버튼 경로만, `SlideDirection` 삭제), `StickGestureDetector`(↑↑·↗↖ 판정 삭제), 신규 `Movement/TwinStickSprintInput`(진입 추적·유지·슬라이딩 판정, `SprintExitReason`), `MechStateMachine`(`TwinStick` 매 프레임 갱신), `RunState`(두 스틱 진입), `SprintState`(두 스틱 유지·해제, 0.2초 슬라이딩 대기, 해제 이유 이벤트), `BrakeState`(주석), `BernardTuning`(이동: 질주·슬라이딩 두 스틱 수치), `PlayLogger`(`sprint_enter`, `sprint_exit_*`, `slide`), `GAME_SPEC.md` 2·9장, `QUESTIONS.md`. 봇은 상태머신·InputReader를 안 써서 영향 없음(MeleeDummy는 리지드바디 직접 이동). 위험: 패드 실기로 아직 확인 안 됨 — 진입 시간 0.15초가 빡빡하면 `sprintEntryTime`을 늘린다. 스틱을 굴려서 슬라이딩하면 각도 해제로 제동된다(QUESTIONS E6).
- **E6.1** 완료. 바뀐 파일: `TwinStickSprintInput`(`AngleRoll` 이유, `IsInwardRoll` — 마지막 유지 프레임의 두 스틱 사이 방향 기준, L은 시계·R은 반시계로 둘 다 돌아야 함), `SprintState`(유지 방향 기억, 안쪽 굴리기면 대기 + 그 프레임에 바로 슬라이딩 판정), `PlayLogger`(`sprint_exit_angle_roll`), `InputSystem_Actions.inputactions`(Sprint L3 바인딩 삭제), 주석·`GAME_SPEC`·`QUESTIONS`(E6 답 기록)·`TEST_CHECKLIST`. 위험: 한쪽 스틱만 먼저 크게 돌리면(다른 쪽이 아직 그대로) 각도 해제 순간 "둘 다 돎"이 아니라 제동된다 — 실기에서 자주 그러면 기준을 "한쪽은 돌고 다른 쪽은 반대로 안 돎"으로 완화.
- **E5.1** (주먹 슬롯 선택 사항, TASKS 외 추가 작업) 완료. 바뀐 파일: `MechVisualRig`(`OptionalSlotNames`·`IsOptionalSlot`, `GetPunchPoint` — 주먹 → 아래팔 끝 → 루트, 선택 시 빨간 구 기즈모), `Editor/VisualRigSetup`(`AssignSlots`가 주먹은 경고 목록에서 빼고 따로 담음), `Editor/ModelImportSetup`(주먹 없음 안내), `MODEL_IMPORT.md`. 펀치 판정(`MeleeSystem.ResolvePunch`)은 여전히 기체 루트 중심이라 `GetPunchPoint`를 쓰는 곳은 아직 없음. 위험: 끝 뼈 없는 리그는 '아래팔 = 위팔 길이' 근사.
- **E5.2** (bernard 모델 적용 메뉴, TASKS 외 추가 작업) 완료. 신규 `Editor/BernardModelSetup`(`Tools/Apply Bernard Model` — Mech·Mech_P2 이름으로 찾음, 임시 부품 렌더러만 끔, 메시 경계로 높이 비교 배율, 캡슐 바닥에 발 맞춤, `AssignSlots` 재사용, 이전 bernard 프리팹 인스턴스 삭제), `SETUP.md` 73, `TEST_CHECKLIST`. 위험: bernard의 메시 높이에 안테나 등 돌출부가 있으면 키 기준이 달라져 조금 작게 맞춰짐 — 그럴 땐 배율을 손으로 조정.
- **F1** 완료. 바뀐 파일: `AnchorClaw`(단계 Ready/Windup/Flying/Retracting/Pulling, SphereCastAll로 지나간 길 판정, 발사 위치 = 리그 `GetPunchPoint(false)`, 와이어·클로 머리·발광을 기체 밖 오브젝트로 생성(Sprites/Default), 이벤트 `OnWindupStarted`·`OnFired`·`OnMissed`), `BernardTuning`(앵커 클로 발사·표시 수치), `MechVisualRig`(예비 동작 팔 당김·비행 중 팔 뻗기), `CombatFeedback`(SFX 3칸), `PlayLogger`(`anchor_fire`·`anchor_miss`), `GAME_SPEC` 7장, `SETUP` 74, `QUESTIONS`. 위험: 예전엔 락온만 되면 50m에서도 잡혔는데 이제 12m 제한 — 연습 봇 등 거리가 먼 상황에서 체감이 달라짐. 대기 0.2초 동안 상대가 리그에 없는 위치로 사라지면(파괴) 정면으로 쏨.
- **F2** 완료. 바뀐 파일: `TwinStickSprintInput`(`LeftWeak`·`RightWeak` 이유, 유지 기준 `sprintHoldStickThreshold`, `IsJudgingEntry`), `SprintState`(해제 디바운스 `sprintExitConfirmTime`, 처음 어긋난 순간의 이유·스틱 값 `ExitLeftStick`/`ExitRightStick`), `MechStateMachine`(`IsLookInputBlocked`·`IsCameraFollowingVelocity`), `MechCamera`(차단·진행 방향 따라가기, 속도 튜닝 값), `PlayLogger`(sprint_exit detail, left/right_weak), `BernardTuning`, `GAME_SPEC` 2장, `QUESTIONS`. 각도 비교는 원래부터 원시 스틱끼리였음(확인만). 위험: `ConsumeEntry` 정수 넘침 버그는 그대로 둠(QUESTIONS F2).
- **F3** 완료. 바뀐 파일: `MechVisualRig`(`PoseIdle` — 대기 자세 오프셋·숨쉬기(몸통 상하·가슴·팔 지연), 정지 아니거나 동작 중이면 숨쉬기 `breathFadeTime`에 걸쳐 끔, `IsActionPoseActive`로 가드·공격·스턴 중 대기 자세 제외, 선택 슬롯 `shinL`·`shinR` + 이름으로 자동 탐색), `BernardTuning`(Visual: 대기 자세·숨쉬기 12칸), `SETUP` 75, `QUESTIONS`. 위험: bernard 뼈 축이 임시 상자와 다르면 각도 방향이 반대로 보일 수 있음(값 부호로 조정).
- **E5.3** (Apply Bernard Model 수정) 완료. 바뀐 파일: `Editor/BernardModelSetup`(FBX 루트 회전·배율 유지, 렌더러 바운드로 선 자세 높이 측정 후 배율 곱하기 → 다시 재서 발바닥 맞춤, 높이·배율·맞춘 뒤 높이 로그, 이전 인스턴스 이름으로도 삭제), `SETUP` 73, `TEST_CHECKLIST`. 원인: 회전·배율을 0/1로 덮어써서 누운 상태 두께로 배율을 계산했음.
- **F2.1** 완료. 바뀐 파일: `TwinStickSprintInput.ConsumeEntry`(진입 입력 없음 `int.MinValue`를 빼기 전에 거름 — 정수 넘침으로 0.15초 빠르게 꺾기 조건이 무시되던 버그), `QUESTIONS` F2 답. 위험: 지금까지 천천히 밀어도 들어가던 질주가 스펙대로 빡빡해짐 — 테스터 체감이 달라지면 `sprintEntryTime`을 늘린다.
- **E5.4** 완료. 바뀐 파일: `Editor/BernardModelSetup`(회전 계산 대신 `ModelLocalRotation` 필드(-90,0,0)를 그대로 넣음, 그다음 높이 측정·발바닥 맞춤), `SETUP` 73. 이전엔 회전을 계산하지 않고 `Apply`에서 FBX 프리팹 루트의 `localRotation`을 그대로 복사했음.
- **E5.5** 완료. 원인: `MechVisualRig.Apply`가 위치 오프셋(기체 축 m)을 회전만 바꿔 부모 로컬 좌표에 그대로 더해서, 부모 뼈 배율이 1이 아닌 bernard에선 숨쉬기·걷기 상하가 배율만큼 커져 몸통이 뜨고 메시가 늘어났음(회전은 원래부터 원래 회전 * 오프셋). Animator는 컨트롤러 에셋이 프로젝트에 없어 원인 아님. 바뀐 파일: `MechVisualRig`(위치 오프셋을 부모 `InverseTransformVector`로 변환, `ParentFromMech` 삭제, 컨트롤러 없는 Animator는 Awake에서 끔), `Editor/BernardModelSetup`(bernard Animator 컨트롤러 비우고 끔, 리그 Animator 칸 비움 — AnimatorSetup.Configure 호출 제거). 위험: 플레이로 확인 안 됨.
- **H1** 완료. 바뀐 파일: `MechVisualRig`(Awake에서 `RestructureLowerBody` — Pelvis가 Body 아래에 있으면 Pelvis → Body 원래 부모(Root), Body → Pelvis로 `SetParent(…, true)` 후 `BindJoints`, `FindBone` 공용). bernard FBX 실제 계층: Mecha_Rig(-90, ×100) → Root → Body → Pelvis → Hip → Leg → Shin → Ankle, 메시는 뼈 자식(스킨 아님). 위험: 슬롯 `Body`가 비어 있으면 바꾸지 않음.
- **H2** 완료. 바뀐 파일: `Editor/BernardModelSetup`(임시 부품 렌더러 끄기·높이 측정에서 `IsUnderModel` 제외, 새 bernard 아래 꺼진 렌더러를 전부 켜고 이름 출력 — Blender 숨김이 Import Visibility로 꺼져 들어오는 경우). 켜기는 높이 측정 전이라 숨겨졌던 파츠도 키에 들어간다.
- **H3** 완료. 바뀐 파일: 신규 `Visual/MechVisualRig.Grounding.cs`(partial — 발목 뼈(없으면 다리 뼈) 아래 메시 경계 꼭짓점을 Awake에 뼈 기준으로 캐시, LateUpdate 마지막에 낮은 발바닥 vs 기체 루트 콜라이더 바닥, 비주얼 루트 Y만 0.05초 시간 상수로 보정, `OnGroundSample`), `MechVisualRig`(partial, 선택 슬롯 `ankleL/R` 자동 탐색, `BindJoints` 끝에 `SetupGrounding`, OnDisable 복원), `BernardTuning`(접지 보정 4칸), `PlayLogger`(`ground_offset` 이벤트), `Editor/BernardModelSetup`(발 기준 = 루트 콜라이더 바운드 최저점). 위험: 렌더러 대신 메시 경계로 잼(꺼진 임시 부품도 재려고).
- **H4** 완료. 바뀐 파일: `MechVisualRig`(`PoseIdleLegs` — 다리 벌림 6°·무릎 12°·왼발 앞 0.15m 상당(다리 길이로 각도), 걸음 세기 `_gaitWeight`만큼 줄임, 숨쉬기 무릎 `breathKneeAngle`, 스프링 뒤 `LevelAnkle`로 발목이 다리·정강이 기울기를 되돌림), `MechVisualRig.Grounding`(다리 길이 측정), `BernardTuning`(대기 자세 다리 4칸·숨쉬기 무릎, idleKneeBend 기본 10→12), `SETUP` 76. 위험: 발목 평행은 작은 각도 근사(각도 합을 빼기).
- **G1** 완료. 바뀐 파일: `MechVisualRig`(선택 슬롯 pelvis·hipL/R·skirtFront/Back/SideL/SideR + 이름 자동 탐색(Shin·Ankle은 H3·F3에서), `HasLowerBody`, 관절 순서(골반 먼저·발목은 사슬 뒤), 발목 평행 보정 사슬에 골반·엉덩이 포함, H1 구조 변경도 pelvis 칸 우선). 필수 슬롯 목록(`SlotNames`)은 그대로라 메뉴 경고 없음.
- **G2** 완료. 신규 `Core/MechFacing.cs`(`IUpperBodyFacing`, `MechFacing.AttackForward` — 상하체 분리 리그면 상체 방향, 아니면 transform.forward), 신규 `Visual/MechVisualRig.Locomotion.cs`(하체 = 이동 방향·회전 속도 상한·뒷걸음, 상체 = 카메라/하드 락온 상대, 비틀림 ±100° 넘으면 하체 끌림, 머리 ±30°·0.2초 지연, 순간이동 시 맞춤), `MechVisualRig`(`IUpperBodyFacing` 구현, `Joint.Yaw`), `BernardTuning`(상하체 분리 8칸). 공격 방향 transform.forward → AttackForward로 바꾼 곳: `MeleeSystem.IsInFrontArc`(펀치·태클 부채꼴), `ImpactSlam.TryActivate`(슬램 부채꼴), `AnchorClaw.Fire`(목표 없을 때), `ThrusterLunge.BeginDash`(카메라 없을 때), `MeleeDummy.IsAimed`(봇 조준), `WeaponSystem.TryFire`(카메라 없을 때 사격). 위험: 하드 락온 중 상체가 기체 루트보다 빨리 상대를 향해 펀치 부채꼴 판정이 조금 후해질 수 있음.
- **G3** 완료. 바뀐 파일: `MechVisualRig.Locomotion`(`PoseGait` — 거리 기반 위상·보폭 걷기/달리기/질주, 엉덩이 각도 = 보폭·다리 길이, 스윙 ease-in 들기·빠른 내려놓기 무릎, 착지 이벤트 `OnFootstep`, 착지 꺼짐 = 디딘 다리 무릎 추가 굽힘(`KneeBendForDrop`), 상체 0.1초 지연, 골반 기울임, 팔 반대 스윙·팔꿈치 지연, 가감속 숙임, 스커트 느슨한 스프링), `MechVisualRig`(하체 있으면 `PoseGait`/없으면 예전 `PoseMovement`, 스커트 스프링 분기, 숨쉬기 복귀 `stopBlendTime`), `MechVisualRig.Grounding`(쓰지 않게 된 의도적 하강 제거), `CombatFeedback`(`Footstep Clip`·`Footstep Volume`), `MechCamera`(`AddShake`, 내 기체 발소리 흔들림), `BernardTuning`(무게감 걸음·착지·팔 스윙·가감속·스커트 23칸). 위험: 실제 bernard 뼈 축으로 각도 방향 확인 안 됨 — 거꾸로면 각 값 부호로 조정.
- **G4** 완료. 바뀐 파일: `MechVisualRig.Locomotion`(`UpdateTurnSteps` — 서 있고 상하체가 60° 이상 벌어지면 2~3걸음(각 0.25초, 남은 각도 ÷ 남은 걸음)으로 하체 yaw를 끊어 돌림, 도는 쪽 발부터 번갈아 들기 `PoseTurnStep`, 걸음마다 `Land`로 발소리·흔들림, 움직이면 취소), `BernardTuning`(제자리 회전 스텝 6칸). 위험: 디딘 발도 골반과 같이 돌아 바닥에서 약간 비틀려 보일 수 있음(IK 없음).
- **J1** 완료. 점검 결과: 런타임(`MechVisualRig`·H1 구조 변경·G1 자동 탐색·접지 보정·`MechFacing`)엔 GameObject.Find/FindObjectOfType/FindObjectsByType/씬 이름 검색이 없음 — 전부 자기 기체 `GetComponentsInChildren`/`GetComponent`. 씬 검색은 에디터 메뉴뿐(`BernardModelSetup`·`ModelImportSetup`·`AnimatorSetup`의 `FindObjectsByType<MechVisualRig>`는 기체 자체를 찾는 용도, 뼈는 각 기체 bernard 아래에서만 찾음). 바뀐 파일: `MechVisualRig`(`OwnBone` — 슬롯 뼈·구조 변경 대상이 자기 자식이 아니면 에러 후 비움), `Editor/BernardModelSetup`(연결 후 슬롯 소유 검사). 원인 아님(방어만 추가).
- **J2** 점검만(코드 변경 없음). `BernardTuning`(ScriptableObject)에 실행 중 쓰는 곳 없음 — 모든 컴포넌트가 읽기만 하고, 위상·스프링·타이머·속도는 전부 각 컴포넌트 인스턴스 필드(`MechVisualRig`의 Joint·_gaitPhase 등). `TuningRef`는 컴포넌트마다 가진 구조체 필드. static 필드는 `GameManager.Instance`·`ErrorLog`(폴더·훅)·`StatusPopup._font`(공용 폰트)·`BernardTuning._fallback`(읽기 전용 기본 에셋)뿐이라 기체별 상태 없음. 틴트(`MechTint`)는 공유 머티리얼 대신 기체별 `MaterialPropertyBlock`, 앵커 클로 머티리얼은 기체별 생성. 원인 아님.
- **J3** 완료. 점검: 씬의 Mech·Mech_P2 Rigidbody Interpolate 둘 다 None(`m_Interpolate: 0`) — 같았음. 락온 기준점은 상대 루트(소프트는 콜라이더 중심)라 뼈 아님. 몸체 자동 회전(FixedUpdate, 루트 MoveRotation)과 카메라(LateUpdate, 카메라 yaw)는 서로 다른 것을 돌림. 바뀐 파일: `MechController`(`forceInterpolation` — 시작 시 Interpolate, `ApplyHardLockFacing`이 보간 트랜스폼 대신 두 rb.position 사용), `MechCamera`(하드 락온 방향을 이번 프레임 위치에서 계산, 1° 딱 붙이기/따라가기 번갈아 하던 것을 지수 감쇠 하나로), `MechVisualRig.Locomotion`(가속도를 FixedUpdate에서 물리 스텝마다 계산), `MechVisualRig.Grounding`(보간 위치 기준 바닥). 위험: 보간을 켜면 Update에서 읽는 transform.position이 물리 위치보다 최대 한 스텝 뒤 — 펀치 OverlapSphere 중심이 질주 중 최대 0.4m 늦을 수 있음(판정 수치는 그대로).
- **L1** 완료. 원인: `InputSystem_Actions`가 프로젝트 전역 액션(`EditorBuildSettings`)이고 두 PlayerInput의 Default Map이 비어 있음. 먼저 초기화된 P1은 전역 원본(시작 시 자동으로 켜짐)을 쓰고, P2 PlayerInput은 같은 에셋이라 복사본을 받는데 Default Map이 없어 그 복사본을 켜는 곳이 없었음 → P2 액션이 전부 꺼진 채 ReadValue가 0, 에러 없음. 페어링은 정상이라 Input Debugger에선 멀쩡해 보임. 어느 쪽이 복사본을 받는지는 초기화 순서라, 어제 마지막 커밋에서 씬을 다시 만들며(Mech_P2 재생성) 순서가 바뀐 것으로 보임(어제 작업본은 커밋에 없어 확인 불가). 바뀐 파일: `InputReader`(`gameplayActionMap` "Player", Start·패드 배정 때 `EnsureGameplayActionMap`으로 현재 맵 지정·켜기, `DescribeBinding`), `VersusInputPairing.Pair`(배정 후 맵 맞춤 — P2가 꺼졌다 켜져도), `MeleeSystem`(입력 소스를 InputReader 우선으로 — BotInput이 같이 붙어도 사람 입력, `InputSourceName`), `GameManager`(VERSUS 진입 시 `[VersusInput]` 기체별 한 줄, 임시 추적 `traceP2Input`), 신규 `Input/InputTrace`(임시 `[P2Trace]` 단계별 로그, 0.25초 간격, 카운트다운+3초), `MechStateMachine`·`MechController`·`MechCamera`(추적 로그 한 줄씩). 위험: P1도 이제 현재 맵이 Player로 지정됨(전역 UI 맵은 그대로 켜짐) — 메뉴는 Player 맵 액션만 쓰므로 영향 없어야 함. 확인 끝나면 `GameManager → Trace P2 Input` 끄기.
- **M0** 조사만(코드 변경 없음). 결과 `docs/MOTION_TIMING.md`. 판정이 누른 순간인 파일 드라이버·슬램은 모션이 판정보다 늦게 보일 수밖에 없어 M6에서 히트스톱만 외형 타격 순간으로 늦춘다(판정·피해 시간 그대로).
- **M1** 완료. 신규 `Visual/MechPoseLibrary`(이름 있는 포즈 = 관절 15개 회전 오프셋 + 골반 위치, 오른손잡이 기준·좌우 반전, 이징 커브 5개, 초안 포즈 26개, Resources → 코드 초안 순 기본값), 신규 `Visual/MechVisualRig.Motion`(키 = 포즈·도착 시각·이징, 관절별 앞당김(lead)·하체 마스크, 밑에 깔린 자세와 섞기, 모션 관절 스프링 우회, 미리보기·리그 멈춤·캡처, 플레이 전 계산용 변환 함수), `MechVisualRig`(Joint `MotionWeight`·`DirectPosition`, 멈춤·미리보기 분기, 스프링 뒤 우회), `BernardTuning`(`Motion` 묶음: enabled·springBypass), 신규 `Editor/MechPoseEditor`(`Tools/Mech Pose Editor`, `Tools/Create Mech Pose Library (초안 값)`). 아직 어떤 동작도 라이브러리를 쓰지 않음(M2부터). 위험: 플레이 전 캡처는 원본 계층(Body 아래 Pelvis)이라 보이는 모습이 게임과 다름 — 몸통·하체가 같이 걸린 포즈는 플레이 중 캡처 권장.
- **M2** 완료. 신규 `Visual/MechVisualRig.Moves`(`PoseStance` — LowGuard·SprintGuard 포즈를 더하기 층으로, 전력질주 진입·해제 `sprintStanceBlendTime` 0.2초 섞기, `ArmSwingScale`), `MechVisualRig.PoseIdle`(전신 모션 모델은 예전 대기 팔·상체·무릎 대신 기본 자세 포즈, 발 벌림·엇갈림만 대기 다리에서 — 무릎 중복 없게 `includeKnee`), `Locomotion.PoseGait`(팔 스윙 × 돌격 배율), `BernardTuning.Motion`(`sprintStanceBlendTime`, `sprintArmSwingScale` 0.3). 위험: 가드는 M5 전까지 예전 가드 오프셋이 로우 가드 위에 더해져 팔이 더 높게 보일 수 있음.
