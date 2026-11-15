# PLAN CLEANUP — "버추얼온 코어 + 우리 게임 레이어" 구조 정리

상태: **승인됨 — CL1~CL7 진행 완료.** 계획과 다르게 된 것·값이 실제로 놓인 곳·CL8 후보는 7절.
원칙: 기능을 새로 만들지 않는다. 동작(수치·판정·타이밍)은 그대로 두고 파일 위치·의존 방향·수치의 소속만 바꾼다. push 없음.
기준 커밋: `5b4bcac` (브랜치 `v3`).

---

## 0. 지금 구조 요약 (조사 결과)

- 이동기는 두 벌이다. `BernardTuning.movementModel`(기본 `VirtualOn`)이 고른다.
  - **V8 (지금 실제로 도는 것)**: `Movement/V8/` — `MechMotor`, `VoTickDriver`, `VoLever`, `VoState`(서기·걷기·근접 스텝·대시 정지·급강하), `VoGroundDashState`, `VoJumpState`(점프·공중 대시), `DashAttackRunner`.
  - **Legacy (V6.1, 꺼져 있음)**: `IdleState`, `RunState`, `DashState`, `DashStopState`, `AirState`, `SideStepState` + `MechController`·`MechStateMachine`·`MechCamera` 안의 예전 분기.
  - **두 이동기가 같이 쓰는 상태**: `LandingState`, `LungeState`, `KnockdownState` (V8에서는 `MechMotor.Tick`이 `Update()`→`FixedUpdate()`를 틱마다 한 번씩 대신 부른다).
- 코어가 게임을 직접 안다: `MechMotor`가 `MeleeSystem.IsGuarding`(가드 자동 선회)·`IsStunned`·`ApplyBump`를, `VoState`가 `MeleeSystem.MoveSpeedScale`(콤보 감속)을, `VoGroundDashState`가 `DashAttackRunner`(대시 어택·태클)를 직접 부른다.
- 어셈블리 정의(`.asmdef`)는 없다. 전부 `Assembly-CSharp` 하나.
- **`Assets/Resources/BernardTuning.asset`은 Phase C 이후 저장된 적이 없다.** 들어 있는 블록은 `movement`(예전 필드 이름), `boost`, `heat`, `punch`, `guard`, `parry`, `tackle`, `dashAttack`, `lunge`, `anchorClaw`, `impactSlam`, `lockOn`, `hitStop`뿐이다. `v8`·`movementModel`·`combo`·`startup`·`knockdown`·`hitFeel`·`visual`·`gait`·`motion`은 에셋에 없어서 **지금 V8 수치는 전부 코드 기본값으로 돌고 있다.** (그래서 V8 수치의 클래스·필드를 옮겨도 잃을 에셋 값이 없다.)
- 씬(`SampleScene.unity`)에 스크립트로 붙어 있는 이동 쪽 컴포넌트: `MechController`×2, `MechStateMachine`×2, `MechCamera`×2, `InputReader`×2, `MechTuning`×3, `MeleeDummy`×1. `MechMotor`·`VoTickDriver`·`BotInput`은 씬에 없고 런타임에 붙는다. 씬에 UnityEvent 영구 호출·`SerializeReference`는 0개 → 네임스페이스·폴더를 바꿔도 `.meta`(GUID)만 같이 옮기면 참조가 유지된다.

---

## 1. 삭제 후보와 근거

### 1-A. 레거시 이동 상태 (파일 삭제)

| 파일 | 지워도 되는 근거 | 지우기 전에 옮길 것 |
|---|---|---|
| `Movement/IdleState.cs` | 진입 경로는 `ChangeState(IdleState)`뿐이고, V8이면 `RedirectForV8`이 전부 `VoStand`로 바꾼다. 실제로 Enter되는 일이 없다 | `ChangeState(IdleState)` 호출 9곳(`Health` 2, `LungeState` 3, `KnockdownState` 1, `MechStateMachine` Start·ResetForRound, `LandingState` 1)을 "중립 상태로" 메서드 하나(`ToNeutral()` = 스틱 있으면 걷기, 없으면 서기 — 지금 `RedirectForV8`·`Land`가 하는 일 그대로)로 교체 |
| `Movement/RunState.cs` | `Land`·`LandingState`·`DashStopState`에서만 진입, V8이면 `VoWalk`로 바뀜 | 위와 같음 |
| `Movement/DashState.cs` | `TryStartDash()`에서만 진입하고, `TryStartDash`는 Idle·Run·Air(레거시)만 부른다 | ① `DashExitReason` enum(PlayLogger·V8 상태가 씀) → 별도 파일로. ② `OnDashStarted`/`OnDashEnded` 이벤트(V8 상태가 `StateMachine.DashState.RaiseStarted/RaiseEnded`로 빌려 씀, `PlayLogger`:269·`CombatFeedback`:177이 구독) → `MechStateMachine`으로 이동(PLAN_V8 9절에 적어 놓고 안 한 것) |
| `Movement/DashStopState.cs` | `DashState`·`MechController.CheckDashWall`(레거시)에서만 진입 | `MechStateMachine`의 `IsDashStopping`·`DashEndAllowsAttack`·`IsAirborne`에서 이 타입 검사 제거 |
| `Movement/AirState.cs` | `TryStartJump()`·`CheckFall()`·`OnHitResolved`의 레거시 분기에서만 진입 | ① `OnJumped` 이벤트(`VoJumpState`가 `AirState.RaiseJumped()`로 빌려 씀, `PlayLogger`:257 구독) → `MechStateMachine`으로. ② `NeedsLandingLag`(`RedirectForV8`에서만 읽음) 같이 삭제 |
| `Movement/SideStepState.cs` | Idle·Run이 `InputReader.SideStepDirection`을 볼 때만 진입. V8엔 사이드스텝이 없다(근접 스텝이 대신, QUESTIONS V8) | `PlayLogger`:246 `state is SideStepState` 카운트 제거 — CSV의 `sidestep` 열은 형식 유지로 남기고 항상 0 (`landing_lag_long`과 같은 처리) |
| `Input/StickGestureDetector.cs` + `InputReader`의 `gesture` 필드·`SideStepDirection`·`DetectSideStep()` | `SideStepDirection`을 읽는 곳이 Idle·Run·SideStep 세 레거시 상태뿐 | 실행 단계에서 `StickGestureDetector`의 다른 출력(FlickSide 외)을 읽는 곳이 없는지 한 번 더 grep. 씬의 `InputReader`에 저장된 `gesture` 값은 필드가 없어지면 Unity가 무시한다 |

남기는 상태: `MechState`(바탕), `LandingState`, `LungeState`, `KnockdownState` — V8에서 실제로 돈다.

### 1-B. 이동기 선택 스위치

| 대상 | 근거 |
|---|---|
| `enum MovementModel`, `BernardTuning.movementModel`, `BernardTuning.Model` | 읽는 곳은 `MechMotor.RefreshModel` 한 곳. 에셋에 저장된 값도 없음(코드 기본값 `VirtualOn`) |
| `MechMotor.RefreshModel()`을 매 프레임 `Update`에서 부르는 것, `IsActive` 플래그의 "꺼짐" 경로(`Rb.isKinematic = false`로 되돌림) | 스위치가 없어지면 Start에서 한 번 켜면 끝. 단 **`Input == null || StateMachine == null`이면 안 켜지는 조건은 남긴다**(아래 1-C 연습 봇 참고) |
| `MechController.IsV8`, `MechStateMachine.IsV8`과 그 분기 14곳 | `MechController` 106·243·255·279·313·324, `MechStateMachine` 182·266·329·343·370, `MechCamera` 194, `WeaponSystem` 137 — 전부 V8 쪽만 남긴다 |
| `MechStateMachine.OnMovementModelChanged()` | `RefreshModel`에서만 부름 |
| `VoTickDriver.Unregister`의 "모터가 0개면 원래 `fixedDeltaTime`으로 복원" | 레거시로 되돌릴 때를 위한 것. 씬 종료·에디터 플레이 종료 시 복원은 필요하므로 **삭제가 아니라 주석만 고침** |

### 1-C. V8 전환용 임시 다리 코드

