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
### [x] M3. 기본 펀치(관절 지연 사슬·팔로스루·회복·좌우·앞발 디딤)
### [x] M4. 스러스터 런지 모션 + 새 판정 방식(정면 타격 거리에서 박기+펀치)
### [x] M5. 가드·막음·패리·가드 브레이크
### [x] M6. 앵커 클로 → 파일 드라이버(어퍼컷·피스톤·히트스톱)
### [x] M7. 대시 공격·태클·슬램

## Phase N — 타격감·원투쓰리 콤보·이동 모션

### [x] N1. 피격 반응·공격 세기 등급·방향 흔들림·번쩍임·타격 이펙트
### [x] N2. 원투쓰리 콤보
### [x] N3. 이동 모션 재설계 (초당 걸음 수 기준)

## Phase O — N 피드백

### [x] O1. 이동 방식 상태별 분리 (걷기 2.5m/s 걸음, 달리기 스러스터 도약, 전력질주 호버 활주)
### [x] O2. 콤보 쓰리 후 회복 0.8초
### [x] O3. 효과음 칸 추가
### [x] O4. 포즈 라이브러리·.meta 커밋 정리

## Phase P — O 피드백

### [x] P1. 게임 속도 원복 (걷기 10, 달리기 20, 전력질주 30)
### [x] P2. 다리 모션을 실제 수평 속도로 결정 (걸음 / 스러스터 디딤걸음 / 도약 / 호버)
### [x] P3. 손으로 만든 .meta GUID 점검

## Phase Q — 플레이 시작 직후 NaN (Invalid worldAABB/localAABB)

### [x] Q1. NaN 원인 찾기 + 처음 생긴 순간 진단 로그
### [x] Q2.1. F9 프레임 제한 전환·fps 표시 (테스트용)

## Phase R — 파일 드라이버 넉다운

### [x] R1. 넉다운 동작 (히트스톱 뒤 비행 → 착지 미끄러짐 → 기상 무적, 벽 충돌, 입력 무시)
### [x] R2. 넉다운 외형 (비행 젖힘·늘어짐, 등/엉덩이 착지·미끄러짐·먼지·흔들림, 기상 포즈)
### [x] R3. 연습 봇 넉다운 처리 확인·30fps 확인 방법

## Phase S — 셀 셰이딩 (셰이더 그래프 ToonMech·M_ToonMech는 수정하지 않음)

### [x] S1. bernard 장갑·관절 머티리얼 (임포터 리맵)
### [x] S2. 피격 틴트·흰색 번쩍임 호환
### [x] S3. 화면 외곽선 렌더러 기능
### [x] S4. 외곽선 fps 비교

## Phase T — 소행성 표면 맵 (메뉴 Tools/Build Asteroid Arena로 생성, 씬 손 편집 없음)

### [x] T1. 지형 (평평한 판정 바닥 + 요철 외형, 경사·테두리 바위 + 보이지 않는 원형 벽, 바위 4개, 스폰)
### [x] T2. 하늘(코드 스카이박스)·행성·조명
### [x] T3. 진공 먼지
### [x] T4. 카메라 충돌
### [x] T5. 맵 전후 fps
### [x] T1.1. 앵커 클로가 바위·벽에 막힘

## 도구

### [x] U1. Tools/Build And Smoke Test
### [x] Q2. 방어 (스프링 안정화, 범위·0 검사, 잘못된 값이면 원래 자세로 되돌리고 상태 리셋)

## Phase V — V3 (`docs/GAME_SPEC_V3.md`, 계획 `docs/PLAN_V3_1.md`, 브랜치 `v3`)

### [x] V1. 카메라: 몸 뒤 고정 백뷰(뒤 6m·위 2.5m), R스틱 좌우 = 제자리 몸 회전(하드락 안에선 꺼짐), 화면 밖 상대 화살표(두 화면)
### [x] V2. 이동 정리: 걷기·가속도·Brake·초신지선회·슬라이딩·트윈스틱 전력질주 삭제, 항상 달리기 20m/s, 하드락 10/12m + 근접 이동 8m/s, 땅에서 부스트 회복
### [x] V3. ○ 대시: 누르는 동안 30m/s·부스트 초당 소모, 하드락 밖 = 보는 방향 + L스틱으로 틀기, 안 = 상대에게, ○ + 뒤 = 백대시, 1.5초 이상 태클 차징, 대시끼리 몸 충돌
### [x] V3.1. 대시 재작업: 누른 순간 8방향 고정·즉시 30m/s(토글), 1초 후 ○로 취소, 끝 0.2초 미끄러짐, 정면·카메라 고정(모델만 틀기), 출발 분사·발밑 불꽃, 백대시 폐기(뒤 대시), 대시 어택 판정 최대 0.6초
### [x] V5. 점프: △ 5m·1초, 점프 락온 0.2초, 공중 대시, 착지 경직 0.2/0.8초(발밑 표시), 공중 가드·클로·공격 불가, 공중 피격 낙하, Walkable·Wall 레이어 접지, 펀치 높이 허용 2m
### [x] V4. 대시 공격(유도 25m)·차징 태클(직선), 빗나감 0.5초 경직, 대시 공격끼리 맞딜, 공격 버튼 우선순위 갱신
### [x] V6. 템진 기준 이동 수치: 방향별 5점 걷기(22/19.5/19.2/17.7/19.9, 스틱 0.3 이상 = 최대, 하드락 감속 제거)·대시(24.1~14.2, 시작 0.27 → 지속 1.4 → 끝 0.27초 선형 감속, ○ = 정지, 대시 어택 경직 0.2~0.3초)·점프(이륙 2프레임, 23m/s·31.5m/s², 8.4m, 하강 상한 7.5, 공중 이동 방향별, 상승 중에만 공중 대시)·카메라 화면 구도(45%·머리 가운데·지평선 32%)
### [x] V8. 버추얼온식 이동기 (`docs/REFERENCE_MOVEMENT.md`, 계획 `docs/PLAN_V8.md`): 60Hz 틱·위치 += 속도·키네마틱, 서기/걷기/근접 스텝/지상 대시/대시 정지/점프/공중 대시/급강하, 레버 누적값, 거리 감속, 수동·자동 선회, 벽 미끄러짐·단차, 카메라 78u·8.4°·12%·FOV 35, 외부 속도 통로, 이동기 선택 스위치
### [x] V8.2. V8.1 피드백: 근접 구역 중립 ○ = 바로 앞 대시 → 0.25초 안 스틱이면 근접 스텝(모션 이어 감), △ 단독 재점프·호버 복원, 급강하 = 공중 L스틱 아래 + △
### [x] V8.3. 카메라 추종 분리: 방위 12%/틱, 위치 35%/틱, 기체가 화면 가로 중앙에서 폭의 25% 넘게 벗어나지 않는 상한
### [x] V7c. 부스트를 이동에서 제거(A안): 대시·공중 대시·점프·급강하·사이드스텝은 부스트 소모·탈진 차단 없음, 부스트는 런지 전용, 부스트 0% 긴 착지 경직 삭제(착지 경직 0.2초)
### [x] V7a. 벨포레스트 박스 맵: 메뉴 `Tools/Build Bellforest Blockout`(갑판 120×56m·뱃머리 쪽만 좁게, 고지대 2곳 11m, 발판 4개 5m, 엄폐물 2개 2.5m, 스폰 50m, 보이지 않는 테두리 벽, 윗면 Walkable·옆면 Wall, 흰 점선 활주로·노란 경계선·레일·10m 눈금), 소행성 맵은 끔
### [x] V7b. 낮 하늘과 조명: 코드 스카이박스(밝은 낮 하늘 + 갑판 아래 구름층), 해 높이 50°, 밝은 앰비언트, 바닥 반사 얼룩 확인, 버나드 렌더러 키 측정(1.93m)·MechMotor 키 기준값 맞춤
### [x] V9a. 버튼 재배치: 가드·패리 L2 → ✕(땅 = 가드·패리, 공중 = 급강하), 2단 점프·L스틱 아래 + △ 급강하 삭제(하강 호버 유지), L2 비움, 가드 중 자동 선회(정면 ±33.75° 안 132°/s·밖 360°/s)
### [x] V9b. 기체 키 1.93m 확정(키 고정 옵션 없음), 벨포레스트 발판 3.7m·고지대 8.1m로 변경해 씬에 반영, 점프 궤적 계산
### [x] V9c. 카메라 방위는 기체 방위에 지연 없이, 12%는 시선 표적 오프셋에만 (위치 35%·가로 25% 상한 유지)
### [x] V9d. 메뉴 입력 겹침: 메뉴가 뜬 뒤 0.5초 확인 입력 무시(✕를 누른 채 넘어오면 뗐다 다시 눌러야 확인), 공중 대시 고지대 직행은 의도된 기술로 기록
### [x] V9e. 선회 끊김 수정: 빠른 선회(759°/s)는 상대 쪽으로 돌 때만, 33.75°~60° 선형 보간, 가드 자동 선회도 같은 방식
### [x] V9f. 부스트 시스템 제거(게이지·탈진·회복·EXHAUSTED 팝업·HUD 부스트 호), 런지는 쿨다운 3초 — 쿨다운 중 입력은 버림(펀치로 안 바뀜)
### [x] V9g. 런지 쿨다운 호(예전 부스트 호 자리): 쿨다운 동안 0에서 차오르고 다 차면 0.2초 반짝임, 쿨다운 중 런지 입력은 호를 짧게 흔듦
### [x] V8.1. V8 피드백: 걷기 즉시 방향별 최대 속도(누적값 모델 삭제) × 거리 감속, 스틱 중립 ○ = 앞 대시(근접 스텝 규칙 유지), 급강하 = 공중 △ 다시, 공중 대시 8방향
### [x] V6.1. V6 피드백: 대시 끝 단계 △ 내내·공격 0.05초 뒤부터, 차징 태클 0.8초·최대 1.6초, VERSUS 각 화면 16:9 레터박스(HUD 포함), Base FOV 52

## Phase CL — 구조 정리: "버추얼온 코어 + 우리 게임 레이어" (계획 `docs/PLAN_CLEANUP.md`, 동작 변화 없음)