| 대상 | 위치 | 근거·처리 |
|---|---|---|
| `RedirectForV8()` + `ChangeState`의 `else if (newState is VoState) newState = IdleState` | `MechStateMachine` 270·329 | 레거시 상태 요청을 V8 상태로 바꿔 끼우는 번역기. 1-A에서 호출부를 `ToNeutral()`로 바꾸면 필요 없다 |
| `StateMachine.DashState.RaiseStarted/RaiseEnded`, `StateMachine.AirState.RaiseJumped` | `VoGroundDashState` 76·83, `VoJumpState` 66·207·213 | 이벤트를 레거시 상태 객체에 얹어 둔 것. 이벤트를 `MechStateMachine`으로 옮기고 직접 부름 |
| `ActiveDash => _currentState as IDashMove ?? DashState` | `MechStateMachine` 70 | 대시 중이 아닐 때 레거시 `DashState`를 돌려주는 대체값. 삭제하면 대시 중이 아닐 때 `null`이 된다 → **`IsDashing` 확인 없이 쓰는 3곳을 같이 고친다**: `HUDController`:178(차징 바), `CombatFeedback`:533, `MechStateMachine.DashMotionHeading`. 나머지(`MeleeSystem` 330, `WeaponSystem` 161·173, `MechController` 88·310, `MechMotor` 924)는 이미 `IsDashing` 뒤에 있다 |
| `MechStateMachine`의 레거시 틱 경로 | `Update`의 `_currentState.Update()`·`AccumulateTurnInput`, `FixedUpdate` 전체, `_pendingTurn`, `AllowsTurnInput`, `AllowsHardLockFacing`, `TryStartDash`, `TryStartJump`, `CheckFall`, `OnHitResolved`의 리지드바디 분기 | V8이면 전부 `return`으로 건너뛰는 코드. `Update`에는 넉다운 진입 검사 한 줄만 남는다 |
| `MechController`의 레거시 이동 API | `SetMoveVelocity`, `StopHorizontal`, `ApplyRunVelocity`, `ComputeRunVelocity`, `TryGetHardLockForward`, `IsHardLockFacing`, `ApplyHardLockFacing`, `ApplyTurn`, `BodyRelativeDirection`, `UpdateGrounded`(+`_lastGroundedTime`·`TimeSinceGrounded`·`GroundHits`), `ApplyAirGravity`, `OnCollisionEnter`/`OnCollisionStay`/`CheckDashWall`, `MaxRunSpeed`, `DashSpeed`, `forceInterpolation` | 호출하는 곳이 레거시 상태·레거시 카메라뿐(grep 확인). `SetHorizontalVelocity`는 V8 분기만 남김(`LandingState` 등이 쓰는지 실행 때 확인). **남기는 것**: `HoldMoveControl`/`IsMoveHeld`, `ApplyBump`, `RequestDamping`/`SetBaselineDamping`(외부 속도 감속이 `Rb.linearDamping`을 읽으므로 가드·넉다운·런지 요청이 여전히 유효), `ResetForRound`, `IsGrounded` |
| `MechCamera` 레거시 백뷰 | `LateUpdate`의 V3 고정 백뷰 분기, `UpdateFov`, 필드 `followDistance`·`followHeight`·`lookAhead`·`yawFollowSpeed`·`positionFollowSpeed`·`dashPositionFollowSpeed`·`positionFollowBlendRate`·`sprintFov`·`fovLerpSpeed`·`baseFieldOfView` | V8이면 `LateUpdateV8`로 바로 빠진다. `lookHeight`·충돌·흔들림 필드는 V8도 쓰므로 남김. `ResetView`·`Start`의 `fieldOfView = baseFieldOfView`는 V8 FOV 값으로 교체 |
| `MechMotor.SyncFromRigidbody`, `MotionBridge`의 `!rb.isKinematic` 리지드바디 경로 | — | **삭제하지 않는다.** 연습 봇(`MeleeDummy`)은 `MechController`·모터가 없는 다이내믹 리지드바디라 `MotionBridge`가 리지드바디로 보내는 경로가 살아 있다. 주석의 "예전 이동기" 언급만 고침 |

### 1-D. 더 이상 쓰지 않는 튜닝 값

`BernardTuning.MovementValues` (m/s·초 단위, V6 표):

| 값 | 판정 | 근거 |
|---|---|---|
| `diveStartSpeed`, `diveAccel`, `diveMaxSpeed` | **지금도 참조 0** | 코드 어디서도 안 읽음(V8은 `V8Values.diveStartVy/diveAccel/diveAccelUntilVy`). 바로 삭제 가능 |
| `walkStickThreshold`, `hardLockTurnSpeed`, `bodyTurnSpeed`, `sideStepDuration`, `sideStepSpeed`, `dashStartupTime`, `dashSustainTime`, `dashStopDuration`, `dashStopAttackDelay`, `dashWallMaxNormalY`, `dashWallFacingDot`, `jumpTakeoffTime`, `jumpRiseSpeed`, `jumpGravity`, `maxFallSpeed`, `jumpTurnTime`, `airMoveSpeed`, `airAccel`, `airDashSpeed`, `airDashRisingGravityScale`, `groundProbeSkin`, `coyoteTime`, `jumpGroundIgnoreTime` | 1-A·1-C 뒤에 참조 0 | 읽는 곳이 레거시 상태와 `MechController`/`MechStateMachine`의 레거시 분기뿐 |
| `walkSpeed`(m/s), `dashSpeed`(m/s) | **남김 — 결정 필요(질문 Q3)** | V8 이동은 안 쓰지만 `MechVisualRig.Gait`(걸음 모션 기준 속도)와 `MeleeDummy`:427(봇의 대시 도달 시간 예측)이 읽는다. 값이 키 2.6m 환산(앞 22.0/24.1)이라 실제 속도(키 1.93m → 16.3/17.9m/s)와 다르다. 실제 값으로 바꾸면 외형·봇 타이밍이 변하므로 이번엔 값·동작 그대로 게임 레이어 수치로 옮기기만 한다 |
| `knockbackMoveHold`, `mouseTurnSensitivity`, `dashAttackRecovery`, `bumpSpeed`, `landingLag`, `groundMinNormalY` | 남김 | V8 경로가 쓴다 (2절에서 소속만 정함) |

그 밖:

- `BernardTuning.asset` 안의 고아 값: `movement.walkSpeed: 10`(지금은 구조체 필드라 타입이 안 맞음), `runSpeed`, `sprintSpeed`, `maxWalkSpeed`, `maxRunSpeed`, `maxSprintSpeed`, `rotationSpeed`, `turnResistance`, `spinUpTime`, `slideDuration`, `slideSpeed`, `quickTurnDuration`, `brakeDuration`, `brakeSlideWindow`, `boost:` 블록 전체. 대응하는 C# 필드가 이미 없어 Unity가 무시 중. **YAML은 손으로 고치지 않는다** — 에디터에서 에셋을 한 번 저장하면 사라진다(SETUP에 한 줄).
- `HeatValues.sprintCoolingReduction`: 이름만 예전(전력질주)이고 대시 중 냉각 감소로 쓰는 중 → 남김(이름을 바꾸면 에셋 값 0.7을 잃는다).
- `LockOnValues.hardLockEnterRange/ExitRange`: 하드락 몸통 회전은 지우지만 하드락 판정 자체는 `WeaponSystem`:146·`MechVisualRig.Locomotion`:54가 쓰므로 남김.

### 1-E. 문서

- 지우지 않는다(기록). `PLAN_V8.md` 맨 위에 "레거시 이동기·선택 스위치는 PLAN_CLEANUP에서 삭제됨" 한 줄만 추가.
- `CLAUDE.md`의 "폴더와 주요 스크립트" 표는 이미 낡았다(`BoostGauge`, Walk/Sprint/Brake/Slide/QuickTurn 등). 정리 마지막 단계에서 새 폴더 구조로 갱신.

---

## 2. 폴더 구조안

```
Assets/Scripts/
├─ Core/
│  ├─ VirtualOn/                  ← 원본 사양 그대로. 게임 레이어를 모른다
│  │  ├─ VoSpec.cs                (지금 V8Values 중 원본 값 — 기본값 고정)
│  │  ├─ VoUnits.cs               (u·f·각 단위 환산: AngleUnitsToDegrees, U, Dt, SpeedScale)
│  │  ├─ VoTickDriver.cs          (사양서 1.1: 카메라 → 1P → 2P)
│  │  ├─ VoMotor.cs               (지금 MechMotor: 사양서 1.2 한 틱, 2장 선회·자동 선회, 1.4 이동 적분)
│  │  ├─ VoCollision.cs           (MechMotor에서 분리: 사양서 6장 지면·벽·천장 — ProbeGround, CastWall, Depenetrate, CheckCeiling)
│  │  ├─ VoLever.cs               (8방향 코드·오프셋, 대시 방향 잠금)
│  │  ├─ VoCameraRig.cs           (MechCamera.TickV8의 계산: 사양서 5장 — 78u, 8.4°, 12%, 리드)
│  │  ├─ IVoHost.cs               (코어가 바깥에 묻는 것 — 아래 "연결 지점")
│  │  └─ States/
│  │     ├─ VoState.cs, VoStandState.cs, VoWalkState.cs, VoCloseStepState.cs
│  │     ├─ VoGroundDashState.cs, VoDashStopState.cs
│  │     └─ VoJumpState.cs, VoAirDashState.cs, VoDiveState.cs
│  └─ (기존 Core 공용: ErrorLog, GroundLayers, MechIdentity, MechFacing, AppRuntime, HitStop …  그대로)
│
├─ Bridge/                        ← 둘의 연결 지점. 코어와 게임을 둘 다 아는 유일한 곳
│  ├─ MotionBridge.cs             (전투 → 외부 속도 통로. 지금 그대로)
│  ├─ MechStateMachine.cs         (상태 전환·질의 IsDashing/IsAirborne… + 게임 상태 호스팅)
│  ├─ MechController.cs           (넉백 유지·감속 요청·몸 충돌 반동·참조 모음)
│  ├─ VoHostAdapter.cs            (IVoHost 구현: 스턴·콤보 감속·가드 자동 선회·외부 모드·기체 분리/반동)
│  ├─ VoInputAdapter.cs           (MechMotor.ReadInput에서 분리: L스틱 → 레버, R스틱 → 선회, ○/△/✕ 틱 입력)
│  ├─ IDashMove.cs, DashExitReason.cs, DashAttackRunner.cs   (대시 ↔ 전투 접점)
│  ├─ MechCamera.cs               (VoCameraRig 결과 + 우리 흔들림·지형 충돌·화살표)
│  ├─ StyleOverrides.cs           (3절: 원본과 다르게 쓰는 값의 유일한 자리)
│  └─ ExternalControlLock.cs      (지금 Scripts/ 바로 아래 ExternalControllock.cs)
│
└─ Game/                          ← 우리 게임. 코어는 Bridge를 통해서만 건드린다
   ├─ Combat/                     (MeleeSystem[펀치·가드·패리·스턴], WeaponSystem, LockOnSystem, Health, HitTypes,
   │  │                            AttackInputBuffer, Projectile, ThrusterLunge)
   │  ├─ Skills/                  (AnchorClaw, ImpactSlam)
   │  ├─ Knockdown/               (KnockdownController, KnockdownState)
   │  └─ States/                  (LungeState, LandingState — 모터가 "외부 상태"로 틱만 돌려 주는 게임 상태)
   ├─ Heat/                       (HeatGauge)
   ├─ Tuning/                     (BernardTuning[전투·외형 수치만], MechTuning)
   ├─ Bot/                        (MeleeDummy, BotInput)
   ├─ Input/                      (InputReader, ICombatInputSource, VersusInputPairing, InputTrace)
   ├─ UI/                         (HUDController, ArcGauge, LinearGauge, RoundUI, MenuUI, OffscreenTargetArrow)
   ├─ Feedback/, Visual/          (그대로 이동)
   └─ Flow/                       (GameManager, PlayLogger, PracticeHotkeys, SplitScreenLetterbox, FrameRateTester)
```

네임스페이스는 CLAUDE.md 규칙(`MyDongari.<폴더>`)대로: `MyDongari.Core.VirtualOn`, `MyDongari.Bridge`, `MyDongari.Game.Combat` …

### 연결 지점을 어디에 두는가

**`Bridge/` 폴더 하나에 모은다.** 의존 방향 규칙:

```
Game ──→ Bridge ──→ Core/VirtualOn
Game ──────────────→ Core (공용 유틸만)
Core/VirtualOn ──✕──→ Bridge, Game     (using MyDongari.Bridge / MyDongari.Game.* 금지)
```

- **전투 → 이동** (넉백·런지·클로·넉다운·리스폰): `MotionBridge` (지금 그대로).
- **이동 → 전투** (코어가 게임에 묻는 것): `IVoHost` 인터페이스. 코어는 인터페이스만 알고, 구현(`VoHostAdapter`)은 Bridge에 둔다. 지금 `MechMotor`·`VoState`가 직접 부르는 것을 그대로 옮긴다:

| 지금 코어가 직접 부르는 것 | IVoHost 멤버(안) | 원본에서의 자리 |
|---|---|---|
| `MeleeSystem.IsStunned` (`VoState.IsStunned`, 카메라 리드 복귀) | `bool IsStunned` | 사양서의 "경직"·"금지 플래그" |
| `MeleeSystem.MoveSpeedScale` (`VoState.SetLeverMove`) | `float MoveSpeedScale` | 없음(우리 콤보 감속) — 기본 1 |
| `MeleeSystem.IsGuarding` → 가드 자동 선회 (`MechMotor.UpdateAutoTurn` Default 분기) | `bool TryGetAutoTurnOverride(out float bearing, out float rateUnits)` | 없음(우리 규칙) — 기본 false |
| `Controller.IsMoveHeld`·`IsExternallyControlled`·`LungeState`/`KnockdownState` 검사 | `bool IsExternalMode` | 사양서 1.4-1 "넉백 보간 중이면 그것만" |
| `SeparateFromOpponent`의 최소 분리·`ApplyBump`·자동 태클 | `void OnBodyOverlap(Vector3 apart)` + 분리 자체는 `VoHostAdapter` | 없음(6.3 미확인) |
| `VoGroundDashState` 안의 `DashAttackRunner` | `IVoDashAttack` (Bridge의 `DashAttackRunner`가 구현, 상태에 주입) | 사양서 4.4 "지속 + 무기 → 대시 공격" 자리 |
| `StateMachine.Land(lag)` → `LandingState` | `void OnLanded(bool needsLag)` | 사양서 4.6 착지 12f |
| `MechIdentity.Opponent`, `InputReader`, `MechCamera` | `IVoHost`가 상대 위치·틱 입력·카메라 리그를 제공 | — |

- **`.asmdef`는 이번에 만들지 않는다.** 에디터 없이 어셈블리를 쪼개면 `Assets/Editor`·Input System·URP 참조 누락을 컴파일로 확인할 수 없다. 대신 "코어는 Bridge/Game을 `using`하지 않는다"를 grep 검사(4절)로 지키고, asmdef는 에디터 확인이 되는 뒤 태스크로 남긴다.

### 옮길 때 지키는 것

- `.cs`와 `.cs.meta`를 **항상 같이 `git mv`** (GUID 유지 → 씬 참조 유지).
- 씬에 붙어 있는 MonoBehaviour(`MechController`, `MechStateMachine`, `MechCamera`, `InputReader`, `MechTuning`, `MeleeDummy`, `HeatGauge`, `HUDController` 등)는 **클래스 이름·파일 이름을 바꾸지 않는다**(네임스페이스·폴더만). 이름을 바꾸는 것은 씬에 없는 `MechMotor → VoMotor`뿐.
- `BernardTuning.asset`에 들어 있는 블록(`heat`~`hitStop`)의 **필드 이름은 바꾸지 않는다**.

---

## 3. 수치 규칙: 원본은 코어에 고정, 우리 값은 StyleOverrides에서만

### 규칙

1. **`Core/VirtualOn/VoSpec`의 기본값 = `docs/REFERENCE_MOVEMENT.md` 원본값.** 원본 단위(u, u/f, f, 각 단위) 그대로, 필드마다 사양서 절 번호를 주석으로 단다. 인스펙터에 노출하지 않는다(에셋으로 직렬화하지 않는 순수 값 객체) — "고칠 수 없는 기준값".
2. **원본과 다르게 쓰는 값은 `Bridge/StyleOverrides`(ScriptableObject, `Assets/Resources/StyleOverrides.asset`)에만 적는다.** 항목마다 `덮어쓰기 켬/끔 + 값`. 끄면 원본으로 돌아간다. 실행 때 `유효값 = VoSpec 원본 ← StyleOverrides에서 켜진 항목만 덮어씀`.
3. **원본에 대응이 없는 우리 규칙의 수치**(가드 자동 선회, 대시 어택 경직, 몸 충돌 반동 등)는 코어에 넣지 않는다 → 게임 레이어(`BernardTuning`) 또는 `StyleOverrides`의 "추가 규칙" 묶음.
4. 코어 코드에 `V9e`·`V8.3` 같은 우리 변형을 `if`로 박지 않는다. 변형이 필요하면 **기본값이 원본 동작인 스위치**를 코어에 두고 `StyleOverrides`가 켠다.
5. 에셋은 YAML로 만들지 않는다: `StyleOverrides`의 **코드 기본값 = 지금 우리 값**, 에셋이 없으면 `BernardTuning.Fallback`과 같은 방식(`Resources.Load` → 없으면 코드 기본값 인스턴스)으로 돈다. 에디터 메뉴 `Tools/Create Style Overrides Asset` + SETUP.md 한 단계. → **정리 직후 동작은 지금과 같다.**

### 지금 원본과 다른 것 전체 목록

표기: **코어** = `VoSpec` 원본 기본값 / **Style** = `StyleOverrides`에서 덮어씀 / **브리지** = 입력·단위·충돌처럼 원본을 우리 환경에 붙이는 어댑터 / **게임** = 원본에 없는 우리 규칙.

#### (가) 수치를 원본과 다르게 넣은 것