### [x] CL1. 참조 0인 급강하 튜닝 값 삭제, 대시·점프 이벤트와 `DashExitReason`을 레거시 상태 밖(`MechStateMachine`)으로 이동
### [x] CL2. 중립 복귀를 `ToStand`/`ToStandOrWalk`로, `RedirectForV8` 제거, `ActiveDash` null 대비
### [x] CL3. 레거시 이동 상태 6개·스틱 튕기기 판정·이동기 선택 스위치·`IsV8` 분기·예전 카메라 백뷰·죽은 이동 수치 삭제
### [x] CL4. 코어가 게임을 직접 부르던 곳을 `IVoHost`(+ 입력·호스트 어댑터)로 치환
### [x] CL5. `VoSpec`(원본값 고정) + `StyleOverrides`(우리 값) 분리, 코드에 박힌 변형을 스위치로
### [x] CL6. 폴더 이동(`Core/VirtualOn`·`Bridge`·`Game/*`)과 네임스페이스
### [x] CL7. 문서 갱신, `StyleOverrides`·`BernardTuning` 에셋을 디스크에 저장

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
- **M3** 완료. 바뀐 파일: `MechVisualRig.Motion`(앞당김 기준 시각 `LeadAnchor` — 0초에 같이 출발해 기준 시각보다 앞당김만큼 먼저 도착하도록 앞 구간을 압축, 시작 순간 관절이 튀지 않음, `MaskAll`), `MechVisualRig.Moves`(`PosePunch` — 0 → PunchWindup(발동 × 0.4) → PunchStrike(판정 순간) → PunchFollow → 쿨다운 끝까지 Heavy 복귀, 뒷발 3·앞발·골반 2·허리·머리·반대 팔 1·때리는 팔 0 × `punchChainDelay`, 왼 주먹은 좌우 반전, 걸음 중이면 하체 마스크 + PunchStepIn), `MechVisualRig`(시작 시 걸음 세기 기억, 전신 모션이면 예전 펀치 오프셋 생략, 대기 판정 길이), `BernardTuning.Motion`(펀치 4칸). 펀치 발동·판정·쿨다운은 그대로. 위험: 하체를 쓰는 포즈라 펀치 중 서 있으면 걸음이 아니라 포즈 다리가 나옴(의도) — 포즈 값이 bernard 축과 안 맞으면 다리가 이상하게 꺾일 수 있어 캡처로 덮어써야 함.
- **M4** 완료. 바뀐 파일: `ThrusterLunge`(`LungePhase` Dash/Plant/Strike, 새 판정: 돌진 중 상대 중심이 정면 ±`strikeHalfAngle` `strikeDistance` 안이면 `BeginPlant` → `plantTime` 판정 없음 → `strikeExtendTime` 동안만 예전 판정 구로 판정, 못 닿고 돌진 끝이면 같은 박기·헛펀치 후 `Finish(null)`, 박기·뻗기 중 `strikeStopDamping`, `OnLungePlant` 이벤트), `BernardTuning`(`LungeJudgeMode` 스위치·새 판정 5칸, `Motion` 런지 6칸), `MeleeDummy`(Parrier 런지 예상 시각), `MechVisualRig`(런지 이벤트 구독, 전신 모션이면 `PoseLunge`), `MechVisualRig.Moves`(`PoseLunge` — 웅크림·비행(접지 보정 멈춤)·박기·스트레이트·Heavy 복귀, `OnMotionImpact`, `ShakeCameras`), `MechVisualRig.Grounding`(`_groundingHold`), `GAME_SPEC` 5장, `QUESTIONS` M4. 위험: 런지 명중이 박기+뻗기만큼 늦어져 체감이 달라짐 — `Judge Mode`로 되돌릴 수 있음. 먼지 효과는 이벤트(`OnMotionImpact`)만 있고 표시물은 아직 없음.
- **M5** 완료. 바뀐 파일: `MechVisualRig.Moves`(`PoseDefense` — Guard 포즈 섞기(올림 0.06·내림 0.12초, 걸으면 하체는 걸음), 막음 GuardBlocked + 골반 `DirectPosition`으로 때린 쪽 반대 방향 외형 미끄러짐, 패리 ParryDeflect, 가드 브레이크 GuardBreak를 `Guard.breakStun`(공격 스턴이 길면 그쪽) 동안 + 줄어드는 휘청, `OnDefenseResolved`), `MechVisualRig`(전신 모션이면 예전 가드 오프셋·패리/브레이크 킥 대신 위 모션, 브레이크 중 예전 스턴 흔들림 생략, 대기 판정), `BernardTuning.Motion`(가드 계열 10칸). 판정·시간은 그대로. 위험: 막음 미끄러짐이 넉백(물리)과 겹쳐 실제보다 더 밀려 보일 수 있음 — `Block Slide Distance`로 조정.
- **M6** 완료. 바뀐 파일: `MechVisualRig.Moves`(`PoseClaw` — 단계 변화 추적, 예비 ClawWindup(windupTime) → 던지기 ClawThrow → 빗나감 복귀, 잡으면 ClawYank(뒷발·상체 먼저) → ClawReel 유지(당기는 동안 + 준비 중이면 `clawReelHold`) → 풀림; `PosePileDriver` — ClawReel(또는 기본 자세) → UppercutRise → UppercutStrike(`uppercutRiseTime`, 다리 → 골반 → 허리 → 주먹 지연) → 유지 → 복귀, 외형 타격 순간 `OnPileDriverImpact`: 히트스톱 `HitStop.pileDriver` 요청·두 화면 흔들림·`OnMotionImpact`; `ApplyPiston` — 실제 시간으로 Fist_R(없으면 아래팔)을 팔뚝 축 방향 `DirectPosition`), `MechVisualRig`(잡기·파일 드라이버 이벤트 처리, 전신 모션이면 예전 클로·내리찍기 생략, 대기 판정), `BernardTuning`(`Motion` 앵커 클로 7칸·파일 드라이버 8칸, `HitStop.pileDriver` 0.2). 판정·피해 시간은 그대로(누른 순간). 위험: 피해는 누른 순간이라 상대 HP가 외형 타격보다 0.08초 먼저 줄어듦. 히트스톱이 리그 요청이라 공격자 기체에 리그가 없으면(봇 등) 걸리지 않음 — 봇은 앵커 클로를 안 씀.
- **M7** 완료. 바뀐 파일: `MechVisualRig.Moves`(`PoseTackle` — 대시 어택 DashWindup → DashStrike / 차징 태클 TackleWindup → TackleShoulder, 발동 시간에 도착, `Tackle.recoveryStun` 동안 복귀, 발동 중 하체는 걸음; `PoseSlam` — SlamRaise → SlamDown(무릎·골반 먼저) → 유지 → 복귀), `MechVisualRig`(전신 모션이면 예전 어깨·내리찍기 생략, 대기 판정 길이), `BernardTuning.Motion`(태클 1칸·슬램 4칸). 판정·시간 그대로. 남은 위험(M1~M7 공통): 초안 포즈 각도는 bernard 실제 뼈 축으로 확인 안 됨 — 부호가 반대인 관절이 있으면 캡처로 덮어쓰거나 슬롯 `rotationCorrection`으로 맞춘다. 먼지 표시물 없음(`OnMotionImpact` 이벤트만).
- **N1** 완료. 바뀐 파일: `HitTypes`(`AttackStrength` 약·중·강, `HitInfo.ComboStep`·`Strength`), `BernardTuning`(`HitFeel` 묶음: 등급별 히트스톱·흔들림·반응·이펙트 크기, 번쩍임, 방향 흔들림 진동수, 이펙트·먼지 수치), `HitStop`(등급별 길이 — `gradeHitStop` 끄면 예전 종류별, 전신 모션 기체의 파일 드라이버는 리그가 외형 타격 순간에), `MechCamera`(`AddDirectionalShake` — 실제 시간, 타격 방향으로 밀렸다 출렁임), 신규 `Feedback/HitSparkFx`(코드 생성 ParticleSystem: 불꽃+섬광, 먼지, 실제 시간, Sprites/Default + 코드 생성 둥근 텍스처), `CombatFeedback`(`ApplyHitFeel` — 흰색 번쩍임 0.05초가 다른 틴트보다 우선, 타격 지점 = 때린 쪽 가슴 높이에서 맞은 콜라이더 표면, 막으면 파란 불꽃·0.5배, 맞은 쪽 방향 흔들림·때린 쪽 흔들림, `OnMotionImpact` → 먼지/파일 드라이버 불꽃), `MechVisualRig`(`PoseHitReaction` — 반대로 젖힘·머리 늦게·무릎 꺾임·외형 밀림, `HandlesPileDriverImpactOf`, 앵커 클로 잡을 때 뒷발 `ClawBrace` 먼지). 이펙트·번쩍임·흔들림은 임시 상자 기체에도 나옴(피드백), 몸 반응은 하체 뼈 있는 모델만. 위험: 히트스톱 기본값이 바뀜(펀치 0.05 그대로, 대시 어택 0.08→0.07, 슬램 0→0.1, 런지·태클 0.08→0.1). 효과음 칸 12개 전부 비어 있음(씬 CombatFeedback).
- **N2** 완료. 바뀐 파일: `BernardTuning`(`Combo` 묶음 8칸, `Motion` 잽·훅 사슬 배율·이어 붙이기 시간), `MeleeSystem`(`NextComboStep` — 원·투 판정 뒤 `window` 안이면 다음 타, 다음 타 최소 간격 `interval`·원은 예전 쿨다운, 쓰리 회복 중 펀치 불가, 쓰리 피해 ×1.5·넉백, `ComboStep`·`OnComboStep`, `MoveSpeedScale`, 스턴·라운드 리셋에 콤보 끊김), `MechController.ApplyMoveForce`(콤보 속도 비율을 힘·최대 속도에 곱함), `PlayLogger`(`combo1~3_start` 요약 고정 항목, 펀치 결과 detail에 단계, `comboN_hit` 등), `HitInfo.ComboStep`(쓰리 = 중 등급), `MechPoseLibrary`(잽·훅 초안 포즈 6개, 에셋에 없는 포즈·커브는 코드 초안에서 `Draft`, `AddMissingDraftPoses`), `Editor/MechPoseEditor`(초안 포즈 추가 버튼), `MechVisualRig`(`BeginCarry`/`ApplyCarry` — 펀치·태클·런지·슬램·파일 드라이버 시작 때 끊긴 자세에서 이어 붙임, 단계 기억), `MechVisualRig.Moves`(`PosePunch` 단계별 포즈·손·사슬, 외형 길이 = 판정 + 창/쓰리 회복). 위험: 콤보가 예전 단발보다 초당 피해 +35% — QUESTIONS N2. 연습 봇도 콤보가 됨.
- **N3** 완료. 신규 `Visual/MechVisualRig.Gait`(`PoseGaitCadence` — 초당 걸음 수로 위상 진행, 보폭 = 속도 ÷ 걸음 수, 최대 보폭 = 2 × 엉덩이~발목(무릎 반영) × sin(maxStrideAngle), 넘으면 디딤 비율을 줄여 체공(기본, 체공 중 골반 뜸·접지 보정 멈춤) 또는 걸음 수 올리기, 디딘 발목 x를 몸 속도로 뒤로 보내고 엉덩이 = asin(x / 유효 길이)로 역산, 무릎 추가 굽힘·허벅지 앞으로, 골반·어깨 반대 비틀림·팔 따라 흔들기, 디딘 발 쪽 골반 옆 이동·기울임, 착지 내려앉음 → 밀고 올라옴(`LandBounceAt`), 머리 비틀림·기울임 되돌리기, `logGaitNumbers` 출력), `MechVisualRig.Locomotion`(전신 모션이면 새 걸음으로 분기, `KneeBendForDrop` 음수(다리 펴기) 허용 — 예전 호출은 양수라 결과 같음), `MechVisualRig`(시작 시 수치 출력), `BernardTuning`(`StrideMode`, `Gait` 묶음 29칸), `MOTION_TIMING`(속도·보폭 표), `QUESTIONS` N3. 이동 속도는 그대로. 위험: 기본값이면 걷기부터 체공 70% — QUESTIONS N3.
- **O1** 완료. 바뀐 파일: `MechVisualRig.Gait`(다시 씀 — 걷기·도약·호버 세 방식을 `modeBlendTime`에 섞음; 걷기 = 보폭 1.0m(다리 최대 보폭까지) 체공 없는 걸음·N3 연출 유지; 달리기 = `leapRate` 1.6회/초 RunLeapLand → RunLeapPush → RunLeapAir 루프, 공중 골반 뜸·접지 보정 멈춤, 착지 발소리·흔들림·`RunLand` 먼지, 박차기 스러스터 버스트·`OnThrusterBurst`; 전력질주 = HoverGlide + 펄린 노이즈 좌우 체중 이동; 등 스러스터·발밑 불꽃 반복 이펙트), `HitSparkFx.CreateLoop`, `MechPoseLibrary`(RunLeapLand·RunLeapPush·RunLeapAir·HoverGlide 초안), `MechVisualRig`(OnEnable/OnDisable/OnDestroy에 이펙트 정리), `BernardTuning`(`Gait` 묶음 재구성 — N3의 걸음 수·체공 칸 삭제, 도약·호버·스러스터·불꽃 칸 추가; `maxWalkSpeed` 코드 기본값 2.5), `Editor/TuningSetup`(`Apply Feedback Values` 메뉴로 에셋 걷기 10 → 2.5), `MOTION_TIMING`(N3 표 정정: 실제 에셋 속도 10/20/30), `QUESTIONS` O1(달리기·질주 속도 해석, 다른 규칙 영향 목록). 위험: 메뉴를 실행하기 전엔 에셋 걷기 10m/s 그대로라 10걸음/초로 걷는다.
- **O2** 완료. 바뀐 파일: `BernardTuning.Combo.thirdRecovery` 0.6 → 0.8(에셋엔 콤보 칸이 아직 저장 안 돼 코드 기본값이 그대로 쓰임), `Editor/TuningSetup`(에셋에 콤보 칸이 있으면 같은 메뉴가 0.8로), `GAME_SPEC` 콤보 시간, `QUESTIONS` O2(반복 초당 피해 58.3 — 단발 50보다 +17%).
- **O3** 완료. 바뀐 파일: `CombatFeedback`(효과음 칸 5개 + 크기 4칸·페이드 1칸: 슬램 명중, 파일 드라이버 명중 — 전신 모션 기체는 외형 타격 순간, 먼지·착지(`OnMotionImpact`), 스러스터 분사(`rig.OnThrusterBurst`), 호버 루프 — 기체마다 루프 AudioSource를 처음 필요할 때 GameManager에 붙여 전력질주 동안만 재생·페이드), `SETUP` 80. 위험: 루프 AudioSource는 2D라 화면 분할 두 사람이 같은 크기로 들음(기존 효과음과 같음).
- **O4** 완료. 커밋한 파일: `Assets/Resources/MechPoseLibrary.asset`·`.meta`(사용자 포즈 작업물 — 스크립트 GUID가 `MechPoseLibrary.cs.meta`와 일치하는 것 확인), `Feedback/HitSparkFx.cs.meta`·`Visual/MechVisualRig.Gait.cs.meta`(Unity가 아직 임포트하지 않아 없던 것 — 프로젝트의 다른 스크립트 .meta와 같은 형식으로 새 GUID). 위험: 없음 — 두 스크립트는 씬·프리팹에서 GUID로 참조되지 않는다.
- **P1** 완료. 바뀐 파일: `BernardTuning`(`maxWalkSpeed` 코드 기본값 2.5 → 5, O1 이전 값), `Editor/TuningSetup`(`Apply Feedback Values`가 이동 속도를 안 건드림 — 콤보 쓰리 회복만), `QUESTIONS`·`SETUP`·`TEST_CHECKLIST`. 에셋은 처음부터 걷기 10이라 게임 속도는 O1 이전과 같다. 급감속 상한은 원래 코드 동작(걷기 최대 속도로 자름)이고 상한이 10이라 O1 이전과 같음.
- **P2** 완료. 바뀐 파일: `MechVisualRig.Gait`(다시 씀 — 실제 수평 속도로 걸음/디딤걸음/도약 비중을 `SpeedBand`(경계 ± `speedBlendHalfWidth`)로, 전력질주 상태만 호버; 걸음·디딤걸음은 같은 `StepLeg`(위상 따로), 디딤걸음은 `SlideTravel` — 디딤 구간 끝 `stridePlantFraction`은 고정, 그 앞은 발이 지면에 대해 몸 속도로 미끄러지다 멈추는 거듭제곱 곡선(이동 거리 = 다리 최대 보폭이 되도록 지수 계산), 걸음 수 상한 `strideMaxCadence`, 착지마다 스러스터 짧은 분사·`StrideLand` 먼지·출렁임, 긁는 동안 발밑 불꽃·`ScrapeAmount`; 섞이는 구간에선 큰 쪽만 착지 연출), `BernardTuning.Gait`(속도 구간 3칸·디딤걸음 10칸), `CombatFeedback`(`Stride Step Clip`·`Stride Scrape Loop Clip`, 반복 소리 공용 `UpdateLoop`), `MOTION_TIMING`·`SETUP` 82. 판정·전투 수치·이동 속도는 그대로. 위험: 걷기 상태 최대 10m/s에선 걸음당 미끄러짐이 3m라 거의 스케이팅처럼 보일 수 있음 — `Stride Max Cadence`를 올리면 줄어듦.
- **P3** 점검만(코드 변경 없음). 두 손 .meta의 GUID는 프로젝트·패키지 .meta 13,174개 중 자기 파일에만 있고, 씬·프리팹·에셋 참조도 없음. 에디터 임포트 확인은 아직(QUESTIONS P3).
- **Q1** 완료. 바뀐 파일: `MechVisualRig`(진단 — 포즈 목표·스프링·적용 직전·접지 보정량에서 NaN/무한대/발산(각 3600°·위치 50m 초과)을 처음 만나면 관절·단계·입력값(전후 각도·속도·dt·ω·step·안정 한계)을 에러 1회, 스프링 불안정 프레임(ω·step > 한계)을 경고 1회), `QUESTIONS` Q1(원인: 반암시 오일러 스프링이 44fps 아래에서 발산 — 파이썬 재현 20·30fps 75프레임, 40fps 236프레임에 무한대), `MOTION_TIMING`(NaN 가능 지점 표). 아직 막지는 않음(Q2).
- **Q2** 완료. 바뀐 파일: `MechVisualRig`(스프링 하위 스텝 — ω·h ≤ 안정 한계 × 0.8, 최대 32개, 주파수·감쇠·dt 범위 검사; dt NaN·음수는 0; 포즈 목표 검사 `CheckTargets` → 스프링·우회·발목 보정 뒤 관절 검사 `CheckJoint`를 **모든 관절 다 계산한 뒤에야 뼈에 쓰도록** 순서 변경; 잘못되면 `RecoverFromInvalid` — 처음 한 번만 에러, 모든 뼈 원래 자세·스프링·위상(걸음·디딤걸음·도약·호버)·걸음 세기·가속도·이어 붙이기·상하체 방향·접지 리셋; 접지 보정량 검사), `MechVisualRig.Locomotion`(리지드바디 속도 NaN이면 0, 발 들기 정점 0.05~0.95, fixedDeltaTime 최소), `MechVisualRig.Gait`(발 들기 정점, 이펙트 방향 계산 전 몸 뼈 검사), `MechVisualRig.Grounding`(발바닥·바닥 높이·보정량 검사), `MechCamera`(퀵턴 시간 0·최대 속도 0 나눗셈). 60fps 이상 외형 결과는 그대로.
- **Q2.1** 완료. 신규 `Core/FrameRateTester`(F9 전용 InputAction — 운영자 키라 ESC 종료와 같은 방식으로 페어링과 무관; 누를 때마다 `QualitySettings.vSyncCount = 0` + `Application.targetFrameRate` 없음(-1) → 60 → 30 → 20; 오른쪽 위 OnGUI로 현재 제한·실제 fps(실제 시간 0.5초 평균, 히트스톱 중에도), 화면 높이 1080 기준 글자 크기), `.meta`(새 GUID, 겹침 없음 확인), `GameManager`(자동 부착). 빌드에서도 동작. 위험: F9로 VSync를 끈 뒤엔 그 실행 동안 VSync가 꺼진 채(다시 켜려면 재시작).
- **R1** 완료. 신규 `Combat/KnockdownController`(기체마다 — 대기(히트스톱 동안 물리 스텝 없음 → 그 뒤) → 비행(중력 끄고 스텝 가운데 시각의 포물선 속도: 수평 거리/시간, v0 = 4h/T, g = 8h/T², 시뮬레이션 25스텝 8.000m·최고 1.498m) → 미끄러짐(선형 감속 2m) → 기상(무적), ExternalControlLock·감속 0 요청(봇은 직접), 수평 SphereCast로 벽·다른 콜라이더(자기 몸·트리거·바닥 제외) 닿으면 수평 정지·큰 흔들림, 착지 흔들림, 이벤트 6개, `debugLog` 거리 출력, `ResetForRound`), 신규 `Movement/KnockdownState`(입력 무시·힘 없음, 끝나면 Idle), `MechStateMachine`(넉다운이면 어느 상태든 KnockdownState로, 하드 락온 몸 회전 제외), `HitTypes`(`HitResult.Invulnerable`), `MeleeSystem`(넉다운 GetOrAdd, `IsKnockedDown`, 기상 무적이면 판정 무시, 넉다운 중 가드·펀치 불가), `WeaponSystem`(넉다운 중 공격·스킬 무시), `ThrusterLunge`(넉다운 중 런지 불가), `AnchorClaw.TryPileDriver`(맞으면 넉다운 시작 — 방향 = 공격자 상체 정면, 대기 = 어퍼컷 올리기 + 여유), `GameManager.ResetMech`(넉다운 리셋), `HitStop`·`CombatFeedback`(무적이면 없음), `PlayLogger`(`invulnerable_hit`, `lunge_invulnerable`, `knockdown`·`knockdown_wall`·`knockdown_end` 거리·높이), 두 스크립트 .meta(새 GUID, 겹침 없음), `GAME_SPEC`·`QUESTIONS` R1. 위험: 공중·미끄러짐 중엔 무적이 아니라 추가타가 들어감(QUESTIONS R1).
- **R2** 완료. 바뀐 파일: `MechPoseLibrary`(KnockdownFlight·KnockdownLand·GetUpKneel·GetUpRise 초안 — 누운 자세는 골반 -85°로 다리가 바닥과 거의 수평이라 접지 보정(발바닥 기준)이 엉덩이를 땅 높이에 맞춘다), `MechVisualRig.Moves`(`PoseKnockdown` — 단계 변화를 리그 시계로 기억, 비행 = 젖힘 포즈 + 팔다리 `LoosenLimbs`(모션 비율을 낮춰 스프링이 출렁)·날아오를 때 `KickLimbs`·접지 보정 멈춤, 미끄러짐 = 누운 자세, 기상 = 누움 → 무릎 짚기 → 반쯤 일어섬 → 기본 자세, 단계마다 이어 붙이기), `MechVisualRig`(넉다운이면 숨쉬기 끔·전신 모션 위에), `Locomotion.UpdateFacing`(넉다운 중 상하체를 기체 정면에), `Gait`(넉다운 중 걸음·도약·호버·스러스터·불꽃 끔), `CombatFeedback`(넉다운 착지 큰 먼지+소리, 벽 충돌 먼지+소리, 미끄러지는 동안 `slideDustInterval`마다 먼지 — 임시 상자 기체도), `BernardTuning.Knockdown`(외형 7칸). 위험: 누운 자세 높이는 접지 보정이 발바닥으로 맞추므로 캡처한 포즈에서 발이 엉덩이보다 많이 뜨면 몸이 땅에 박힐 수 있음.
- **R3** 완료. 점검: 연습 봇 이동(`MeleeDummy.FixedUpdate`)은 원래 `ExternalControlLock`이면 멈추고, 펀치·런지·가드는 R1에서 `MeleeSystem`·`ThrusterLunge`가 넉다운을 막는다. 봇 두뇌는 넉다운 중에도 버튼을 누르려 해서 `MeleeDummy.CanAct`에 넉다운을 넣었다(버튼 전부 떼고 행동 안 함, 끝나면 원래 모드). 봇은 MechController가 없어 감속 0은 `KnockdownController`가 리지드바디에 직접 넣고 끝나면 되돌린다(R1). 30fps 확인 방법은 `TEST_CHECKLIST` R3(F9 + `Debug Log`/`knockdown_end`, 물리 고정 스텝 0.02초라 같은 결과 — 파이썬 재현 8.000m + 1.999m, 최고 1.498m).
- **S1** 완료. 신규 `Editor/ToonMechSetup`(`Tools/Toon Mech/1. Apply Toon Materials To Bernard (S1)` — M_ToonMech 복제로 `M_ToonMech_Armor`(#8A8D94)·`M_ToonMech_Joint`(#3A3C41) 생성(이미 있으면 색 그대로), 씬 렌더러가 들고 있는 FBX 안 머티리얼을 먼저 새 머티리얼로 교체(Mech_P2는 bernard를 씬에 풀어 둬 리맵만으론 끊김), `ModelImporter.AddRemap`으로 Mecha_Armor·Mecha_Joint 리맵 후 재임포트, Render Face가 Both인 머티리얼 보고; S3 메뉴도 같은 파일). 셰이더 그래프·M_ToonMech는 안 건드림. 점검: 그래프 Render Face = Front(2)·머티리얼 덮어쓰기 꺼짐, M_ToonMech `_Cull` 2(앞면) — Both인 머티리얼 없음. 그래프 활성 서브타깃은 `UniversalLitSubTarget`(Unlit 아님), M_ToonMech의 `_ToonRamp`는 비어 있음(기본 흰색). 위험: 메뉴를 실행해야 적용됨(이 환경에선 에디터 실행 불가).
- **S2** 완료. 점검: `MechTint`(스턴·과열·가드 틴트, 흰색 번쩍임)는 MaterialPropertyBlock으로 `_BaseColor`(URP Lit/셰이더 그래프)·`_Color`(빌트인)를 덮어쓴다. ToonMech 그래프에 `_BaseColor`·`_EmissionColor`가 노출(Exposed) 프로퍼티로 있어 **추가할 프로퍼티 없음**. 바뀐 파일: `MechTint`(이미시브가 있는 머티리얼은 `Apply(…, emission)`일 때 원래 이미시브 + 색 × 세기 — 셀 셰이딩 그늘 쪽까지 하얗게), `CombatFeedback`(번쩍임만 이미시브 사용, 다른 틴트는 기본색만), `BernardTuning.HitFeel.flashEmission`(1). 그래프의 EmissionMask 기본값이 흰색이라 마스크 없이도 이미시브가 나온다. 위험: 틴트가 걸린 동안 그 렌더러는 SRP Batcher에서 빠진다(예전과 같음).
- **S3** 완료. 신규 `Assets/Materials/ScreenOutline.shader`(`Hidden/MyDongari/ScreenOutline` — URP Blit.hlsl 풀스크린, 상하좌우 4이웃의 선형 깊이 상대 차이·노멀 각도 차이로 모서리, 하늘(깊이 = 먼 평면) 픽셀은 선 없음·이웃이 하늘이면 실루엣, 굵기는 카메라 화면 높이 1080 기준 px, 먼 곳 옅어짐; 머티리얼 값: 선 색·굵기·깊이/노멀 민감도·옅어짐 거리), `Editor/ToonMechSetup`의 `Tools/Toon Mech/2. Add Screen Outline Renderer Feature (S3)`(M_ScreenOutline 생성, 쓰이는 URP 렌더러(PC_Renderer)마다 URP 기본 `FullScreenPassRendererFeature`를 `ScreenOutline` 이름으로 추가 — 후처리 뒤, 색 복사, 깊이+노멀 요구; 인스펙터의 Add Renderer Feature와 같이 목록·로컬 ID 맵에 넣음), `FrameRateTester`(F10으로 외곽선 켜기/끄기 — S4 비교용, 끝나면 원래 상태로, 화면 표시에 외곽선 상태). 화면 분할 두 카메라는 같은 렌더러라 둘 다 적용. 위험: 셰이더는 이 환경에서 컴파일 확인 불가(URP CoreBlit.shader와 같은 include 구성) — 에디터 콘솔에서 셰이더 오류 확인 필요. 월드 공간 팝업 글자(STATUS)에도 선이 생길 수 있음.
- **S4** 측정은 못 함 — 이 환경에선 에디터·빌드를 실행할 수 없다. 측정 방법을 `TEST_CHECKLIST` S4에 표로 남겼다(F9 제한 + F10 외곽선 켜고/끄기, 화면 표시 실제 fps). 예상 비용: 카메라마다 깊이+노멀 사전 패스(SSAO가 이미 노멀을 쓰면 공유) + 풀스크린 패스 1번(픽셀당 텍스처 9회) + 색 복사 1번, 화면 분할이라 ×2.
- **T1** 완료. 신규 `Core/AsteroidArenaConfig`(맵 설정 ScriptableObject — 바닥·경사·테두리 바위·벽·바위·스폰·색·행성·조명), 신규 `Editor/AsteroidArenaBuilder`(`Tools/Build Asteroid Arena` — 이전 `AsteroidArena` 삭제 후 새로, 예전 `Map_Spaceport.`는 끄기만; 루트 아래 `Floor_Collider`(평평한 박스, 윗면 = groundHeight -1.01 — 예전 바닥과 같은 높이) · `Floor_Visual`(지름 90m 요철 원판, 판정 없음) · `Crater_Slope_Visual`(폭 16m·높이 7m 경사 고리) · `Rim_Rocks/RimRock_N`(26개, 판정 없음) · `Boundary_Wall/Wall_NN`(반지름 45m 원형 박스 벽 48조각, 렌더러 없음) · `Rocks/Rock_1~4`(정이십면체 80면 일그러뜨림·각진 면·바닥 평평, 높이 2~3m, 볼록 MeshCollider, 45°부터 4방향·중앙 20m 밖); 메시는 `Assets/Arena/Generated/*.asset`, 머티리얼은 M_ToonMech 복제 회갈색 `Assets/Arena/Materials`; GameManager가 가리키는 스폰 포인트를 (0, 0, ∓15)·서로 마주 보게(높이 그대로)). 점검: **앵커 클로는 바위에 막히지 않는다** — `AnchorClaw.IsTargetCollider`가 목표·상대 콜라이더만 보고 나머지(벽·바닥·바위)는 지나간다(F1 결정). 위험: 메뉴를 실행·씬 저장해야 적용.
- **T2** 완료. 신규 `Assets/Materials/AsteroidSky.shader`(`MyDongari/AsteroidSky` — 검은 배경, 방향을 3D 격자로 나눠 칸 해시로 별 두 겹(크기·밝기·색 편차), 축에 수직인 큰 원을 따라 FBM 구름 은하수 띠; 깊이를 안 써서 화면 외곽선이 하늘로 보고 선 안 그림), 신규 `ToonPlanet.shader`(`MyDongari/ToonPlanet` — 메인 라이트 방향 계단 툰 + 가장자리 대기 림, 그림자 없음), `AsteroidArenaBuilder.BuildSkyAndLight`(스카이박스 머티리얼 M_AsteroidSky를 RenderSettings에, 안개 끔, 앰비언트 평면 푸르스름 (0.07, 0.09, 0.14); `Planet` 구체 반지름 220m·거리 760m(카메라 먼 평면 1000m 안)·방위 35°·높이 10°, 색·림은 설정 값을 M_Planet에; 기존 디렉셔널 라이트 하나를 해로 — 높이 20°·세기 2.2·흰빛·소프트 그림자, 다른 디렉셔널은 끔). 행성은 760m라 외곽선 거리 감쇠(40~80m)로 선이 안 생긴다. 위험: 셰이더 두 개는 이 환경에서 컴파일 확인 불가. 렌더 설정(스카이박스·앰비언트·안개)은 Ctrl+Z로 안 돌아간다.
- **T3** 완료. 바뀐 파일: `HitSparkFx.SpawnDust`(진공: 위를 향한 원뿔에서 위·바깥으로 튀고 공기 저항 없이(속도 감쇠 모듈 안 씀) 중력만으로 포물선 낙하, 크기가 부풀지 않는 알갱이, 월드 충돌(낮은 품질)로 바닥·바위에 닿으면 조금 튀고 멈춤), `DustStyle`(중력·원뿔 각·충돌), `CombatFeedback`(먼지 모양 공용 `DustStyleOf` — 런지 박기·앵커 클로 버팀·달리기 착지·디딤걸음 착지·넉다운 착지·벽 충돌·미끄러짐 전부), `BernardTuning.HitFeel`(먼지 기본값 알갱이 0.18m·24개·4.5m/s·1.4초, 중력 1·원뿔 55°·충돌 켬). 위험: 월드 충돌 파티클은 먼지가 많이 동시에 날 때 비용 — 느리면 `Dust Collision` 끔.
- **T4** 완료. 바뀐 파일: `MechCamera`(`ResolveCollision` — 흔들림까지 더한 최종 카메라 위치를 기체 중심에서 SphereCast(반지름 0.25m)해 리지드바디 없는 정적 지형(바닥 판정·원형 벽·바위·테두리)에 먼저 닿으면 그 앞(여유 0.05m)으로 당김, 기체·트리거 무시, 레이어 마스크·켜기 끄기 설정). 두 화면 카메라 모두(MechCamera가 각 카메라에). 점검: 지금 카메라는 기체 중심 위 1.5m(`offset (0, 1.5, 0)`, 뒤로 떨어진 거리 없음)라 평소엔 걸릴 일이 거의 없고, 화면 흔들림이 지형 쪽으로 밀 때·나중에 오프셋을 뒤로 뺄 때 동작한다. 테두리 바위(외형)엔 콜라이더가 없어 원형 벽(반지름 45m)에서 막힌다.
- **T5** 측정은 못 함 — 이 환경에선 에디터·빌드를 실행할 수 없다. 맵 전후 비교 방법·표를 `TEST_CHECKLIST` T5에 남겼다. 예상 부담: 그림자 켠 해 1개(기체·바위·테두리 바위 26개·경사가 그림자를 드리움 — 가장 큰 비용 후보), 지형 메시(바닥 약 3.5천·경사 약 1천 정점, 바위 80면씩), 스카이박스 픽셀 셰이더(별 두 겹 + 노이즈 8회), 먼지 월드 충돌 파티클. 화면 분할이라 그림자·하늘은 카메라마다.
- **T1.1** 완료. 바뀐 파일: `AnchorClaw.UpdateFlight`(같은 SphereCastAll 결과에서 목표보다 가까운 지형이 있으면 그 지점에서 빗나감 → Retracting·`OnMissed`; `IsBlockingTerrain` — 리지드바디 없는 정적 콜라이더, 위를 향한 면(바닥)·시작부터 겹친 것 무시), `BernardTuning.AnchorClaw.terrainFloorNormalY`(0.7), `QUESTIONS` T1.1. 발사·쿨다운 규칙은 그대로.
- **U1** 완료. 신규 `Editor/BuildAndSmokeTest`(`Tools/Build And Smoke Test` — 열린 씬 저장 → `Builds/yyyyMMdd_HHmmss/제품명.exe`로 Windows 64비트 빌드(빌드 설정의 켜진 씬) → `-logFile Builds/…/Player.log -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -smokeTest`로 실행 → 10초 뒤 창 닫기 요청, 3초 안에 안 끝나면 강제 종료 → Player.log에서 Exception·Error(대소문자 무시)·NaN 줄을 세고 같은 줄은 묶어 처음 줄 번호·횟수로 최대 30종류 콘솔 요약(있으면 경고, 없으면 일반 로그) + `SmokeTestSummary.txt`; 10초 전에 스스로 끝나면 크래시 가능으로 종료 코드 표시; 기다리는 동안 EditorApplication.update라 에디터가 멈추지 않음), `AppRuntime`(`-smokeTest` 인자면 전체 화면 강제 안 함). `Builds/`는 원래 .gitignore에 있음. 위험: 빌드 대상은 StandaloneWindows64 고정.
- **V1** 완료. 바뀐 파일: `MechCamera`(1인칭 → 몸 뒤 백뷰: 위치 = 기체 + 몸 yaw 기준 뒤 `followDistance` 6m·위 `followHeight` 2.5m, 시선 = 기체 위 `lookHeight` 1.5m·앞 `lookAhead` 8m, yaw는 몸 회전을 `yawFollowSpeed` 8로 지수 감쇠 추적. 시점 회전·퀵턴·`LockToForward`·하드락 추적·질주 속도 따라가기 삭제. 충돌은 기체 위 시선 높이에서 SphereCast, 흔들림·FOV·`ResetView` 유지. 시작 시 `OffscreenTargetArrow` 자동 부착), 신규 `UI/OffscreenTargetArrow`(OnGUI, 카메라 `pixelRect` 기준이라 분할 화면마다, 뒤에 있으면 방향 반전, 1080p 기준 크기), `MechController`(`RotateToCamera` 삭제, `ApplyTurn`, `BodyRelativeDirection`), `MechStateMachine`(`AllowsTurnInput`, R스틱 기울기 × `bodyTurnSpeed`(120°/초)·마우스 × `mouseTurnSensitivity`(2)를 Update에서 모아 물리 스텝에 적용, 하드락 몸통 회전·스턴·외부 제어·사이드스텝·런지·넉다운·초신지선회 중엔 끔), Idle/Walk/Run/Sprint/QuickTurn(몸 기준 이동, 초신지선회는 몸을 180° 돌림 — V2에서 삭제), `LockOnSystem`(카메라 추적 호출 삭제), `ThrusterLunge`(런지 방향 = 상체 정면), `MechVisualRig.Locomotion`(상체 = 하드락이면 상대, 아니면 몸 정면), `BernardTuning`(`bodyTurnSpeed`, `mouseTurnSensitivity` 추가, `hardTrackSpeed` 삭제), `Editor/BuildAndSmokeTest`(`RunFromCommandLine` — batchmode 동기 실행, 종료 코드 0/2/1). 위험: 기존 씬의 MechCamera `offset`·감도 값은 무시됨(새 칸은 코드 기본값). 소프트 락온(앵커 클로·HUD 마커)은 여전히 카메라 정면 ±30° 기준이라, 카메라가 몸보다 늦게 도는 동안 조준이 살짝 늦다. V1 동안 트윈스틱 질주 판정 중엔 R스틱 회전이 꺼진다(V2에서 질주 판정 삭제).
- **V2** 완료. 삭제: `WalkState`, `BrakeState`, `SlideState`, `QuickTurnState`, `TwinStickSprintInput`(+meta), `MechController.ApplyMoveForce`(방향 전환 저항·스핀업)·`ClampVelocity`·`RotateTowards`, `InputReader.RunInput/SlideInput/QuickTurnInput/LookStickRaw`, `StickGestureDetector.FlickDown`, `LockOnSystem.BlockAllLockFor`, 튜닝 `walkSpeed/runSpeed/sprintSpeed/maxWalkSpeed/rotationSpeed/turnResistance/spinUpTime/slide*/quickTurnDuration/brake*/sprint 스틱·카메라 칸/quickTurnCost/slideCost/hardEnterRange/hardExitRange/hardMaxAngle`. 바뀐 파일: `MechStateMachine`(Idle·Run·Sprint·SideStep·Lunge·Knockdown), `IdleState`(중립이면 즉시 정지), `RunState`(항상 달리기), `MechController`(`SetMoveVelocity`·`ApplyRunVelocity` — 기울기를 데드존~1 → 0~1로 다시 맞춰 즉시 속도, 하드락 밖 몸 기준 `maxRunSpeed` 20, 하드락 안 상대 기준 `closeMoveSpeed` 8; `StopHorizontal`; `HoldMoveControl` — 넉백 뒤 `knockbackMoveHold` 0.35초는 속도를 덮어쓰지 않음; `ResetForRound`), `SprintState`(키보드 Space 경로만, 속도 직접 지정, `SprintExitReason`을 이 파일로), `SideStepState`(→ Run), `LockOnSystem`(하드락 = 기체 기준 3D 거리 10m 걸림·12m 풀림, 각도·가려짐 조건 없음, 카메라 없어도 판정, 하드락 중 `Target` = 상대), `WeaponSystem`·`MeleeDummy`(펀치/런지 = 상대 거리 ≤ `meleeSwitchRange` 6m), `MeleeSystem`(넉백 시 `HoldMoveControl`), `BoostGauge`(Idle·Run이고 가드 아닐 때 회복), `InputReader`(`MoveDeadzone` 공개, Run·Slide·QuickTurn·LookStick 액션은 읽지 않음 — `.inputactions`는 그대로), `PlayLogger`(slide·트윈스틱 해제 이유 삭제), `MechVisualRig`(Walk 분기 삭제), `GameManager`(라운드 리셋에 `MechController.ResetForRound`). 위험: 패드에서 대시 공격·차징 태클이 안 나감(키보드 Space 질주로만 — 3단계 ○ 대시에서 복구). 하드락 10m를 빠져나갈 수단이 사이드스텝뿐. `docs/BUILD.md`·조작법 시트의 질주(↑↑)·슬라이딩·퀵턴 항목은 7단계 조작법 갱신 때 고친다.
- **V3** 완료. 바뀐 파일: `.inputactions`(새 `Dash` 액션 — 패드 buttonEast(○/B), 키보드 Space. 기존 바인딩 그대로), `InputReader`(`SprintInput` → `DashHeld`·`DashPressedThisFrame`), `SprintState` → `DashState`(파일 이름 변경 + 다시 씀: 누르는 동안 `dashSpeed` 30m/s, 하드락 밖은 시작 = 몸 정면 + L스틱(몸 기준) 쪽으로 `dashTurnRate` 120°/초까지 회전, 하드락 안은 매 스텝 상대 쪽, 몸이 진행 방향을 봄, 버튼 떼면·탈진이면 끝(달리기/정지로), 태클 차징·자동 접촉 태클·대시 어택 판정은 예전 코드 그대로, `DashExitReason`), 신규 `BackDashState`(○ 누른 순간 L스틱 아래 ≥ 0.5이고 좌우보다 크면 — 부스트 `backDashCost` 15를 한 번, 하드락 안은 상대 반대·밖은 몸 뒤로 `backDashDistance` 6m를 `backDashDuration` 0.25초에, 몸은 안 돌림), `MechStateMachine`(`TryStartDash` — Idle·Run에서, 탈진·스턴이면 안 나감; `IsDashing`/`IsBackDashing`; 대시·백대시 중 R스틱 회전 끔), `MechController`(`OnCollisionEnter` — 대시 중 공격 없이 상대 몸에 닿으면 둘 다 대시 끝 + 서로 반대로 `bumpSpeed` 4m/s + 넉백 유지 시간, 차징이 차 있고 정면이면 대신 자동 태클; `ApplyBump`, `DashSpeed`, `TryGetHardLockForward` 공개), `BoostGauge`(`dashDrainRate` 25/초, `GetBackDashCost`), `BernardTuning`(대시·백대시·몸 충돌 칸, `maxSprintSpeed`·`sprintDrainRate` 삭제), 질주 → 대시 이름 반영: `MeleeSystem`(대시 중 가드 불가), `HeatGauge`(대시 중 냉각 감소), `WeaponSystem`, `HUDController`(태클 차지 바), `CombatFeedback`(호버 소리), `MechVisualRig`·`.Gait`·`.Locomotion`·`.Moves`(호버 활주를 대시에), `MechCamera`(FOV 기준 = 대시 속도), `PlayLogger`(`dash_enter`·`dash_exit_*`·`backdash`). 위험: 대시·백대시 속도에도 콤보 이동 배율(60%·40%)이 곱해진다. 하드락 안 대시는 상대에게 곧장 가므로 공격하지 않으면 거의 항상 몸 충돌로 끝난다(스펙대로).
- **V4** 완료. 바뀐 파일: `DashState`(대시 중 공격 = 누른 순간 판정 → 돌진 판정: 발생 시간(Startup.dashAttack 0.12 / chargedTackle 0.1) 뒤 판정 구간(대시 어택 `activeTime` 0.8초, 태클 0.4초) 동안 대시 속도로 돌진하며 두 기체 중심 거리 ≤ `hitDistance` 3m이고 정면 ±45°면 `ResolveTackle`; 끝까지 못 닿으면 빗나감 → `recoveryStun` 0.5초 경직; 맞힘·막힘도 0.5초, 패리당함·맞딜은 경직 없음. 유도: 대시 1.5초 미만 공격이고 시작할 때 상대가 `homingRange` 25m 안이면 초당 `homingTurnRate` 120°까지 상대 쪽으로 꺾고, 상대가 진행 방향 뒤로 가면(지나침) 그 공격 동안 직진. 차징 태클은 직선. `WouldHitWithin`, `EndByClash`, `EndByKnockback`), `MeleeSystem.ReceiveHit`(대시 어택·태클을 맞는 쪽도 대시 공격 중이고 clashWindow 0.1초 안에 맞힐 상황이면 맞딜 — 둘 다 0 피해 + `Punch.clashKnockback`으로 밀려남, 히트스톱은 맞딜 길이 0.12초(가장 김)), `MechController.HoldMoveControl`(넉백을 받으면 대시가 끝남), `WeaponSystem`(우선순위 주석을 V3 표 1~7로, 2·3 공중 자리만), `BernardTuning`(대시 어택 `homingRange`·`homingTurnRate`·`activeTime`·`hitDistance`, 태클 `activeTime`·`hitDistance`), `MeleeDummy`(봇 패리 타이밍 = 발생 시간 + 남은 거리 / 대시 속도). 위험: 판정 거리 3m는 중심 거리라 기체 크기에 따라 조정 필요. 정면 부채꼴은 상체 방향 기준이라 급하게 꺾는 동안 상체 스프링이 늦으면 붙어도 안 맞을 수 있음(판정 구간 동안 계속 다시 봄). 피해·넉백 수치는 기존 값(대시 어택 20/6, 태클 30/8 × 차징).
- **V3.1** 완료. 삭제: `BackDashState`(+meta), 대시 조향(`dashTurnRate`, `UpdateHeading`), 버튼 떼면 끝나는 규칙, 튜닝 `backDash*`·`Boost.backDashCost`. 바뀐 파일: `DashState`(누른 프레임에 기준(하드락 안 = 상대 쪽, 밖 = 몸 정면)에서 L스틱 각도를 45° 스냅해 방향 고정, 중립이면 앞, Enter에서 바로 30m/s; 발동 후 `dashCancelLockTime` 1초 뒤 ○ = 취소, 부스트 0%·벽 → `DashStopState`; 몸은 안 돌림; `Heading`·`Elapsed`·`EndByWall`; 태클 판정 부채꼴 = 진행 방향), 신규 `DashStopState`(`dashStopDuration` 0.2초 동안 속도를 스틱이 원하는 달리기 속도 쪽으로 감속도 = 대시 속도 / 0.2초로 다가감, 스턴이면 Idle), `MechStateMachine`(`DashStopState`·`IsDashStopping`, 대시 중 하드락 몸통 회전 허용 — 상대를 정면에 둔 채 옆으로 빠짐, R스틱은 대시 중 끔·끝 미끄러짐 중 켬, `TryStartDash`에서 백대시 분기 삭제), `MechController`(`SetHorizontalVelocity`, `ComputeRunVelocity`, 벽 판정 `CheckDashWall` — OnCollisionEnter/Stay에서 리지드바디 없는 콜라이더의 옆면(법선 y ≤ 0.5)이 진행 방향을 정면(내적 ≤ -0.5)으로 막으면 끝, 공격 중엔 무시), `MeleeSystem`(`ResolveTackle`·`IsOpponentInTackleArc`에 기준 방향 인자), `WeaponSystem`(대시 끝 미끄러짐 중 공격 불가 — 버퍼로 끝난 뒤 나감), `MechVisualRig.Locomotion`(대시 중 상체 볼 방향 = 대시 방향 → 모델만 틈), `MechVisualRig.Moves`(대시 중 상체 `dashLeanAngle` 35° 숙임), `MechCamera`(기준점이 기체 위치를 지수 감쇠로 따라감: 평소 `positionFollowSpeed` 100, 대시 중 `dashPositionFollowSpeed` 12(30m/s에서 약 2.5m 처짐), 전환 `positionFollowBlendRate` 200/초), `CombatFeedback`(`DashBurst` — 출발 순간 기체 중심에 `dashBurstSize` 4.5m 섬광 `dashBurstTime` 0.03초 + 뒤로 튀는 불꽃, `UpdateDashScrape` — 대시 중 0.05초마다 발밑 불꽃), `BernardTuning`(위 칸들, 대시 어택 `activeTime` 0.8 → 0.6 + 최대 0.6 제한), `PlayLogger`(`dash_exit_canceled`·`dash_exit_wall`). 판정 점검: 예전엔 대시 어택이 못 맞히면 발생 0.12초 뒤 0.8초(누른 뒤 0.12~0.92초) 동안 판정이 살아 있었다 → 0.12~0.72초. 위험: 다리를 앞뒤로 벌리는 자세는 아직 없음(호버 활주 + 상체 숙임만). 발밑 불꽃은 기체당 초당 20개 오브젝트를 만들었다 1.5초 뒤 지움 — 30fps에서 느리면 간격을 늘린다. 이 작업 끝에 에디터가 프로젝트를 열고 있어 batchmode 스모크 테스트를 못 돌렸다(컴파일만 Unity 컴파일러로 확인).
- **V5** 완료. 신규: `Movement/AirState`(점프 = 초기 속도 2h/t·중력 2h/t², h `jumpHeight` 5m, t `jumpRiseTime` 1초, 내려올 때도 같은 중력, 최대 낙하 40m/s; 점프 락온 — 거리 무관 상대 쪽으로 `jumpTurnTime` 0.2초 SmoothStep 회전; 공중 이동 = 몸 기준 스틱 쪽 `airMoveSpeed` 8m/s로 `airAccel` 15m/s²씩; 공중 ○ = 공중 대시; 착지 판정은 뛴 뒤 0.15초 지나고 내려오는 중일 때), `Movement/LandingState`(`landingLag` 0.2초 / 부스트 0%면 `exhaustedLandingLag` 0.8초, 제자리, 이동·공격·대시·점프·가드 불가, `OnLanded`), `Core/GroundLayers`(Walkable 레이어 + 법선 y ≥ 0.7 = 땅, Wall 레이어 = 벽; Walkable 콜라이더가 하나도 없으면 정적 콜라이더 윗면 대체 규칙 + 경고 한 줄), `Editor/GroundLayerSetup`(메뉴 Mydongari/Map — Walkable·Wall 레이어를 빈 사용자 레이어에 만들고 AsteroidArena의 Floor_Collider·Rocks → Walkable, Boundary_Wall → Wall, 켜진 예전 맵 바닥 → Walkable; Selected → Walkable / Wall). 바뀐 파일: `.inputactions`(새 `Jump` — 패드 buttonNorth(△/Y), 키보드 F), `InputReader`(`JumpPressedThisFrame`), `MechController`(물리 스텝마다 콜라이더 중심에서 아래로 SphereCast해 `IsGrounded`·`TimeSinceGrounded`, `ApplyAirGravity`, 대시 벽 판정에 Wall 레이어), `MechStateMachine`(`AirState`·`LandingState`, `IsAirborne`·`IsLanding`·`IsJumpTurning`, `TryStartJump`(부스트 `jumpCost` 15, 탈진 불가), `CheckFall`(땅을 `coyoteTime` 0.1초 넘게 못 밟으면 낙하 — 경직 없음), `Land`, 공중에서 맞으면 수평 속도 0 + 공중 대시 끊김 + 착지 경직 표시), `DashState`(공중 대시: 세로 속도 0으로 출발, 중력 × `airDashGravityScale` 0.2, 공격 무시, 끝나면 AirState로 떨어짐, 착지하면 Landing), Idle·Run·DashStop(점프·낙하 검사), `MeleeSystem`(공중·착지 경직 중 가드·패리 불가, 펀치 높이 차 > `Punch.heightTolerance` 2m면 빗나감), `WeaponSystem`(공중·착지 경직 중 공격·스킬 입력 무시), `AnchorClaw`(공중·착지 경직 중 발사 불가 — 땅에서 쏜 클로는 원래 3차원으로 끌어 공중 상대도 끌어내림), `BoostGauge`(`jumpCost`, 회복은 Idle·Run만이라 공중·착지 경직엔 안 참), `MechVisualRig`(공중이면 접지 보정 멈춤), `MechCamera`(점프 락온 동안 카메라 yaw를 몸에 바로 붙임), `CombatFeedback`(착지 먼지·흔들림, 경직이 있으면 발밑에 LANDING / HARD LANDING 월드 글자 — 두 화면 모두), `PlayLogger`(`jump`, `air_dash`, `landing_lag`, `landing_lag_long`, `dash_exit_landed`), `BernardTuning`. 하드락·공격 전환 거리는 원래 3D 거리. 연습 봇은 점프하지 않고, 이동·조준이 수평만 써서 공중의 플레이어에게 날아오르지 않음(펀치는 높이 허용으로 빗나감). 위험: 접지 검사는 기체 루트 콜라이더 기준(자식 콜라이더 무시). 레이어 메뉴를 안 돌리면 대체 규칙으로 동작. 이 작업 내내 에디터가 프로젝트를 열고 있어 batchmode 스모크 테스트를 못 돌림(게임·에디터 어셈블리를 Unity 컴파일러로 직접 컴파일해 오류 0 확인).
- **V6** 완료. 모든 값은 `BernardTuning → 이동`. 신규 `BernardTuning.DirectionalValues`(앞/앞대각/옆/뒤대각/뒤 5점, 사이 선형 보간, `Evaluate(각도)`·`Evaluate(스틱)`). 삭제한 튜닝: `maxRunSpeed`·`closeMoveSpeed`(→ `walkSpeed` 방향별), `dashSpeed`(float → 방향별), `dashCancelLockTime`, `jumpHeight`·`jumpRiseTime`(→ `jumpRiseSpeed` 23·`jumpGravity` 31.5), `airMoveSpeed`(float → 방향별), `airDashGravityScale`(→ `airDashRisingGravityScale` 0.5), `MechController.CloseMoveSpeed`, `AirState.JumpGravity`. 바뀐 파일: `MechController`(걷기 = 스틱 0.3 이상이면 방향별 최대 속도, 하드락 안도 같은 속도·상대 기준 방향; `DashSpeed` = 대시 표 최댓값), `DashState`(누른 순간 스냅 각도로 방향별 속도, 시작 0.27초엔 공격·점프 불가, ○ 재입력은 언제든 끝 단계로, 지속 1.4초 지나면 `Timeout`, 지속 단계 △ = 대시 점프, 대시 어택 경직 = 방향별 `dashAttackRecovery`(차징 태클은 0.5초 그대로), 공중 대시는 세로 속도 유지 + 상승 중 중력 ×0.5), `DashStopState`(들어온 속력을 0.27초에 0까지 선형 감속, 스틱 무시, 공중 대시 끝 단계 모드 → 끝나면 낙하·착지 경직), `AirState`(이륙 준비 0.033초 → 23m/s, 중력 31.5, 공중 이동 방향별, `CanAirDash` = 뜬 뒤 상승 중), `MechStateMachine`(공중 대시는 상승 중에만, 공중 끝 단계도 `IsAirborne`), `WeaponSystem`(대시 시작 단계 공격 불가 — 버퍼), `MechCamera`(화면 구도 계산: FOV 70 → 피치 14.15°, 뒤 3.26m, 발바닥 위 3.42m), `PlayLogger`(`dash_exit_timeout`·`dash_exit_jump`), `MeleeDummy`(봇 패리 타이밍 = 앞 대시 속도), `MechVisualRig.Gait`(로그 문구). 에셋: `Resources/BernardTuning.asset`에 새 칸이 없어 코드 기본값이 그대로 들어간다(옛 `maxRunSpeed` 20 등은 더 안 읽힘). 위험: 걷기 22m/s가 예전 근접 8m/s보다 훨씬 빨라 펀치 거리 조절·봇 접근이 달라진다. 대시 최대 1.67초라 태클 차징(1.5초)은 거의 안 차고 최대 차징(3초)은 불가 — QUESTIONS V6. 카메라가 예전보다 훨씬 가깝다(6m → 3.26m).
- **V6.1** 완료. 바뀐 파일: `DashStopState`(땅 끝 단계에서 △ = `TryStartJump` 내내, `AllowsAttack` = `dashStopAttackDelay` 0.05초 뒤), `WeaponSystem`(끝 단계 공격은 `AllowsAttack`부터), `MechStateMachine`(`CanStartLunge`에 끝 단계 0.05초 뒤 포함 — 런지가 버퍼에서 사라지지 않게), `BernardTuning`(`dashStopAttackDelay`; `Tackle.chargeStartTime`·`fullChargeTime` → `dashChargeStartTime` 0.8·`dashFullChargeTime` 1.6 — 에셋에 예전 1.5·3이 저장돼 있어 이름을 바꿈), `DashState`(차징 보간 구간), `MechCamera`(`baseFov` → `baseFieldOfView` 52 — 씬에 저장된 70을 안 읽게 이름 바꿈), 신규 `Core/SplitScreenLetterbox`(VERSUS 각 반쪽 안 16:9 영역, 넓은 모니터면 좌우 띠, 화면 크기 바뀌면 다시 계산, 깊이 낮은 배경 카메라가 화면 전체를 검게 지움), `GameManager`(`letterboxVersus`·`versusAspect`, `ApplyCameraLayout`), `.gitignore`(PerformanceTestRun*.json). HUD는 각 카메라의 Screen Space - Camera 캔버스라 자동으로 16:9 안(캔버스가 기준 해상도 1920×1080 그대로가 됨 — 예전엔 세로로 긴 1920×2160). 위험: 전체 화면 오버레이 캔버스(라운드 표시·메뉴)는 띠까지 포함한 전체 화면 기준이라 그대로. 화면 밖 화살표 크기는 Screen.height 기준이라 예전과 같음.
- **V8** 완료. 신규 `Movement/V8/`: `MechMotor`(60Hz 틱 파이프라인·적분·지면·벽·외부 속도·기체 분리·자동 선회·카메라 방위), `VoTickDriver`(물리 주기 1/60, 카메라 → 1P → 2P), `VoLever`(레버 누적값 → 레버 이동 벡터), `VoState`(서기·걷기·근접 스텝·대시 정지·급강하), `VoGroundDashState`, `VoJumpState`(+ `VoAirDashState`), `DashAttackRunner`(V8용 대시 어택·태클 — 예전 DashState 공격 로직 복사); `Movement/MotionBridge`(외부 속도 통로), `Movement/IDashMove`, `Mech/BernardTuning.V8.cs`(`MovementModel` 스위치 + `V8Values` 원본 단위). 바뀐 파일: `MechStateMachine`(V8 상태·`IsV8`·`ActiveDash`·`CurrentState`·V8 전환 도우미, 예전 상태 요청을 V8 상태로 바꿔 넣음, V8이면 Update/FixedUpdate에서 상태를 안 돌림, 질의(`IsDashing`·`IsAirborne`·`IsDashStopping`·`IsIdle`·`IsRunning`·`CanStartLunge`·`IsJumpTurning`·`DashEndAllowsAttack`)에 V8 포함), `MechController`(모터 붙임·`Velocity`·V8이면 OnCollision 무시·지면은 모터 값), `DashState`/`AirState`(이벤트 Raise, IDashMove), `InputReader`(`JumpHeld`, ○·△ 누름 횟수), `MeleeSystem`·`KnockdownController`·`ThrusterLunge`·`AnchorClaw`·`GameManager`(MotionBridge), `WeaponSystem`(ActiveDash, 공격 시 사격 조준), `MechCamera`(V8 틱·보간, V6d 구도 계산 삭제 → 예전 이동기는 V3 백뷰), `HUDController`·`CombatFeedback`·`MechVisualRig`·`PlayLogger`(ActiveDash·속도·`close_step`·`dive`), `BernardTuning`(partial). 위험: 실제 조작으로는 아무것도 확인 못 함(스모크는 타이틀 10초). 레버 누적값 때문에 걷기 속도가 0.8배로 시작해 8.5초에 걸쳐 1.2배까지 오름. 하드락 몸통 자동 회전이 없어 펀치·태클 조준은 수동 선회·사격 조준에 달림. 키네마틱이라 예전 OnCollision 기반 대시 벽 종료·몸 충돌은 모터 캐스트로 대체.
- **V8.1** 완료. 바뀐 파일: `VoLever`(누적값 모델 삭제 → 8방향 코드·스틱 방향·입력 축소 배율만), `BernardTuning.V8`(`scaleByLeverMagnitude`·레버 표·`diveStickY`·호버·`gatherFall` 삭제, `neutralDashWaitFrames` 15 추가), `MechMotor`(급강하 = 공중 △ 새 입력, 중립 ○ = 앞 잠금, 근접 구역 서기면 15f 기다림), `VoState`(걷기·점프 속력 = 곡선 × 입력 축소, 대시 조건), `VoJumpState`(상승·하강 모두 △ = 급강하, 재점프·호버 삭제). 문서: QUESTIONS V8 답, TEST_CHECKLIST V8.1, `docs/REFERENCE_MOVEMENT.md` 커밋. 위험: 근접 구역 서기 중립 ○는 0.25초 늦게 나감. 실제 조작 확인은 아직.
- **V8.2** 완료. 바뀐 파일: `MechMotor`(중립 ○ 기다림 삭제 → 바로 앞(0) 잠금 + `LockedNeutralClose`, 급강하 = 공중 + L스틱 아래 + △), `VoGroundDashState`(근접 구역 중립 ○ 앞 대시는 15f 안 스틱이면 `V8CloseStep(방향, fromDash)`), `VoCloseStepState`(`FromDash`·`Heading`), `VoJumpState`(재점프·호버 복원), `MechStateMachine`(`IsDashMotion`·`DashMotionHeading`), 외형·연출(`MechVisualRig` 3파일·`CombatFeedback`·`MechCamera` 처짐)이 `IsDashMotion`을 봄, `BernardTuning.V8`(`closeStepFromDashFrames`, `diveStickY`·호버 수치 복원), `DashExitReason.CloseStep`·로그 `dash_exit_close_step`. 위험: 대시 → 근접 스텝 전환 때 속력은 곡선 시작값으로 바로 바뀜(앞 24.1 → 옆 29.8m/s).
- **V8.3** 완료. 바뀐 파일: `MechCamera.TickV8`(기준점 위치 35%·방위 12%를 따로 추종, 선회 리드는 그 위에 더함, 가로 상한은 기준점을 카메라 오른쪽 축으로 당김), `BernardTuning.V8`(`cameraFollow` 삭제 → `cameraYawFollow` 0.12·`cameraPositionFollow` 0.35·`cameraMaxScreenOffset` 0.25). 위험: 빠른 선회(759°/s) 중엔 방위가 크게 늦는다(정상 상태에서 약 90° — `Camera Yaw Follow`로 조정). 옆 대시(22.3m/s)의 처짐은 35%에서 약 0.7m라 상한(약 2.6m)에는 평소 안 닿는다.
- **V7c** 완료. 바뀐 파일: `BoostGauge`(대시 초당 소모·`GetJumpCost`·`GetSideStepCost` 삭제), `BernardTuning`(`Boost.dashDrainRate`·`sideStepCost`·`jumpCost`, `Movement.exhaustedLandingLag` 삭제), `MechStateMachine`(`TryStartDash`·`TryStartJump`·`TryStartJumpV8`에서 부스트 검사 삭제, `Land`는 항상 `landingLag`), `LandingState`(`OnLanded`가 경직 시간만 넘김), V8 상태(`BoostExhausted` 삭제 — `VoState`·`VoGroundDashState`·`VoJumpState`), 예전 이동기(`DashState`·`IdleState`·`RunState`·`SideStepState`), `CombatFeedback`(HARD LANDING 문구 삭제)·`PlayLogger`. 위험: 대시·점프를 막는 자원이 없어졌다(대시 연타는 대시 게이지 허가 < 24만 남음). EXHAUSTED 팝업·HUD 탈진 색은 런지로 0%가 됐을 때만 뜬다.
- **V7a** 완료. 신규: `Editor/BellforestBuilder`(메뉴 + batchmode용 `BuildFromCommandLine`), `Materials/ToonFlat.shader`(스페큘러 없는 두 단계 툰 — 그림자 받고 드리움, 외곽선용 깊이·노멀 패스), `Editor/MapPreview`(시점 5곳을 `Builds/MapPreview/*.png`로 저장), `Arena/Materials/M_Bell_*`·`Arena/Generated/Bell_*`. 바뀐 파일: `AsteroidArenaBuilder`·`GroundLayerSetup`(공용 도우미를 internal로), `SampleScene.unity`(batchmode로 메뉴를 실행해 저장 — Bellforest 생성, AsteroidArena 끔, 스폰 (0, ∓25)), `TagManager.asset`(레이어 8 Walkable·9 Wall 추가 — 지금까지는 레이어가 없어 대체 규칙으로 돌고 있었다). 위험: 에디터에서 플레이로 확인 못 함(미리보기 그림과 스모크 테스트만). 테두리 벽이 보이지 않는 Wall이라 벽을 등지면 카메라가 당겨진다(소행성 맵과 같은 동작).
- **V7b** 완료. 신규: `Materials/DaySky.shader`, `Arena/Materials/M_BellSky`. 바뀐 파일: `BellforestBuilder`(`BuildSkyAndLight` — 스카이박스·해 50°/방위 35°/세기 1.3·3색 앰비언트·환경 반사 0.35·안개 끔), `SampleScene.unity`(batchmode로 다시 적용), `AppRuntime`(`IsSmokeTest`), `MechMotor`(스모크 테스트면 잰 키를 Player.log에 남김, 기본 키 1.93), `BernardTuning.V8`(`fallbackBodyHeight` 2.6 → 1.93). 반사 얼룩: 맵은 스페큘러 없는 ToonFlat이라 해 쪽을 보는 낮은 시점 미리보기(`5_low_toward_sun`)에서도 갑판에 얼룩 없음. 위험: 실제 키가 1.93m라 V8 이동은 기획서 m/s의 0.74배로 돌고 있다(QUESTIONS V7b) — 맵 높이(5m·11m)는 점프 8.4m 기준이라 다시 봐야 한다.
- **V9a** 완료. 바뀐 파일: `.inputactions`(`Cross` 액션 추가 — 패드 ✕(buttonSouth)·마우스 오른쪽 버튼. 예전 `Guard` 액션과 L2 바인딩은 지우지 않고 남겼지만 아무도 읽지 않는다), `InputReader`(`GuardHeld`·`GuardPressedThisFrame`이 `Cross`를 읽음, `GuardPressCount` 추가), `MechMotor`(급강하 = 공중에서 ✕ 새 입력, 가드 자동 선회 — 가드 중 수동 선회 무시), `VoJumpState`(상승 중 △ 재입력 삭제), `BernardTuning.V8`(`diveStickY` 삭제, `guardTurnInside` 400·`guardTurnOutside` 1092 추가). 위험: ✕는 메뉴의 `Submit`(패드 A)과 같은 버튼이다 — 매치가 끝난 화면에서 가드를 누르고 있으면 바로 넘어갈 수 있다. 가드 자동 선회는 V8 이동기에만 있다(Legacy는 하드락 몸통 회전 그대로).
- **V9b** 완료. 바뀐 파일: `BellforestBuilder`(`StepHeight` 5 → 3.7, `PlateauHeight` 11 → 8.1), `Arena/Generated/Bell_Plateau_*`·`Bell_Step_*`, `SampleScene.unity`(batchmode로 메뉴 재실행). 점프 궤적(QUESTIONS V9b): 일반 점프 최고점 6.12m — 땅 → 발판 +2.42m, 발판 → 고지대 +1.72m 여유, 땅 → 고지대는 1.98m 모자람. 위험: 점프 직후 공중 대시(상승 중 중력 절반)를 쓰면 최고 약 12m라 땅에서 고지대로 바로 올라간다.
- **V9c** 완료. 바뀐 파일: `MechCamera.TickV8`(방위 = 이동기 카메라 방위 + 선회 리드, 12% 방위 추종 삭제, 시선 표적 높이 오프셋이 12%로 목표값을 따라감), `BernardTuning.V8`(`cameraYawFollow` 삭제 → `cameraTargetOffsetFollow` 0.12). 위험: 카메라 모드가 하나라 시선 표적 오프셋 목표가 일정해서 12%는 지금 화면에 보이는 효과가 없다(플레이 중 `Camera Target Up`을 바꿀 때만 보임).
- **V9d** 완료. 바뀐 파일: `GameManager`(`menuConfirmDelay` 0.5초 — `OpenMenu` 시각부터 실제 시간으로 재고 그동안 확인만 버림, 위아래 고르기는 바로). 타이틀·매치 종료·연습 일시정지가 모두 같은 `OpenMenu`를 쓴다. 라운드 결과 화면은 입력 없이 타이머로 넘어가서 해당 없음. 위험: 타이틀에서도 0.5초 동안 확인이 안 먹는다(의도).
- **V9e** 완료. 바뀐 파일: `MechMotor`(`ManualTurnRate`가 선회 입력 방향을 봄 — 상대 반대쪽이면 항상 '정면 안' 선회율, 상대 쪽이면 `OutsideBlend`로 33.75°~60° 선형 보간. 가드 자동 선회 132 → 360°/s도 같은 보간), `BernardTuning.V8`(`turnBlendEndAngle` 60 추가). 위험: 원본과 다른 규칙이다(원본은 방향을 안 가림). 마우스 선회는 선회율과 무관해 그대로. 근접 구역·지상 대시의 '정면 안' 값(512/549, 320)도 같은 규칙을 탄다.
- **V9f** 완료. 바뀐 파일: `BoostGauge.cs` 삭제, `BernardTuning`(`BoostValues`·런지 `boostCost` 삭제, 런지 `cooldown` 3 추가), `ThrusterLunge`(`IsOnCooldown`·`CooldownRemaining`, 시작 순간부터 쿨다운, 라운드 리셋 때 초기화), `WeaponSystem`·`MeleeDummy`(쿨다운 중 공격 입력을 버퍼에서 지움), `CombatFeedback`(EXHAUSTED 팝업 삭제), `HUDController`(부스트 호 삭제), `MechController`·`GameManager`·`VersusSceneSetup`(참조 삭제), `PlayLogger`(CSV의 `p1_boost`·`p2_boost` 열과 `dash_exit_exhausted` 집계 삭제), `DashState`(`DashExitReason.Exhausted` 삭제), `SampleScene.unity`(batchmode로 `BoostGauge` 컴포넌트 2개·`BoostGaugeArc`와 배경 4개 삭제). 위험: 쿨다운이 화면에 안 보인다(HUD 표시 없음). CSV 열이 줄어 예전 로그와 열 위치가 다르다. `BernardTuning` 에셋 파일에는 예전 `boost` 값이 에디터에서 다시 저장될 때까지 남아 있다(읽히지 않음).
- **V9g** 완료. 바뀐 파일: `HUDController`(`lungeCooldownArc` + 색·반짝임·흔들림 수치), `ThrusterLunge`(`CooldownDuration`, `OnCooldownDenied`·`NotifyCooldownDenied`), `WeaponSystem`(쿨다운으로 입력을 버릴 때 알림), `Assets/Editor/LungeCooldownHudSetup.cs`(메뉴 `Mydongari/Setup Lunge Cooldown Arc`), `SampleScene.unity`(batchmode로 메뉴 실행 — HUD 2개에 `LungeCooldownArc`·`LungeCooldownBG` 생성·연결). 위험: 씬에서 예전 부스트 호는 화면 가운데 **오른쪽**(x +200, 히트 호가 왼쪽 −200)이었다 — 그 자리에 그대로 넣었다(QUESTIONS V9g).
- **CL1** 완료. 바뀐 파일: `BernardTuning`(`MovementValues`의 `diveStartSpeed`·`diveAccel`·`diveMaxSpeed` 삭제 — 참조 0), 신규 `Movement/DashExitReason.cs`(enum만 옮김), `MechStateMachine`(`OnDashStarted`·`OnDashEnded`·`OnJumped` + `Notify*`), `DashState`·`AirState`·`VoGroundDashState`·`VoJumpState`(상태 머신 이벤트로 알림), `PlayLogger`·`CombatFeedback`(구독처 변경). 스모크 테스트: 빌드 성공, Exception 0·Error 0·NaN 0. 위험: 낮음(이벤트 발생 시점·순서는 그대로).
- **CL2** 완료. 바뀐 파일: `MechStateMachine`(`ToStand`·`ToStandOrWalk` 추가, `RedirectForV8`와 `ChangeState`의 상태 바꿔치기 삭제, `DashMotionHeading` null 대비), `Health`·`LungeState`·`KnockdownState`·`LandingState`(`ChangeState(IdleState/RunState)` → `ToStand`/`ToStandOrWalk`), `CombatFeedback`·`HUDController`(`ActiveDash` null 대비). 스모크 테스트: 빌드 성공, Exception 0·Error 0·NaN 0. 위험: 낮음 — V8에서 가는 상태는 예전 `RedirectForV8`과 같다(Idle → 서기, Run → 걷기).
- **CL3** 완료. 지운 파일 7개(+ .meta 7개): `IdleState`·`RunState`·`DashState`·`DashStopState`·`AirState`·`SideStepState`·`StickGestureDetector`. 바뀐 파일: `MechStateMachine`·`MechController`(V8 경로만 남기고 다시 씀 — 레거시 상태·`IsV8`·R스틱 회전 누적·하드락 몸통 회전·리지드바디 이동 API·접지 검사·대시 벽 충돌 삭제), `MechMotor`(`RefreshModel` → 시작할 때 한 번 `Activate`), `BernardTuning`(`MovementModel`·`movementModel` 삭제, `MovementValues`에서 죽은 값 23개 삭제 — 남은 것: `walkSpeed`·`dashSpeed`·`knockbackMoveHold`·`bumpSpeed`·`dashAttackRecovery`·`mouseTurnSensitivity`·`landingLag`·`groundMinNormalY`), `MechCamera`(V3 백뷰·속도 FOV와 그 필드 삭제), `InputReader`(사이드스텝 더블탭·스틱 튕기기 삭제), `IDashMove`(`EndByWall` 삭제), `PlayLogger`(`sidestep` 열은 남기고 항상 0), `WeaponSystem`. 스모크 테스트: 빌드 성공, Exception 0·Error 0·NaN 0, 씬·에셋·프리팹 diff 없음. 위험: ① 연습 봇 경로(`MotionBridge`의 리지드바디 분기, `ThrusterLunge`의 상태 머신 없음 분기)는 건드리지 않았다. ② 동작 차이 하나 — 라운드 리셋 직후 카메라 FOV를 예전 백뷰 값(52) 대신 V8 값(35)으로 맞춘다(예전엔 다음 틱까지 52가 한 번 보일 수 있었음). ③ `.inputactions`의 `StepLeft`·`StepRight`·`MoveStick` 액션은 규칙대로 지우지 않았다(읽는 코드만 없음).
- **CL4** 완료. 신규: `Movement/V8/IVoHost.cs`(`IVoHost`·`VoInputFrame`), `Movement/V8/IVoMachine.cs`(`IVoMachine`·`IVoDashAttack`·`VoDashAttackResult`), `Movement/V8/VoCameraRig.cs`(`MechCamera.TickV8` 계산을 그대로 옮김), `Movement/GameState.cs`(착지 경직·런지·넉다운의 바탕), `Movement/VoHostAdapter.cs`·`VoInputAdapter.cs`(브리지). 바뀐 파일: `MechMotor`(`MechController`·`MechStateMachine`·`InputReader`·`MeleeSystem`·`MechIdentity` 참조 삭제 → `Host`/`StateMachine` 인터페이스, `Bind`·`Nudge` 추가, `SeparateFromOpponent`는 `VoHostAdapter.AfterIntegrate`로), `VoState` 계열(생성자 `IVoMachine`, 스턴·콤보 감속은 `Host`), `VoGroundDashState`(`IVoDashAttack`), `DashAttackRunner`(인터페이스 구현), `VoTickDriver`, `MechState`(상태 머신 참조 없는 바탕), `LandingState`·`LungeState`·`KnockdownState`(`GameState` 상속), `MechStateMachine`(`IVoMachine` 구현), `MechController`(호스트 생성·`Bind`, `Update`에서 마우스 누적, `NotifyAttackStarted`), `MechCamera`, `WeaponSystem`. 틱 안의 호출 순서는 그대로다. 스모크 테스트: 빌드 성공, Exception 0·Error 0·NaN 0. 위험: ① 마우스 이동량 누적이 `MechMotor.Update`에서 `MechController.Update`로 옮겨져 스크립트 실행 순서에 따라 한 프레임 차이가 날 수 있다(패드 조작은 영향 없음). ② 코어(`Movement/V8`)에 남은 게임 의존은 `BernardTuning.V8Values` 타입뿐 — CL5에서 `VoSpec`으로 바꾼다. `DashAttackRunner`는 브리지 소속이라 CL6에서 폴더를 옮긴다.
- **CL5** 완료. 신규: `Movement/V8/VoSpec.cs`(`VoSpec` — 기본값 = 사양서 원본, 인스펙터 노출 없음 / `VoDirectional` / `VoUnits`), `Movement/StyleOverrides.cs`(ScriptableObject — 원본과 다르게 쓰는 값만, 칸마다 켬/끔). 삭제: `Mech/BernardTuning.V8.cs`(`V8Values` 전체 — 에셋에 `v8` 블록이 없어 잃는 값 없음). 바뀐 파일: `MechMotor`(`V`가 `VoSpec`, 코드에 박혀 있던 V9e 방향 제한·중립 ○ 앞 대시를 스위치로, 사격 조준 선회율은 `NotifyAttackStarted(rate)`로 받음, 스모크 테스트일 때 유효값·원본과 다른 칸을 로그에 남김), `VoJumpState`(상승 중 재점프를 `risingRejump` 스위치 뒤에 복원 — 우리 값은 끔, 하강 중 급강하는 `diveAllowedWhileFalling`), `VoLever`(스틱 문턱은 입력 어댑터로), `VoCameraRig`(FOV는 호스트가 줌), `VoHostAdapter`·`VoInputAdapter`·`MechController`·`MechCamera`, `BernardTuning`(`DirectionalValues` → 코어 `VoDirectional`). **검증:** 스모크 테스트 Exception 0·Error 0·NaN 0, 정리 전 `V8Values` 기본값 77개와 CL5 유효값이 전부 같음(스크립트 대조), 옮긴 5개(`stickThreshold` 0.3·`guardTurnInside` 400·`guardTurnOutside` 1092·`attackAimRate` 2560·`cameraFov` 35)는 `StyleOverrides` 코드 기본값이 같은 값. 위험: `StyleOverrides.asset`이 아직 없어 코드 기본값으로 돈다(CL7에서 저장). `risingRejump` 경로는 V8.2 코드를 되살린 것이라 켰을 때의 동작은 에디터 확인 필요(기본은 꺼짐).
- **CL6** 완료. 스크립트 70개를 `.meta`와 함께 `git mv`: `Movement/V8/*`·`MechState`·`DashExitReason`·`IDashMove` → `Core/VirtualOn/`(상태는 `States/`), `MotionBridge`·`MechStateMachine`·`MechController`·`VoHostAdapter`·`VoInputAdapter`·`GameState`·`StyleOverrides`·`DashAttackRunner`·`MechCamera`·`MechIdentity`·`ExternalControlLock`(파일 이름 대소문자 바로잡음) → `Bridge/`, 나머지 → `Game/{Combat,States,Knockdown,Heat,Tuning,Bot,Input,UI,Feedback,Visual,Flow}/`. 네임스페이스를 폴더대로 바꾸고 using을 다시 씀(`Assets/Editor` 13개 포함), 한정 이름(`Combat.KnockdownController` 등)은 단순 이름으로. `MechMotor` → `VoMotor`(씬에 없는 런타임 컴포넌트), 지면·벽 판정은 `VoMotor.Collision.cs`(partial)로 파일만 나눔. 빈 예전 폴더와 폴더 `.meta` 삭제. **검증:** 컴파일 오류 0, 스모크 테스트 Exception 0·Error 0·NaN 0, 유효 수치 77개 동일, 씬·프리팹·에셋이 참조하는 프로젝트 스크립트 GUID 30개 전부 존재(대조 스크립트), 씬·에셋·프리팹 파일 변경 없음, `Core/` 아래 `using MyDongari.Bridge|Game` 0건. 위험: 씬 파일의 `m_EditorClassIdentifier` 문자열은 예전 네임스페이스로 남아 있다(스크립트는 GUID로 찾으므로 동작에는 영향 없고, 에디터에서 씬을 저장하면 Unity가 고쳐 쓴다 — 그때 씬 diff가 크게 보일 수 있음).
- **CL7** 완료. 신규: `Assets/Editor/StyleOverridesSetup.cs`(메뉴 `Mydongari/Tuning/Create Style Overrides Asset`·`Save Tuning Assets To Disk`, batchmode용 `SaveFromCommandLine`), `Assets/Resources/StyleOverrides.asset`(Unity가 코드 기본값으로 생성). `Assets/Resources/BernardTuning.asset`을 Unity가 다시 저장 — 빠진 것은 코드에 없는 고아 칸 33줄(`boost` 블록, 예전 `movement` 칸, `chargeStartTime`·`hardEnterRange` 등)뿐이고 기존 칸 값은 그대로, 없던 블록은 코드 기본값으로 채워짐. 문서: `CLAUDE.md`(폴더 표, 원본값 고정·IVoHost·MotionBridge 규칙), `PLAN_CLEANUP.md` 7절(계획과 다르게 된 것, 값이 놓인 곳, CL8 후보), `PLAN_V8.md` 머리말, `SETUP.md` 96, `QUESTIONS.md`. **검증:** 에셋을 읽는 빌드로 스모크 테스트 Exception 0·Error 0·NaN 0, 유효 수치 77개 동일. 남은 위험: ① 스모크 테스트는 10초 동안 타이틀에서 오류·NaN만 본다 — 조작감·전투 연결은 TEST_CHECKLIST의 CL 항목을 에디터에서 직접 봐야 한다. ② 씬의 `m_EditorClassIdentifier`는 다음에 씬을 저장할 때 Unity가 새 네임스페이스로 고쳐 쓴다. ③ CL8(실제 속도 m/s 연결)은 손대지 않음.

## 전투 재구성 — 버추얼온(템진) 전투 뼈대 위에 다시 얹기 (2026-11-15)

근거: `docs/REFERENCE_COMBAT.md`(전투 사양서 1~13장), `docs/REFERENCE_MOVEMENT.md`(개정판). 편차 목록: `docs/PLAN_CLEANUP.md` 3절 (다).

- [x] **전투A** 이동 빈칸: 근접 스텝 5점 속력·기체 키 1.93m를 `VoSpec` 원본으로(덮어쓰기 삭제), 대시 방향 재입력, 하강 가속. 정면 부호 대조는 보고만(아래 메모).
- [x] **전투B** 전투 틱과 처리 순서: `VoTickDriver`가 카메라 → 피격 판단(전원) → 1P → 2P → 판정 모으기(전원). `IVoHost.SettleHits/IsHitStopped/EndTick`, `IVoTickUnit`. 연습 봇도 같은 틱.
- [x] **전투C** 근접 구역 A와 공격 선택 우선순위. `VoCombatSpec`·`VoMeleeZone` 추가, `meleeSwitchRange` 삭제.
- [x] **전투D** 파고들기 펀치: `VoMeleeDrive`·`VoMeleeState`, 근접 추적, ② 단계 예약 콤보, 근접 취소.
- [x] **전투E** 명중 판정: 공격 구 쓸기 vs 피격 캡슐(`HitGeometry`), 한 휘두르기 한 번, 점프 중 캡슐 축소.
- [x] **전투F** 충격치와 피격 반응: `HitRules`·`ImpactMeter`, 경직·다운·날아감·공중 다운·기상(`KnockdownController` 공용화).
- [x] **전투G** 뛰어 베기: `VoJumpMeleeState`, 미확인 값은 `StyleOverrides` [미상].
- [x] **전투H** 가드 정리와 기체별 히트스톱 15f (`HitStop.cs` 삭제, 씬에서도 batchmode로 제거).
- [x] **전투I** 선입력: 공격 버퍼 4f, 착지 취소, 공중 예약 삭제.
- [x] **전투J** EditMode 테스트 17개(`Assets/Editor/Tests/CombatRulesTests.cs`, batchmode `-runTests`로 통과 확인), 문서, 튜닝 에셋 재저장.

메모:
- **검증:** A~J 커밋마다 `Tools/Build And Smoke Test`(batchmode) Exception 0·Error 0·NaN 0. 스모크는 10초 동안 타이틀에서 오류만 본다 — **전투 감각·실제 명중·반응은 에디터에서 `TEST_CHECKLIST.md`의 (전투A~J) 항목으로 직접 확인해야 한다.**
- **정면 부호 (보고만, 고치지 않음):** 사양서는 정면 = (−sin 방위, cos 방위), +방위가 +z에서 −x 쪽. 코어(`VoMotor.Forward`, `OpponentBearing = atan2(x, z)`)는 Unity Y 회전 그대로라 정면 = (+sin, cos), +각이 +x 쪽(오른쪽 +). 즉 코어의 방위 = −(사양서 방위)로 좌우가 거울상이다. 코어 안에서는 한 가지 부호로 일관돼 동작에는 문제가 없고, 사양서의 "+방위 = 왼쪽" 표현을 코드에 옮길 때만 부호를 뒤집어 읽어야 한다(방향 코드 → 오프셋 `VoLever.Offset`이 이미 그렇게 한다).
- **바뀐 주요 파일:** Core `VoTickDriver`·`VoMotor`·`VoSpec`·`VoCombatSpec`(신규)·`VoMeleeDrive`(신규)·`States/VoMeleeState`·`VoJumpMeleeState`(신규)·`IVoHost`·`IVoMachine` / Bridge `VoHostAdapter`·`MechStateMachine`·`MechController`·`MotionBridge`·`DashAttackRunner`·`StyleOverrides` / Game `MeleeSystem`(다시 씀)·`CombatLedger`·`HitRules`·`ReactionTimers`·`HitGeometry`(신규)·`HitTypes`·`WeaponSystem`·`ThrusterLunge`·`AnchorClaw`·`ImpactSlam`·`KnockdownController`(다시 씀)·`MeleeDummy`·`BotInput`·`InputReader`·`BernardTuning`.
- **남은 위험:** ① 외형(`MechVisualRig`)은 시간 값의 출처만 바꿨다 — 펀치 예비 동작이 0.1초 → 원 0.32초(16 + 3f)로 길어져 포즈 타이밍이 어색할 수 있고, 뛰어 베기·경직·가드 경직 전용 포즈는 없다(기존 스턴 흔들림·넉다운 포즈 재사용). ② 뛰어 베기의 [미상] 값(특히 중력 0.451)은 맞는지 실기 확인 필요. ③ 연습 봇은 여전히 다이내믹 리지드바디라 밀려남·다운 궤적이 물리 감속·충돌의 영향을 조금 받는다. ④ 근접 추적 표적은 지연 표적이 아니라 상대의 지금 위치. ⑤ 기상 대시·기상 공격, 맞교환 동시 판정은 2인이 있어야 확인된다.

### 전투 재구성 후속 (2026-11-15)

- [x] **피해량** 템진 원본 비율로: `VoCombatSpec.damage…`(원본) + `StyleOverrides.damageScale`(10 ÷ 290). 슬램 10·파일 드라이버 50은 `BernardTuning` 그대로.
- [x] **전투 스모크** `Tools/Combat Smoke Test`(`Game/Flow/CombatSmokeRunner`, batchmode `BuildAndSmokeTest.RunCombatFromCommandLine`). 플레이어 자리 자동 입력은 `InputReader.SetScripted/ScriptState/ScriptPress…`. 첫 실행 결과: 경직 2 / 다운 2 / 날아감 2 / 가드 경직 1 / 맞딜 2, 예외 0, 겹친 틱 0, 맵 밖 0 → PASS.
- 메모: 전투 스모크로 확인된 것 — 원 10 → 투 4.5 → 쓰리 13.1 피해가 들어가고, **원·투를 맞힌 뒤의 쓰리는 누적 충격치(약 520 + 1100)가 날아감 문턱 1200을 넘어 다운이 아니라 날아감**이 된다(쓰리 단독 1100이면 다운). 다운은 대시 공격·뛰어 베기에서 나왔다. 스모크는 평지 한 줄에서 봇 상대로만 돈다 — 2인 대전·벽 근처·기상 선입력은 여전히 에디터 확인 대상.

### 원본 복원·봇·2인 스모크·로그 (2026-11-15)

- [x] **지연 표적** 원본 복원: `Core/VirtualOn/VoDelayedTarget`(신규), `VoMotor.DelayedTargetBearing`를 근접 추적(원·투·쓰리·뛰어 베기)의 표적으로, 스위치 `VoSpec.delayedTargetEnabled`(원본 = 켬). EditMode 테스트 2개.
  - 남은 위험: "같은 행동"을 상대의 상태로 해석(QUESTIONS 지연표적). 효과는 꾸준히 걷는 상대 기준 약 한 틱만큼 늦게 쫓는 정도.
- [x] **봇 V8** 연습 봇을 리지드바디 이동에서 VoMotor로: `InputReader` 가상 패드 모드(PlayerInput 없으면 스크립트 입력만, 입력 잠금을 따름), 봇 = `BotSenses`(반응 지연 링 버퍼) → `BotBrain`(모드별 행동 선택) → `BotPad`(입력 생성). `BotInput` 삭제. 전투 스모크에 봇 장면 3개(공격만·패리·섞어서).
  - 바뀐 파일: `Game/Bot/*`, `InputReader`, `MechStateMachine`(참조 늦게 찾기), `MechController`(MeleeSystem 자동 찾기), `MeleeSystem`(히트스톱 중에도 봇 두뇌), `GameManager`(사람 기체 판정), `PlayLogger`, `CombatSmokeRunner`.
  - 남은 위험: 봇 두뇌 수치는 정한 값(QUESTIONS 봇V8). 기상 선입력·근접 스텝·옆 대시 회피는 스모크가 직접 보지 않는다(체크리스트). 봇 외형은 이제 상태 머신을 읽어 걷기·대시 포즈가 나온다(외형 코드는 안 건드림).
- [x] **2인 스모크** 전투 스모크에 VERSUS 단계(`Game/Flow/VersusSmokeScenes`): 동시 펀치 맞딜 대칭(자리 바꿔 두 번), 대시 공격 히트스톱이 기체별인지, 벽 2m 앞 런지(임시 벽 상자), 대시 공격으로 공중 다운 → 기상 선입력 대시(점프 거리 세 가지). 60fps·30fps 두 번 돌려 장면별 반응·최종 체력 비교. `GameManager.ResetMechsNow`(모드 무관 리셋) 추가, 스모크 제한 시간 60 → 200초.
  - 찾은 것: ① 맞딜 자체는 대칭(밀림 0.498m 같음)이지만 파고들기 마지막 틱에서 1P가 0.25m 더 들어간다 — 1P가 먼저 움직이고 2P가 1P의 이번 틱 위치를 보는 원본 순서(이동 사양서 1.1). 고치지 않음. ② 런지(강타)는 공중이어도 날아감이 먼저라 공중 다운이 안 난다 — 원본 규칙(전투 사양서 2.1 5번)이라 장면을 대시 공격으로 바꿈. ③ 30fps 단계 첫 맞딜 장면에서만 2P 밀림이 0.514m(60fps 0.498m) — 반응·체력은 같고 허용 오차(5cm) 안. 원인 미확정(프레임 의존인지 장면 이력인지).
- [x] **로그 항목** `PlayLogger`: 대시 시작(지상·공중)·끝(이유 + 틱 수), 착지 경직 중 피격, 근접 취소(무엇으로), 반응 종류 + 충격치 누적값, 히트스톱 시작(때린 쪽·맞은 쪽), 기상 방식, 대시 공격 빗나감·피함. 훅: `MeleeSystem.OnHitStopStarted`·`OnWokeUp`·`OnTackleEnded`·`LastMeleeCancelReason`, `VoMeleeState`/`VoJumpMeleeState.ExitedMidSwing`, `DashAttackRunner.Finish`가 빗나감을 알림. 열 설명은 TEST_CHECKLIST "로그 CSV 열".
  - 남은 위험: `hit_while_landing`·`wake_attack`·`wake_jump`는 스모크에서 한 번도 안 나와 실제 기록은 확인 못 함. 근접 취소 이유를 못 가린 경우 `other`(스모크에서 1건).

---

## 발표 전 종결 (2026-11-16)

패드 테스트에서 찾은 문제 A·B를 먼저, 그다음 1~5. 항목마다 커밋, 커밋 전에 빌드 스모크·전투 스모크·EditMode 테스트.

- [x] **종결A** 붙어서 때려도 펀치가 안 맞던 것. 원인: 공격 판정이 "중심에서 1.8m 앞의 구(반지름 0.5m)" 하나라, 1m 안에 붙은 상대가 정면에서 26° 넘게 벗어나면 구가 닿지 않았다(안쪽 원 12.5u 안에서는 추적 회전도 없다). 판정을 "손 높이에서 몸 앞 0.3m ~ 사거리 끝까지 이어진 캡슐"로 바꿔 지난 틱 → 이번 틱으로 쓸어 검사한다(`HitGeometry.SweptCapsuleGap`, `StyleOverrides.punchCapsule/thirdCapsule` — 예전 `…Sphere` 값은 `FormerlySerializedAs`로 이어받고 `start` 0.3만 새로 생김).
  - 진단 로그: 공격 버튼마다 `events_*.csv`에 `punch_trace` 한 줄(시작 거리·각도·높이차, 구역 여부, 판정 창 동안 최소 틈, 결과). 결과 = `hit`·`guarded`·`parried`·`clash` / `miss_range` 사거리 밖 / `invulnerable` 상대 무적 / `super_armor` 대시 슈퍼아머 / `zone_out_lunge` 구역 밖이라 런지 / `lunge_cooldown` 런지 쿨다운이라 아무것도 안 나감 / `canceled_<이유>` 판정 창 전에 끊김. 훅 `MeleeSystem.OnPunchTraced`·`TraceAttackPress`, `HitDecision/HitInfo.ArmoredByDash`.
  - 표시: 무적이라 무시된 타격 = 회색 작은 불꽃, 슈퍼아머로 버틴 타격 = 노란 불꽃 (`BernardTuning.HitFeel.invulnSparkColor/invulnSparkScale/armorSparkColor`).
  - 스모크: PRACTICE 장면을 이름표(`PracticeScene`)로 바꾸고 "밀착 원투쓰리"(0.8m, P1을 40° 비스듬히) 추가 — 3타 모두 명중해야 PASS. P1 펀치마다 진단 줄을 Player.log에 남긴다. EditMode 테스트 4개 추가(0.6/1.1/1.8m 명중, 비스듬한 밀착, 쓸기, 슈퍼아머 표시).
  - 남은 위험: 캡슐이 몸 앞 0.3m부터라 옆(정면에서 약 80°까지)에 붙은 상대도 맞는다 — 등 뒤는 여전히 안 맞음. 런지·슬램·대시 공격 판정(겹침 구)은 그대로다.
- [x] **종결B** 뛰어 베기 → **내려찍기**로 교체 (우리 규칙, 값은 `StyleOverrides` "내려찍기" 묶음). 점프 중(상승·정점·하강) 공격 버튼, 체공 한 번에 한 번, 이륙 준비 2f에 누른 것도 뜨는 틱에 나간다. 예비 8f(공중 멈칫 + 상대 쪽 선회 844°/s) → 예비 끝 순간 상대 위치로 직선 40m/s, 유도 1.41°/f, 수평 최대 15m, 땅에 닿으면 끝. 일반 근접 판정(가드·패리 가능, 양쪽 히트스톱 15f), 충격치 1100·피해는 원본 점프 근접 값. 맞히면 착지 12f, 빗나가면 30f(공격으로 취소 불가). 내려찍기 중 공중 대시·급강하·가드 없음.
  - 신규 `Game/States/DiveStrikeState`(게임 상태 — 외부 속도 통로로 이동, 틱 끝에 주먹 캡슐 쓸기), `Game/Combat/DiveStrikeMath`(조준·유도, 순수 계산). 삭제 `Core/VirtualOn/States/VoJumpMeleeState`, `IVoMachine.V8JumpMelee`, `MeleeSystem.TryJumpSlash/InJumpSlashWindow`, `StyleOverrides.jumpMelee…` 7칸·`jumpSlashSphere`. `VoCombatSpec`의 점프 근접 원본 칸은 그대로 둠(피해·충격치·추적율을 빌려 쓴다).
  - 바뀐 파일: `MechStateMachine`(`IsDiveStrike`·`CanStartDiveStrike`·`IsJumpTakeoff`·`LandFor`, `IsAirborne`에 내려찍기 포함), `LandingState`(`AllowsAttackCancel`), `VoHostAdapter`(외부 속도 모드), `WeaponSystem`(공중 공격 = 내려찍기, 선입력 4f), `MeleeSystem`(`TryDiveStrike`·`OnDiveStrikeLanded`), `HitTypes`(`AttackType.JumpSlash` → `DiveStrike`), `PlayLogger`(`divestrike_start`·`divestrike_land_hit/miss`), 봇(`BotOrderKind.DiveStrike`, 공격 누르는 틱 3 → 16), `MechVisualRig`(`PoseDiveStrike` — 예비 = `ClawReel`(어퍼컷 준비), 내리꽂기 = `SlamDown`, 새 포즈 없음), 튜닝 에셋 재저장.
  - 정한 값(지시에 없던 것): 유도는 **방위·기울기를 따로** 1.41°/f씩(3D 각 하나로 재면 가파른 내리꽂기가 좌우를 너무 잘 따라간다). 주먹 캡슐 0.2~1.2m·반지름 **0.35m** — 0.5m 이상이면 옆 대시로도 거의 못 피해서 줄였다(0.35면 옆 대시는 0.1~0.3m 차이로 피하고 옆걸음은 맞는다). 진로 최소 기울기 15°(상대가 위에 있어도 땅에 닿게). 패리·무적으로 무효가 된 내려찍기는 "빗나감"(30f)으로 친다, 가드된 것은 "맞힘"(12f).
  - 스모크: 뛰어 베기 장면 → 내려찍기 3장면(정점 명중 · 하강 중 명중 · 8m에서 봇이 옆 대시로 피함 → 착지 경직 30틱 확인). EditMode 테스트 2개 추가(조준·15m 자르기·최소 기울기, 서 있는 상대 명중·옆 대시 회피) + 1개 수정 → 29개.
  - 남은 위험: 예비 8f 동안 카메라가 몸과 같이 최대 844°/s로 돈다(화면이 휙 돌 수 있음 — 패드로 확인). 내리꽂는 중 벽에 닿으면 수평만 멈추고 그대로 떨어진다. 회피 여유가 작아서(0.1~0.3m) 값 조정이 필요할 수 있다 — `diveStrikeCapsule.radius`·`diveStrikeHomingRate`.
- [x] **종결1** 30fps 넉백 차이. **원인은 프레임 의존이 아니라 장면 이력**이었다: 맞딜 장면을 60fps·30fps로 3번씩 돌리니 30fps 단계의 **첫 번째만** 2P 0.514m, 2·3번째는 0.498m(60fps는 셋 다 0.498m — 다만 PRACTICE에서 P1이 봇에게 다운된 판은 60fps 첫 맞딜의 1P가 0.514m였다).
  - 원인 ①(0.514m): `VoMotor._skipExternalDamping`("이번 틱만 감속 생략")이 외부 속도 적분 때만 지워졌다. 넉다운이 끝나는 틱·경직 마지막 틱·히트스톱 틱처럼 외부 적분이 안 도는 틱에 세워지면 그대로 남아, 한참 뒤 **다음 넉백의 첫 틱이 감속 없이** 적분됐다(2m/s × 21틱: 감속 있음 0.4977m / 첫 틱 무감속 0.5143m — 측정값과 일치). 30fps 단계 첫 맞딜의 2P는 바로 앞 60fps 단계 마지막 장면들에서 다운됐던 기체다. → 틱 끝에 항상 지운다.
  - 원인 ②(위치 비교를 넣자 드러난 진짜 프레임 의존, 벽 앞 런지에서 1P가 멈추는 자리 62mm 차이): 감속 요청(`MechController.RequestDamping`)을 `LateUpdate`(화면 프레임)에서 확정해서, 30fps에서는 런지 박기의 감속이 한 틱 늦게 먹었다. 넉다운의 감속 요청도 `Update`에서 넣고 있었다. → `IVoHost.BeginTick`(신규)을 `VoTickDriver`가 틱 맨 앞에 전원에게 부르고, `MechController.BeginTick`이 넉백 유지 시간(`_moveHoldTimer`) 감소와 감속 확정을 한다. `KnockdownController`의 요청은 `CombatTick`으로. 60fps에서의 동작은 그대로다(요청한 다음 틱부터 적용).
  - 넉백 경로에서 `Update`·`Time.deltaTime`·`Time.time`을 쓰는 곳: 위 둘(LateUpdate 감속 확정, KnockdownController.Update)뿐. `MechCamera`의 흔들림 타이머와 `MechController.Update`(마우스 이동량 모으기)는 화면 쪽이라 그대로 둠.
  - 스모크: VERSUS 장면을 9개로(맞딜 A × 3 + 기존 6) — 장면마다 "첫 반응 뒤 밀린 거리"와 "끝난 자리"를 기록하고 60fps·30fps 차이가 **1mm 이하**여야 PASS(`VersusSmokeScenes.FpsPositionTolerance`). 수정 뒤 최대 차이 0.000mm. 스모크 제한 시간 200 → 300초.
  - 남은 위험: 감속 요청을 틱 밖(`Update`)에서 넣는 코드가 새로 생기면 다시 프레임을 탄다 — CLAUDE.md 코드 규칙에 적어 둠.