| # | 항목 (지금 위치) | 원본 | 지금 값 | 도입 | 둘 곳 |
|---|---|---|---|---|---|
| 1 | 가드 자동 선회 `guardTurnInside`/`guardTurnOutside` | 없음(가드 자체가 없음) | 정면 안 400(132°/s) / 밖 1092(360°/s), 33.75°~60° 선형, 가드 중 수동 선회 무시 | V9a·V9e | **게임** (`BernardTuning.Guard` 쪽 또는 Style "추가 규칙") + `IVoHost.TryGetAutoTurnOverride` |
| 2 | 빠른 선회 방향 제한 (`ManualTurnRate`의 `toward` 검사, **코드에 박혀 있음**) | 방향을 안 가림 — 상대가 정면 ±33.75° 밖이면 어느 쪽으로 돌든 2304 | 상대 쪽으로 도는 입력일 때만 2304, 반대쪽은 '정면 안' 값 | V9e | 코어에 스위치 `fastTurnOnlyTowardOpponent`(기본 **false** = 원본) → **Style**에서 true |
| 3 | 선회율 보간 구간 `turnBlendEndAngle` | 33.75° 경계에서 바로 바뀜 | 33.75°→60° 선형 | V9e | 코어 기본 = 33.75(보간 없음) → **Style** 60 |
| 4 | 카메라 위치 추종 `cameraPositionFollow` | 월드 위치 지연 없음(눈·표적 = 기체 위치 + 국부 오프셋, 5.1) → 1.0 | 0.35/틱 | V8.3 | 코어 기본 1.0 → **Style** 0.35 |
| 5 | 카메라 가로 상한 `cameraMaxScreenOffset` | 없음(4번이 1.0이면 필요 없음) | 화면 폭의 25% | V8.3 | 코어 기본 = 끔 → **Style** 0.25 |
| 6 | 사격 조준 `attackAimRate`/`attackAimFrames` | 무기별 592, 336, 2560, 848, 80 … (12종), 15f | 공격 하나의 값 2560, 15f, 최대율 틱 ×0.1 | V8 | 15f·×0.1은 **코어**. 선회율은 무기(=우리 공격)의 값이라 **게임** (`BernardTuning`의 공격 수치) |
| 7 | 근접 스텝 속력 대각·뒤 `closeStepStartSpeed/EndSpeed` | 앞 1.5→2.9, 옆 4.2→2.2만 있음 | 대각 2.85→2.55, 뒤 1.5→2.9 (채운 값) | V8 | 앞·옆은 **코어**. 채운 값은 **Style**로 빼고 코어엔 "원본 미상" 표시 — **결정 필요(Q1)** |
| 8 | 기체 키·환산 `fallbackBodyHeight` | 22u = 2.6m(추정) | 렌더러로 잰 1.93m → 1u = 0.0877m (속도·점프·카메라 거리 전부 0.74배) | V7b·V9b | u 값은 원본 그대로(**코어**). 미터 환산은 **브리지**(모델 키를 재서 코어에 넘김) |
| 9 | 충돌 캡슐 | 반경 5u(0.59m)·높이 15u(1.77m) (추정) | 씬의 캡슐 콜라이더 반경 0.5·높이 2m | V8 | **브리지**(씬 콜라이더를 코어에 넘김). 코어 기본은 5u/15u로 적어 둠 |
| 10 | 카메라 FOV `cameraFov` 35° | **사양서에 없음** (PLAN_V8에만 있음) | 35° | V8 | **Style** — 원본 근거를 못 찾음. **결정 필요(Q2)** |
| 11 | 카메라 기준 높이 `cameraBaseHeightRatio` 0.5 | 불명(5.2 "낮음") | 발 + 키 × 0.5 | V8 | **Style**(원본 미상 표시) |
| 12 | 대시 정지 중 공격 허용 `dashEndAttackFrame`을 대시 정지에도 적용 | "종료 3f째부터 무기"는 대시 종료 단계 얘기. 대시 정지는 언급 없음 | 대시 정지도 3f째부터 | V6.1 | 값 3은 **코어**. 대시 정지에 적용하는 것은 **게임**(공격 가능 판정은 `MechStateMachine.DashEndAllowsAttack`) |
| 13 | 정렬 허용 오차 `alignEpsilon` 0.5° | "정렬되면" | 0.5° | V8 | **코어**(구현 허용 오차, 원본 수치 아님 표시) |
| 14 | `stickThreshold` 0.3, `turnDeadzone` 0.05(R스틱에 적용), `mouseTurnSensitivity` | 레버 누적값 임계 64/48, 차동 0.05 | 스틱 기울기 0.3 | V8.1 | **브리지**(`VoInputAdapter`) |
| 15 | `closeStepFromDashFrames` 15 | 없음 | 근접 구역 중립 ○ → 앞 대시 → 15f 안 스틱이면 근접 스텝 | V8.2 | **Style**(입력 편의 규칙) + 분기는 `VoInputAdapter`/스위치 |

#### (나) 동작(로직)을 원본과 다르게 한 것

| # | 항목 | 원본 | 지금 | 둘 곳 |
|---|---|---|---|---|
| 16 | 조작 체계 | 트윈 레버 2개 + 터보 2개 | L스틱 = 두 레버 같은 방향, R스틱 좌우 = 레버 차동, ○ = 터보 하나, △ = 레버 벌리기, ✕(공중) = 레버 모으기 | **브리지** `VoInputAdapter`. 코어는 "레버 이동 벡터·선회 계수·터보/점프/급강하 틱 입력"만 받는다 |
| 17 | 레버 누적값 모델(2.1) | 방향별 누적 0~1536, 걷기 속력 × 0.8~1.2 | 삭제 — 스틱 0.3 이상이면 즉시 곡선 최대값 × 1.0 | V8.1 결정. **브리지**(레버 크기 1.0 고정을 어댑터가 공급). 코어 주석에 "원본은 0.8~1.2" |
| 18 | 스틱 중립 ○ | 방향 잠금 없음 → 대시 안 나감 | 앞 대시 | V8.1. **브리지**(중립이면 방향 0을 공급) |
| 19 | 급강하 입력 | 점프 **상승 중** 레버 모으기 | 공중(상승·하강 모두)에서 ✕ | V9a. 입력은 **브리지**, "하강 중에도 허용"은 코어 스위치(기본 원본) → **Style** |
| 20 | 상승 중 재점프(vy 3.25 한 번) | 있음 | 삭제 | V9a. 코어 스위치(기본 **켬** = 원본) → **Style**에서 끔 |
| 21 | 하강 중 레버 모으기 가속(15f 안 vy −= (n+1)×0.2) | 있음 | 없음(✕는 바로 급강하) | **미구현 목록**으로 코어 주석·문서에만. 이번에 만들지 않는다 |
| 22 | 착지 | 12f 착지 모션 → 서기, 모든 착지 | `LandingState` 0.2초(점프·공중 대시·공중 피격만), 발판에서 그냥 떨어지면 경직 없음 | 12f는 **코어** 값. "낙하 착지는 경직 없음"·이동/공격/가드 불가는 **게임**(`LandingState`) |
| 23 | 공중 피격 | 피격 상태(미분석) | 수평 이동 끊고 낙하 + 착지 경직 | V5. **게임** (`MechStateMachine.OnHitResolved`) |
| 24 | 기체끼리 | 밀어내기 못 찾음 | 캡슐 반경 합만큼 분리 + 대시 몸 충돌 반동(`bumpSpeed` 4m/s) + 차징 자동 태클 | V3·V8. **게임**(`VoHostAdapter`), 수치는 `BernardTuning` |
| 25 | 외부 속도 모드(넉백·런지·클로·넉다운) | 1.4-1 "넉백 보간" | 외부 속도 + 중력 + `linearDamping` 흉내 + 벽·지면 | 적분기는 **코어**(원본 자리 있음), 무엇이 외부 모드인지는 **브리지** |
| 26 | 콤보 이동 감속(×0.6 / ×0.4) | 없음 | 걷기·점프 수평에 곱함 | **게임** → `IVoHost.MoveSpeedScale` |
| 27 | 스턴 | 경직(무기별) | 스턴이면 상태 전환·선회 정지, 대시 종료 | **게임** → `IVoHost.IsStunned` |
| 28 | 대시 중 공격 | 무기별 대시 공격, 방향별 경직 8~18f | 유도 대시 어택·차징 태클, 경직 0.2~0.3초(`dashAttackRecovery`) | **게임**(`DashAttackRunner`, 수치 `BernardTuning`). 코어엔 "지속 + 공격 → 지속 조준" 자리만 |
| 29 | 대시 중 방향 재입력, 미확인 대시 파생 상태 | 있음 | 없음 | 미구현 목록 |
| 30 | 카메라 모드 | 원거리 기본 / 근접(66u, 8%) / 서기·대시 특수(60u, 2%) / 낙하 | 원거리 기본 하나 | 미구현 목록. 코어 `VoCameraRig`에 값만 주석 |
| 31 | 카메라 흔들림·지형 충돌(T4)·화면 밖 화살표 | 연출값 36/48/64(추정) | 우리 흔들림·구 캐스트 충돌 | **게임/브리지**(`MechCamera`) |
| 32 | 표적 선회·공격 회전 모드, 무기 점프(vy 3.80), 앉아 사격 | 있음 | 없음 | 미구현 목록 |
| 33 | 벽에 막힌 대시 | 안 끝남 | 안 끝남 (`EndByWall` 빈 구현) | 원본과 같음. `IDashMove.EndByWall`은 레거시용이라 **삭제** |
| 34 | 마우스 선회(키보드·마우스 예비 조작) | 없음 | 이동량 × 감도를 선회에 더함 | **브리지** |

#### (다) 전투 재구성(2026-11-15, 근거 `docs/REFERENCE_COMBAT.md`) 뒤 바뀐 것

위 (가)·(나) 표에서 **없어진 편차**(원본대로 됨):

| 위 번호 | 항목 | 지금 |
|---|---|---|
| 7 | 근접 스텝 대각·뒤 속력 | 원본 5점 확정(시작 1.5/4.2/4.2/2.5/1.2, 끝 2.9/2.4/2.2/1.95/1.8) → `VoSpec`. `StyleOverrides.closeStepStartFill/EndFill` 삭제 |
| 8 | 기체 키 환산 | 개정 사양서 기준이 22u = 1.93m → `VoSpec.fallbackBodyHeight` 1.93, 덮어쓰기 삭제 |
| 19 | 하강 중 ✕ = 급강하 | 삭제. 하강 중 ✕(누르고 있기)는 원본의 하강 가속, 급강하는 상승 중에만. 스위치 `diveAllowedWhileFalling`도 삭제 |
| 21 | 하강 가속 미구현 | 구현 (`VoJumpState`, `VoSpec.fallAccelStep/fallAccelLastFrame`) |
| 29 | 대시 중 방향 재입력 미구현 | 구현 (`VoGroundDashState` 지속 단계). 미확인 대시 파생 상태는 여전히 없음 |
| 32·C6 | 지연 표적 없음(근접 추적 표적 = 상대의 지금 위치) | 구현 (`VoDelayedTarget`, `VoMotor.DelayedTargetBearing`). 상대의 같은 행동(상태) 15f 이상이면 반씩 따라감. 스위치 `VoSpec.delayedTargetEnabled`(원본 = 켬) |

**전투에서 원본과 다르게 한 것** (원본 = `docs/REFERENCE_COMBAT.md`):

| # | 항목 | 원본 | 지금 | 둘 곳 |
|---|---|---|---|---|
| C1 | 근접 판정 순서(공정성) | 1P 근접 판정 → 2P 갱신(같은 틱 정산) → 2P 근접 판정 → 1P는 다음 틱 정산. 같은 틱 맞교환은 1P가 이김 | 두 기체가 모두 움직인 뒤 판정을 모으고(`IVoTickUnit.CollectHits`), 다음 틱 맨 앞에 두 기체가 같은 시점 상태로 판단(`DecideHits`), 적용은 각자 틱의 정산 자리. 1P·2P 모두 1틱 뒤 반응 | 순서는 **코어** `VoTickDriver`, 판단은 **게임** `CombatLedger`·`MeleeSystem` |
| C2 | 같은 틱 맞교환 | 상쇄 없음(반응 없는 약한 타격끼리만 상호 타격) | 펀치끼리·대시 공격끼리 같은 틱에 서로 맞으면 맞딜: 둘 다 피해 0 + 밀려남(`Punch.clashKnockback`) | **게임** `CombatLedger.Decide` |
| C3 | 기체끼리 밀어내기 | 없음 (정지 거리·넘어가기 방지뿐) | 유지: 캡슐 반경 합만큼 분리 + 대시 몸 충돌 반동 (위 24번) | **게임** `VoHostAdapter.AfterIntegrate` |
| C4 | 전투 시간 | 60f/s 논리 프레임 | 전투 전체가 `VoTickDriver`의 60Hz 틱에서 돌고 모든 시간(쿨다운·경직·입력 창)을 틱 수로 센다. 우리 값(런지 쿨다운 180f 등)도 `BernardTuning`의 `…Frames` | 원본과 같음 (기록용) |
| C5 | 근접 상태 | 서기 상태 안의 무기 하위 상태 | 별도 상태 `VoMeleeState`(동작은 같다 — 걷기·수동 선회 없음, 점프·대시 취소 구간 동일) | **코어** |
| C7 | 투(A1 2타)의 단계 길이·판정 창 | 미확인(같은 모션의 다른 구간) | 원(A1 1타)과 같게 16/10/22f, 판정 3~8f. 기준 속력 × 0.75는 원본 | `VoCombatSpec.a1Second` ([미상] 표시) |
| C8 | 콤보 구성 | A1은 최대 2타, C1은 두 트리거 동시의 단독 근접 | 원(A1 1타) → 투(A1 2타) → 쓰리(C1)를 공격 버튼 하나로 잇는다. 쓰리도 ② 단계 예약으로 ③을 건너뛰고 나간다 | **게임** `MeleeSystem` |
| C9 | 과열 | 없음 | 과열이면 펀치 시작·예약 불가 (우리 히트 게이지) | **게임** |
| C10 | 버튼 → 근접 변환 창 | 단독 확정 2f 대기 + 확정 후 1~4f 구역 확인 | 버튼이 하나라 확정 대기 없음. 공격 입력 버퍼(4f, I에서 정리)가 변환 창 역할 | **게임** `AttackInputBuffer` |
| C11 | 명중 판정 형상 | 공격: 칼끝이 한 틱 동안 쓸고 간 면(관절 37→35, 중심축→23). 피격: 관절에 붙은 구 7개(반지름 2.2~2.5u) | 공격 구(기체 중심 + 정면 × 사거리, 손 높이)를 지난 틱 → 이번 틱 위치로 쓸어 상대 캡슐 콜라이더와 겹침. 원·투 1.8m/0.5m, 쓰리 2.0m/0.6m, 뛰어 베기 2.0m/0.7m — 골격 길이 미확정이라 우리 근사 | `StyleOverrides`의 "명중 판정" 묶음, 계산 `Game/Combat/HitGeometry` |
| C12 | 점프 중 피격 판정 | 상승 3개 구, 하강 2개 구 | 캡슐 높이·반지름 × 0.6(상승) / × 0.45(하강) | `StyleOverrides.jumpRisingHurtScale/jumpFallingHurtScale` |
| C13 | 한 휘두르기의 재타격 | 맞으면 그 틱에 판정이 꺼졌다가 창이 남으면 다시 켜짐(피격측 근접 무적으로만 막힘 → 약한 피격은 다단 히트 가능) | 한 번 휘두를 때 한 상대에게 한 번만 | **게임** `MeleeSystem` |
| C14 | 원·투·쓰리 충격치 | A1 1타 500(경직), A1 2타 1100(다운), C1 1100 | 원·투 280 — 경직만, 연달아 맞아도 다운 문턱(600) 아래. 쓰리 620 — 단독으로 다운이고 원·투 누적(최대 560) 위에서도 날아감 문턱(1200) 미만이라 원투쓰리가 다운으로 끝난다(원본 1100이면 날아감) | `StyleOverrides.punch1Impact/punch2Impact/punch3Impact`(끄면 원본) |
| C15 | 우리 기술의 판정 종류·충격치 (원본에 대응 없음) | — | 런지 1100 강타 / 대시 공격 1100 근접 계열 2 / 차징 태클 1100 강타 / 파일 드라이버 1100 강타 + 가드·패리 무시 / 슬램 1100 일반 근접 / 뛰어 베기 1100 근접 계열 3 / 앵커 클로는 잡기라 없음. 피해량은 `BernardTuning` 그대로 | 값은 `VoCombatSpec`의 원본 칸을 빌려 쓰고 슬램만 `StyleOverrides.slamImpact` |
| C16 | 파일 드라이버 궤적 | 날아감: vy 1.4, 수평 2.401u/f | 우리 10m 넉백 궤적(8m 포물선 비행 + 2m 미끄러짐). 누움·기상은 원본 규칙 | `KnockdownController`(DownKind.PileDriver), 수치 `BernardTuning.Knockdown` |
| C17 | 가드된 근접의 반응 | 판정 순서 표로는 누적 10%라 대개 경직 문턱(200) 미만 = 반응 없음, 1.5절은 "막으면 가드 경직" | 막으면 항상 가드 경직(1.5절을 따름). 강타·판정값 ≥ 1200이면 깨져서 날아감 | `HitRules.Decide` |
| C18 | 행동 가산치, 사격 무적, 앉은 자세 문턱, 다운 중 추가 피격(다운 추격) | 있음 | 없음 — 근접은 가산치 0이고 사격·앉기·다운 추격 공격이 우리 게임에 없다 | 미구현 목록 |
| C19 | 다운 중 무적 | 무적 타이머를 매 틱 3f(대기 중 3~6f 난수)로 다시 채움, 일부 특수 공격은 통과 | 다운 중(대기·띄움·누움·기상 대기·기상 모션) 완전 무적 | `KnockdownController.IsInvulnerable` |
| C20 | 기상 대기의 특수 입력(vy ±3.25 회피 추정), 기상 모션 중 선입력 | 있음 | 없음. 대시 선입력은 "끝나는 순간 ○ + 방향" | 미구현 목록 |
| C21 | 피격 밀림·다운의 이동 방식 | 상태별 속도 벡터로 이동 적분 | 게임이 틱마다 u/f 속도를 정해 외부 속도 통로(`MotionBridge`, 감속 없음)로 넣는다 — 이동기 없는 연습 봇도 같은 규칙을 쓰려고 | **게임** `MeleeSystem.TickPush`·`KnockdownController` |
| C22 | 런지·대시 공격의 겹침 방지 | 대시 근접: 15u에서 정지, 이동량 ≤ 상대 거리 | 런지(우리 기술)와 대시 어택에 같은 15u 정지·넘어가기 방지를 붙임. 이동은 예전 그대로 | `ThrusterLunge.LimitedDashSpeed`, `DashAttackRunner` |
| C23 | 뛰어 베기(점프 근접)의 미확인 값 | 확정: 입력 8f, vy 3.80, 추적 2560. 기준 속력·단계 길이·판정 창·이 상태의 중력은 미확인 | 기준 4.0u/f, ① 12 / ② 10f, 판정 2~7f, 중력 0.451·낙하 상한 4.001(다운 상태 값을 빌림), 정지 거리 15u, 이후 일반 낙하·착지 12f, 피해 30(에셋의 펀치 10 대비 큼 — 확인 필요) | `StyleOverrides`의 "뛰어 베기" 묶음([미상] 표시), 피해는 `BernardTuning.Combo.jumpSlashDamage` |
| C24 | 뛰어 베기 발동 조건 | 점프 이륙 2f 안 + 점프 입력 후 8f 이내 | 점프 입력 후 8f 이내(이륙·상승 중 어디서든) + 근접 구역(높이차 창 무시) | **게임** `MeleeSystem.InJumpSlashWindow` |
| C25 | 가드 입력·조건 | 레버 모으기 4~24f, 상대가 근접 구역 안 + 방위 ±112.5° 안, 시작 4f → 유지 32f → 해제 8f | ✕ 버튼(땅) 누르는 즉시, 구역 조건 없음, 재사용 대기 24f. 막는 방향(±112.5°)·피해 6%·충격치 10%·가드 경직 16f/1.4u/f·근접 무적 16f·≥1200 깨짐은 원본 | **게임** `MeleeSystem` |
| C26 | 패리, 가드 히트, 가드 반격 | 패리·히트 없음. 가드 반격(가드 유지 중 근접) 있음 | 패리(새로 누른 9f 창, 런지·태클·대시 어택은 반사)와 막을 때 히트 증가는 유지. 가드 반격은 없음. 과열 가드 깨짐은 삭제 | **게임** |
| C27 | 히트스톱 중 모션 | 15f 중 2칸만 진행(남은 8·0일 때) | 완전 정지 (공격 단계·경직 시간·외형 시계·애니메이터 속도 0) | `VoMotor`(틱을 건너뜀)·`MechVisualRig` |
| C28 | 맞딜의 히트스톱 | (맞딜 자체가 없음) | 맞딜은 히트스톱 없이 밀려나기만 | **게임** |
| C29 | 착지 12f의 무기 입력 | 무기만 즉시 실행(착지 취소) | 지상 공격(구역 안 펀치 / 밖 런지)만 즉시. 스킬(앵커 클로·슬램)은 착지가 끝난 뒤 | **게임** `WeaponSystem` (위 22번 `LandingState`는 12f 틱으로) |
| C30 | 대시 선입력의 터보 15f 창 | 서기·걷기에서 터보 15f 이내 + 유지 + 방향 | 코어 구현과 같음(대조함). 근접 구역에서 "터보 먼저 → 레버"로 잠기면 대시 대신 근접 스텝인 것도 원본 규칙 그대로 | 원본과 같음 (기록용) |
| C31 | 피해량 | 템진 근접 피해(A1 1타 290 / 2타 130 / C1 380 / 점프 근접 A 340 / 대시 근접 A 275 / A2 435 / C2 460), 원본 체력 스케일 | 기술 사이 비율은 원본 그대로(`VoCombatSpec.damage…`), 크기만 배율 하나로 줄임: `damageScale` = 10 ÷ 290(체력 100에서 원 = 10). 원 10 / 투 4.5 / 쓰리 13.1 / 뛰어 베기 11.7 / 대시 공격 9.5 / 런지 15.0 / 차징 태클 15.9(차징이 길수록 최대 1.5배 = 23.8 — 우리 규칙, `BernardTuning.Tackle.maxChargeDamageScale`). 슬램 10·파일 드라이버 25는 원본에 대응이 없는 우리 값(`BernardTuning`) | `StyleOverrides.damageScale`(끄면 원본 스케일 1) |

위 (가)·(나)에서 **없어진 우리 규칙**: 공격 입력 버퍼 0.15초(→ 원본 근접 변환 창 4f), 전역 timeScale 히트스톱(`HitStop.cs` — 세기 등급별 길이 삭제, 기체별 15f로 대체), 23번(공중 피격 = 낙하 + 착지 경직 — 원본대로 점프 중 경직급은 반응 없이 무적 16f, 다운급은 공중 다운), 27번의 초 단위 스턴(런지 0.5초·태클 1.7초·가드 깨짐 0.9초 — 충격치 반응으로 대체. 공격자 경직·훅 경직만 틱으로 남음), 6번(공격 하나의 사격 조준 선회율 2560 — 근접 변형별 추적 회전율 848/848/512로 대체, `StyleOverrides.attackAimRate` 삭제), 26번(콤보 이동 감속 — 근접 중에는 걷기 자체가 없다, `IVoHost.MoveSpeedScale` 삭제).

원본과 같은 것(코어에 그대로): 틱 60Hz·1P→2P 비대칭·카메라 선갱신, 속도 곡선 4종, 근접 감속 200u×0.78, 근접 구역 70u, 레버 거리 보정 70~100u×0.7, 선회율 표(2304/400/512/549/384/320/64/384/104), 점프 조준 2304·공중 대시 복귀 2560·카메라 방위 추종 4096, 대시 게이지(−200/+206/500/<24), 대시 16/12/16f·÷17·잠금 15f, 대시 중 공중 0.081/−1.801, 근접 스텝 6+22f, 점프(2f, 3.25, 0.074, 0.001, −0.526, 2중 적분, 호버 7~14f), 공중 대시(중력 ×0.5, 끝 −0.001), 급강하(−1.75, 0.25, −2.751, 2중), 단차 5u·낙하 −0.5, 카메라 78u·1536·+6u·12%·리드 1536/256.

---

## 4. 위험과 확인 방법

| 위험 | 어디서 | 막는 방법 / 확인 |
|---|---|---|
| **씬 스크립트 참조가 끊김** (Missing Script) | 파일 이동 때 `.meta`를 빼먹거나 클래스 이름이 파일과 달라짐 | `.cs`+`.meta` 같이 `git mv`. 단계마다 `git status`에 새 `.meta`(추가)·지워진 `.meta`(삭제)가 **옮긴 것 외에 없는지** 확인. 씬에서 쓰는 GUID 목록을 정리 전후로 grep해 전부 `Assets/**/*.meta`에 있는지 대조하는 스크립트를 스크래치에 두고 매 단계 실행 |
| **직렬화 값 손실** | 씬에 저장된 `MechCamera`·`MechController`·`InputReader` 필드, `BernardTuning.asset` 블록 | 남기는 필드는 이름을 안 바꾼다. 지우는 필드(`followDistance` 등)는 값이 버려져도 되는 것만. `BernardTuning`에서 V8 수치를 빼도 에셋에 `v8` 블록이 없어 손실 없음(0절). 정리 전후 `git diff --stat -- '*.unity' '*.asset' '*.prefab'`이 **비어 있어야** 한다 |
| **프리팹** | `Assets/Prefabs/Projectile.prefab` 하나뿐 (`Projectile` 스크립트) | `Projectile.cs`는 폴더·네임스페이스만 바뀜. GUID 대조에 프리팹도 포함 |
| **연습 봇** | `MeleeDummy`는 `MechController`·모터·상태 머신이 없는 다이내믹 리지드바디. `MotionBridge`의 리지드바디 경로, `ThrusterLunge`의 "상태 머신 없음" 경로, `MechVisualRig`의 "상태 머신 없으면 속도로 판단" 경로에 기댄다 | 이 세 경로는 "레거시"처럼 보여도 **지우지 않는다**(1-C에 명시). `IsV8` 분기를 지울 때 `Motor == null`인 기체가 지나가는 코드인지 줄마다 확인. 체크리스트: PRACTICE에서 봇 4모드(가만히/Parrier/Lunger…) 이동·넉백·런지·넉다운·클로 끌려오기 |
| **`ActiveDash`가 null** | `HUDController`:178, `CombatFeedback`:533, `DashMotionHeading` | 1-C대로 3곳 null 처리. 체크리스트: 대시 안 할 때 HUD 차징 바 꺼짐, 근접 스텝 전환 때 외형 진로 |
| **이벤트 구독 끊김** | `PlayLogger`(대시 시작/끝·점프·착지), `CombatFeedback`(대시 분사·착지 먼지) | 이벤트를 `MechStateMachine`으로 옮기면서 구독 4곳을 같이 고침. 체크리스트: 대시 출발 섬광·발밑 불꽃, 착지 먼지·흔들림, CSV의 `dash_*`·`jump`·`landing` 카운트가 0이 아닌지 |
| **중립 상태 복귀가 달라짐** | `ChangeState(IdleState)` 9곳 → `ToNeutral()` | 지금 `RedirectForV8`은 Idle → **항상 서기**, Run → 걷기. `ToNeutral()`도 호출부마다 같은 결과가 나오게(런지·넉다운·사망 복귀 = 서기, 착지 = 스틱에 따라) 그대로 옮긴다 |
| **틱 순서·실행 순서 변화** | `VoTickDriver`(`DefaultExecutionOrder(1000)`), `AnchorClaw`(100), `MeleeDummy`(−50) | 속성은 클래스에 붙어 있어 이동해도 유지. 모터 틱 안의 호출 순서(입력 → 보조 → 상대 관계 → 게이지 → 자동 선회 → 카메라 방위 → 메인 → 적분 → 커밋)는 **한 줄도 바꾸지 않고** 호스트 호출로 치환만 한다. `MechMotor.Tick`은 분리 전후를 나란히 놓고 diff |
| **인터페이스 치환으로 미세 동작 변화** | 가드 자동 선회의 `_guardTurning`(수동 선회 차단), 사격 조준 포화 감속, `LeaveExternal`의 vy 넘김 | 치환 단계는 커밋을 따로 끊고, 치환 전후 같은 입력에서 값이 같아야 하는 지점(선회율·속력 배율)을 TEST_CHECKLIST에 수치로 적음 |
| **StyleOverrides 적용 누락** → 원본값으로 돌아가 버림 | 3절의 Style 항목 | 코드 기본값 = 지금 값으로 두고, 스모크 테스트 로그에 "유효값 ≠ 원본"인 항목 목록을 한 번 찍는다(`debugLog`/스모크 테스트일 때만). 기대 목록(가)-2·3·4·5·7·10·11·15, (나)-19·20과 대조 |
| **컴파일 에러** (에디터를 못 띄우면 확인 불가) | 네임스페이스 이동으로 `using` 누락, `Assets/Editor/*` 9개 스크립트가 런타임 타입을 참조 | 단계마다 ① 옮긴 타입 이름으로 전체 grep → 파일마다 `using` 확인, ② `Assets/Editor`도 같이 고침(`VersusSceneSetup`·`TuningSetup`·`MechComponentSetup`·`BuildAndSmokeTest`·`LungeCooldownHudSetup` 등), ③ 이 PC에 Unity가 있으면 `Tools/Build And Smoke Test`를 batchmode로 돌려 컴파일·스모크 확인(V7a·V9f 때 쓴 방법). 안 되면 단계별 커밋 메시지에 "컴파일 미확인"을 적고 TEST_CHECKLIST 첫 항목으로 올림 |
| **`ProjectSettings.asset` preloadedAssets** | 빌드를 돌리면 생기는 부수 효과 | CLAUDE.md대로 `git checkout -- ProjectSettings/ProjectSettings.asset` |
| **문서·주석이 낡음** | Tooltip의 "V8 이동기 →" 경로, CLAUDE.md 폴더 표, SETUP.md | 마지막 단계에서 일괄 갱신 |

### 사용자가 에디터에서 볼 것 (TEST_CHECKLIST에 넣을 항목 초안)

1. 씬을 열었을 때 콘솔에 "The referenced script is missing" 없음, P1·P2·더미 인스펙터에 Missing 없음.
2. VERSUS: 걷기·대시(8방향)·대시 정지·근접 스텝·점프·호버·공중 대시·급강하·착지 경직이 정리 전과 같음.
3. 선회: 상대 반대쪽으로 돌 때 느리고(132°/s) 상대 쪽으로 돌 때 빠름, 33.75~60° 사이 끊김 없음. 가드 중 자동 선회.
4. 카메라: 대시 때 기체가 화면 가로 중앙 ±25% 안, FOV 35.
5. 전투 연결: 넉백·런지·앵커 클로 끌어오기·파일 드라이버 넉다운·대시 어택/태클·몸 충돌 반동·공중 피격 낙하.
6. PRACTICE: 봇이 걷고, 맞으면 밀리고, 런지하고, 넉다운된다.
7. HUD: 차징 바·런지 쿨다운 호·히트 호. PlayLogger CSV가 생기고 대시·점프 카운트가 찍힘.
8. `StyleOverrides`에서 항목 하나(예: 위치 추종)를 끄면 원본 동작(지연 없음)으로 바뀌고, 다시 켜면 돌아옴.

---

## 5. 진행 순서 (확인 후)

태스크 ID는 `CL`로 붙이고 TASKS.md에 Phase CL로 추가한다. 태스크 하나 = 커밋 하나 이상, 매 단계 컴파일 가능한 상태 유지.

| 단계 | 내용 | 성격 |
|---|---|---|
| CL1 | 참조 0인 튜닝 값 3개 삭제, 이벤트(`OnDashStarted/Ended`, `OnJumped`)와 `DashExitReason`을 레거시 상태 밖으로 이동, 구독부 수정 | 삭제 준비 |
| CL2 | `ChangeState(IdleState…)` → `ToNeutral()`, `ActiveDash` null 처리, `RedirectForV8` 제거 | 다리 코드 제거 |
| CL3 | 레거시 상태 6개 + `StickGestureDetector` + 선택 스위치 + `IsV8` 분기 + `MechController`/`MechStateMachine`/`MechCamera` 레거시 경로 삭제, 죽은 `MovementValues` 필드 삭제 | 삭제 |
| CL4 | `IVoHost`·`VoInputAdapter`·`VoHostAdapter` 도입: 코어의 게임 직접 참조를 인터페이스 호출로 치환 (동작 동일) | 의존 방향 |
| CL5 | `VoSpec`(원본 고정) + `StyleOverrides` 분리, 코드에 박힌 변형(V9e 방향 제한 등)을 스위치로, 에디터 메뉴·SETUP | 수치 규칙 |
| CL6 | 폴더 이동(`Core/VirtualOn`, `Bridge`, `Game/*`) + 네임스페이스 + `Assets/Editor` 수정. 코드 내용은 안 바꿈 | 이동만 |
| CL7 | 문서: CLAUDE.md 폴더 표·규칙(코어는 게임을 모른다, 원본값 고정), TEST_CHECKLIST, SETUP, QUESTIONS, PLAN_V8 머리말 | 문서 |

폴더 이동(CL6)을 뒤에 두는 이유: 삭제·치환 diff와 이동 diff가 섞이면 리뷰가 안 된다. 이동 커밋은 `git mv`만 있어 "내용 변경 0"을 확인하기 쉽다.

---

## 6. 확인이 필요한 결정

| # | 질문 | 내가 고를 기본값 |
|---|---|---|
| Q1 | 근접 스텝 대각·뒤 속력처럼 **원본에 값이 없어 채워 넣은 수치**는 코어에 "원본 미상" 표시로 둘까, StyleOverrides로 뺄까? | StyleOverrides로 뺀다 (코어엔 원본에 있는 값만) |
| Q2 | 카메라 FOV 35°는 사양서에 근거가 없다. 원본값으로 볼까 우리 값으로 볼까? | 우리 값(StyleOverrides) |
| Q3 | `Movement.walkSpeed`/`dashSpeed`(m/s, 키 2.6m 환산)를 외형 걸음·봇 예측이 아직 쓴다. 실제 속도(코어 u/f × 환산)로 바꾸면 걸음 모션 전환 속도·봇 패리 타이밍이 변한다 | 이번엔 값 그대로 게임 수치로 유지(동작 변화 없음), 실제 값 연결은 별도 태스크 |
| Q4 | 폴더 이름: `Bridge/`를 최상위에 둘지 `Game/Bridge/`에 둘지 | 최상위 `Scripts/Bridge/` (코어·게임 어느 쪽도 아님을 드러냄) |
| Q5 | `.asmdef`로 의존 방향을 컴파일러가 강제하게 할지 | 이번엔 안 함(grep 검사), 에디터 확인 가능한 뒤 별도 태스크 |
| Q6 | PlayLogger CSV의 `sidestep` 열 | 남기고 항상 0 (형식 유지) |
| Q7 | 미구현 원본 기능(21·29·30·32)은 이번에 **목록만** 남기고 만들지 않는다 — 맞는지 | 목록만 |

---

## 7. 결과 (CL1~CL7 진행 뒤 추가)

사용자 답: Q1 StyleOverrides / Q2 우리 값(근거를 주석·문서에) / Q3 이번엔 그대로, CL8로 따로 / Q4 `Scripts/Bridge/` / Q5 asmdef 안 함(grep 검사) / Q6 `sidestep` 열 남기고 0 / Q7 목록만.

### 7-1. 계획과 다르게 된 것

| 계획 | 실제 | 이유 |
|---|---|---|
| `VoCollision.cs`를 별도 클래스로 | `VoMotor.Collision.cs` (`partial class VoMotor`) | 코드를 한 줄도 안 바꾸고 파일만 나누려고 |
| 상태를 파일 하나씩 | `States/VoState.cs`(서기·걷기·근접 스텝·대시 정지·급강하)·`VoGroundDashState.cs`·`VoJumpState.cs`(점프·공중 대시) 그대로 | 이동만 하는 커밋에 내용 변경을 섞지 않으려고 |
| `IDashMove`·`DashExitReason`은 Bridge | `Core/VirtualOn` | 코어의 대시 상태가 구현하고 `IVoMachine`이 쓰는 타입이라 Bridge에 두면 코어가 Bridge를 참조하게 된다 |
| `MechIdentity`·`HitStop`은 Core 그대로 | `MechIdentity` → Bridge, `HitStop` → `Game/Flow` | 둘 다 Bridge/Game 타입을 참조해 "Core는 Bridge/Game을 모른다"를 깨고 있었다 |
| 가드 자동 선회·사격 조준 선회율은 `BernardTuning` | `StyleOverrides`의 "우리 규칙의 선회율" 묶음 | 이동기에 얹는 우리 값을 한 에셋에 모으려고. `BernardTuning.asset`의 기존 블록을 건드리지 않아도 됨 |
| 원본과 다른 값은 전부 켬/끔 칸 | `stickThreshold`·`cameraFov`·가드/사격 선회율은 값만(끌 수 없음) | 원본에 대응하는 값이 없어 "끄면 원본"이 성립하지 않는다 |
| 게임 → 코어는 Bridge를 거쳐서만 | `PlayLogger`가 코어 상태 타입(`VoAirDashState` 등)을 로그 집계용으로 직접 본다 | 읽기 전용 타입 검사. 규칙은 "코어가 Bridge/Game을 모른다"만 강제한다 |

### 7-2. 원본과 다른 값이 실제로 놓인 곳

| 3절 번호 | 항목 | 놓인 곳 |
|---|---|---|
| 1 | 가드 자동 선회 400 / 1092 | `StyleOverrides.guardTurnInside/Outside` → `VoHostAdapter.TryGetAutoTurnToOpponent` |
| 2 | 빠른 선회 방향 제한 | 코어 스위치 `VoSpec.fastTurnOnlyTowardOpponent`(원본 false) ← `StyleOverrides`(켬) |
| 3 | 선회율 보간 60° | `VoSpec.turnBlendEndAngle`(원본 33.75) ← `StyleOverrides`(60) |
| 4 | 카메라 위치 추종 35% | `VoSpec.cameraPositionFollow`(원본 1) ← `StyleOverrides`(0.35) |
| 5 | 카메라 가로 상한 25% | `VoSpec.cameraMaxScreenOffset`(원본 0.5 = 꺼짐) ← `StyleOverrides`(0.25) |
| 6 | 사격 조준 선회율 2560 | `StyleOverrides.attackAimRate` → `VoMotor.NotifyAttackStarted(rate)`. 15f·×0.1은 `VoSpec` |
| 7 | 근접 스텝 대각·뒤 속력 | `StyleOverrides.closeStepStartFill/EndFill`. `VoSpec`엔 앞·옆 원본 + 중립 채움 |
| 8 | 기체 키 1.93m | `VoSpec.fallbackBodyHeight`(원본 2.6) ← `StyleOverrides`(1.93). 실제 키는 `VoMotor`가 렌더러로 잼 |
| 9 | 충돌 캡슐 | 씬의 캡슐 콜라이더 (`VoMotor.Collision.cs`가 읽음) |
| 10 | 카메라 FOV 35° | `StyleOverrides.cameraFov` — **사양서에 없는 우리 값.** 원본 카메라 거리 9.2m(78u, 기체 키 2.6m 기준)에서 기체가 화면 높이의 45%가 되도록 역산: 2 × atan((2.6 ÷ 0.45) ÷ (2 × 9.2)) ≈ 34.9° → 35° |
| 11 | 카메라 기준 높이 0.5 | `VoSpec.cameraBaseHeightRatio`(미상, 중립 0.5) ← `StyleOverrides`(0.5) |
| 12 | 대시 정지 공격 3f | 값은 `VoSpec.dashEndAttackFrame`, 적용 판단은 `MechStateMachine.DashEndAllowsAttack` |
| 13 | 정렬 오차 0.5° | `VoSpec.alignEpsilon` (미상 표시) |
| 14 | 스틱 문턱 0.3 | `StyleOverrides.stickThreshold` → `VoInputAdapter`. 마우스 감도는 `BernardTuning.Movement` |
| 15·18 | 중립 ○ 앞 대시, 15f 안 근접 스텝 | `VoSpec.neutralTurboDashesForward`(원본 false)·`closeStepFromDashFrames`(원본 0) ← `StyleOverrides` |
| 16·34 | 조작 체계, 마우스 선회 | `Bridge/VoInputAdapter` |
| 19 | 하강 중 급강하 | `VoSpec.diveAllowedWhileFalling`(원본 false) ← `StyleOverrides`(켬) |
| 20 | 상승 중 재점프 삭제 | `VoSpec.risingRejump`(원본 true — V8.2 코드를 스위치 뒤에 되살림) ← `StyleOverrides`(끔) |
| 22·23 | 착지 경직, 공중 피격 낙하 | `Game/States/LandingState`, `MechStateMachine.OnHitResolved` |
| 24 | 기체끼리 분리·몸 충돌 반동 | `VoHostAdapter.AfterIntegrate`, 수치 `BernardTuning.Movement.bumpSpeed` |
| 25 | 외부 속도 모드 | 적분은 `VoMotor.IntegrateExternal`, 판단은 `VoHostAdapter.IsExternalMode` |
| 26·27 | 콤보 감속, 스턴 | `IVoHost.MoveSpeedScale`·`IsStunned` |
| 28 | 대시 어택·태클 | `Bridge/DashAttackRunner` (`IVoDashAttack`) |
| 31 | 카메라 흔들림·지형 충돌 | `Bridge/MechCamera` |
| 21·29·30·32 | 미구현 원본 기능 | 만들지 않음 — 이 문서 3절 (나)에 목록만 |

정리 전 `V8Values` 기본값 77개와 정리 뒤 유효값이 같다는 것은 CL5 스모크 로그의 `[VoSpec] … 유효값`을 스크립트로 대조해 확인했다.

### 7-3. CL8 후보 (Q3 — 이번엔 손대지 않음)

기획서의 m/s 표(기체 키 2.6m 환산)를 아직 읽는 곳. 실제 속도는 키 1.93m 기준 0.74배다.

| 곳 | 읽는 값 | 실제 값 | 실제 값으로 바꾸면 달라지는 것 |
|---|---|---|---|
| `Game/Bot/MeleeDummy`(상대 대시가 닿을 시간 어림) | `BernardTuning.Movement.dashSpeed.front` 24.1m/s | 17.9m/s | Parrier 봇이 대시 어택·태클에 가드를 올리는 시점이 늦어짐(더 정확해짐) |
| `Game/Visual/MechVisualRig.Gait`(걸음 설정 안내 글) | `BernardTuning.Movement.walkSpeed.front` 22.0m/s | 16.3m/s | 디버그 글의 숫자만 |
| `BernardTuning.Gait`의 속도 경계(`strideStartSpeed` 3·`leapStartSpeed` 12m/s) | 실제 속도와 비교 | 걷기 최고 16.3m/s | 값은 그대로 두고 경계를 다시 잡을지 결정 필요 |
| `docs/TEST_CHECKLIST.md`·`GAME_SPEC_V3.md`의 m/s 숫자 | 키 2.6m 환산 | 0.74배 | 문서만 |

방법 후보: `VoHostAdapter`가 `VoSpec`(u/f) × `VoMotor.SpeedScale`로 실제 m/s를 내주고, `BernardTuning.Movement.walkSpeed`·`dashSpeed`는 삭제.
