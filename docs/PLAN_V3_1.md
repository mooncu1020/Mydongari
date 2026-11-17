# V3 1차 계획 — V1 카메라, V2 이동 정리

기준: `docs/GAME_SPEC_V3.md` ("카메라와 시점", "이동", "락온", "부스트"). 예전 `GAME_SPEC.md`와 다르면 V3를 따른다.
브랜치 `v3` (loketest에서 분기, push 안 함). 태스크마다 커밋, 끝에 `Tools/Build And Smoke Test`.

튜닝 에셋(`Assets/Resources/BernardTuning.asset`)에 이미 저장된 필드는 코드 기본값을 바꿔도 에셋 값이 이긴다.
그래서 **값이 바뀌어야 하는 필드는 새 이름으로 추가**한다(에셋에 없는 필드는 코드 기본값으로 읽힘). 지운 필드는 에셋 YAML에 남아도 무시된다.

---

## V1. 카메라 (백뷰 고정, R스틱 몸 회전, 화면 밖 화살표)

### 바꿀 파일

| 파일 | 변경 |
| --- | --- |
| `Camera/MechCamera.cs` | 1인칭(기체 위 1.5m에 카메라) → 몸 뒤 고정 백뷰. 위치 = 기체 + 몸 yaw 기준 (뒤 6m, 위 2.5m), 시선 = 기체 위 `lookHeight` + 앞 `lookAhead`. yaw는 몸 회전을 지수 감쇠로 따라감(`yawFollowSpeed`). 마우스·R스틱 시점 회전, 퀵턴 코루틴(`QuickTurn`), `LockToForward`, 하드락 추적(`TrackTarget/StopTracking`), 질주 중 속도 따라가기 삭제. 지형 충돌(`ResolveCollision`)·흔들림(`AddShake`, `AddDirectionalShake`)·발소리 흔들림·FOV·`ResetView`는 유지. 시작 시 `OffscreenTargetArrow`를 자기 오브젝트에 붙인다 |
| `UI/OffscreenTargetArrow.cs` (새로) | 카메라 하나에 하나. 상대가 이 카메라 화면 밖(뒤 포함)이면 화면(분할된 자기 영역) 가장자리에 상대 방향 삼각형을 OnGUI로 그림. 텍스처는 코드로 생성 → 씬/프리팹 배선 없음 |
| `Movement/MechController.cs` | `RotateToCamera` 삭제(몸이 카메라를 따르지 않고 카메라가 몸을 따름). `ApplyTurn(degrees)` 추가(Rb.MoveRotation). `BodyRelativeDirection(input)` 추가 — 이동 기준을 카메라 대신 몸으로 |
| `Movement/MechStateMachine.cs` | Update에서 R스틱/마우스 X를 회전량으로 모으고 FixedUpdate에서 `ApplyTurn`. 하드락 몸통 회전 중(`IsHardLockFacing`), 스턴, 외부 제어, 런지·넉다운·사이드스텝 중엔 회전 안 함. V1 동안은 트윈스틱 질주 판정 중(`IsLookInputBlocked`)에도 회전 안 함(V2에서 판정 자체 삭제) |
| `Movement/IdleState, WalkState, RunState, SprintState, QuickTurnState, BrakeState.cs` | `RotateToCamera` 호출 제거, 이동 방향을 `BodyRelativeDirection`으로. `SprintState.Enter`의 `LockToForward` 제거. `QuickTurnState`는 카메라 대신 몸을 180° 돌림(V2에서 상태째 삭제) |
| `Combat/LockOnSystem.cs` | `TrackTarget/StopTracking` 호출 제거 (카메라는 몸을 따르고, 하드락이면 몸이 상대를 봄) |
| `Combat/ThrusterLunge.cs` | 런지 방향: 카메라 정면 → 상체 정면(`MechFacing.AttackForward`). 백뷰 카메라는 몸보다 늦게 돌아서 카메라 기준이면 어긋남 |
| `Visual/MechVisualRig.Locomotion.cs` | 상체가 볼 방향(`LookYaw`): 하드락이 아니면 카메라 정면 → 몸 정면 |
| `Mech/BernardTuning.cs` | 이동에 `bodyTurnSpeed`(R스틱 초당 회전각, 기본 120), `mouseTurnSensitivity`(기본 2, 예전 카메라 마우스 감도와 같음) 추가. 락온 `hardTrackSpeed` 삭제(쓰는 곳 없어짐) |
| `Editor/BuildAndSmokeTest.cs` | 명령줄 실행용 `RunFromCommandLine()` 추가 (batchmode에서 같은 빌드·실행·로그 요약을 동기로 돌리고 종료 코드 반환). 메뉴 동작은 그대로 |

### 지우는 것과 참조하는 곳 (V1)

| 지우는 것 | 참조하던 곳 → 처리 |
| --- | --- |
| `MechCamera.QuickTurn()` | `QuickTurnState.Enter` → 몸 회전으로 교체 |
| `MechCamera.LockToForward()` | `SprintState.Enter`, `QuickTurnState.Enter` → 호출 삭제 |
| `MechCamera.TrackTarget/StopTracking` | `LockOnSystem.UpdateHardLock/ReleaseHardLock/ForceRelease` → 호출 삭제 |
| `MechController.RotateToCamera()` | Idle/Walk/Run/QuickTurn의 FixedUpdate → 호출 삭제 |
| `LockOnValues.hardTrackSpeed` | `LockOnSystem` → 같이 삭제 |
| 카메라의 질주 속도 따라가기 (`IsCameraFollowingVelocity` 사용) | `MechCamera.LateUpdate` → 삭제 (프로퍼티는 V2에서 상태머신과 같이 삭제) |

---

## V2. 이동 정리 (항상 달리기, 하드락 10/12m, 땅에서 부스트 회복)

### 지울 상태·시스템과 참조하는 곳

| 지우는 것 | 참조하는 곳 → 처리 |
| --- | --- |
| `WalkState.cs` | `MechStateMachine`(생성·`IsWalking`·`CanStartLunge`), `IdleState`/`RunState`/`SideStepState`/`QuickTurnState`/`BrakeState`의 전환, `BoostGauge`(회복 조건 `IsWalking`), `MechVisualRig.cs`(`IsWalking` → moveState 1) → Run으로 대체 또는 삭제 |
| `BrakeState.cs` | `MechStateMachine`(생성·`CanStartLunge`), `SprintState`(해제 시 Brake로) → 해제 시 Idle/Run |
| `SlideState.cs` | `MechStateMachine`(생성·`ChangeState` 특례·`AllowsHardLockFacing`), `SprintState`(슬라이딩 대기), `BrakeState`, `PlayLogger`(`slide` 카운트·열 이름) → 삭제 |
| `QuickTurnState.cs` (초신지선회) | `MechStateMachine`, `IdleState`/`WalkState`/`RunState`/`SprintState`/`BrakeState` 전환, `BoostGauge.GetQuickTurnCost`, `InputReader.QuickTurnInput`(↓ 튕기기·QuickTurn 액션) → 삭제 |
| `TwinStickSprintInput.cs` (트윈스틱 전력질주·슬라이딩 판정, `SprintExitReason` 정의 포함) | `MechStateMachine.TwinStick`·`UpdateLookBlock`(`IsLookInputBlocked`, `IsCameraFollowingVelocity`), `RunState`(진입), `SprintState`(유지·해제·슬라이딩 대기), `PlayLogger`(해제 이유·두 스틱 값) → `SprintExitReason`은 남는 값만 `SprintState.cs`로 옮김 |
| 가속도 시스템: `MechController.ApplyMoveForce`(방향 전환 저항·스핀업), `WalkSpeed/RunSpeed/SprintSpeed/MaxWalkSpeed` | Walk/Run/Sprint FixedUpdate → 속도 직접 지정(`SetMoveVelocity`) |
| `InputReader.RunInput`, `SlideInput`, `QuickTurnInput`, `LookStickRaw`, `StickGestureDetector.FlickDown` | 위 상태들, `TwinStick` → 삭제. `.inputactions`의 Run/Slide/QuickTurn 액션·바인딩은 규칙대로 지우지 않고 읽지만 않음 |
| 튜닝: `walkSpeed, runSpeed, sprintSpeed, maxWalkSpeed, turnResistance, spinUpTime, rotationSpeed, slide*, quickTurnDuration, brake*, sprint*Stick*/Entry/Exit*, sprintCameraFollowSpeed, cameraLookResumeNeutral`, 부스트 `quickTurnCost, slideCost`, 락온 `hardEnterRange, hardExitRange, hardMaxAngle` | `MechVisualRig.Gait`(로그에 `maxWalkSpeed`) → 근접 속도로 교체, `MeleeDummy`(`hardEnterRange`로 펀치 판정) → 새 `meleeSwitchRange` |
| `LockOnSystem.BlockAllLockFor` (슬라이딩이 상대 락온을 끊던 것) | `SlideState`만 → 같이 삭제 |

**남기는 것:** `SprintState`는 키보드 Space 전력질주 경로만 남긴다(패드 경로 없음). 3단계 ○ 대시가 이 상태(태클 차징·대시 공격 판정)를 바꿔 쓸 예정이라 지금 지우지 않는다. 패드에서는 대시 공격·태클이 임시로 안 나간다.

### 바꿀 파일

| 파일 | 변경 |
| --- | --- |
| `Movement/RunState.cs` | 항상 달리기. L스틱 기울기(데드존 → 1을 0 → 1로 다시 맞춤)만큼 즉시 속도. 하드락 밖: 몸 기준, 최대 `maxRunSpeed` 20m/s. 하드락 안: 상대 기준(앞 = 상대 쪽), 최대 `closeMoveSpeed` 8m/s. 콤보 이동 배율(`MoveSpeedScale`) 유지 |
| `Movement/IdleState.cs` | 스틱 중립이면 즉시 정지(수평 속도 0). 스턴·외부 제어·넉백 유지 시간 중엔 건드리지 않음 |
| `Movement/MechController.cs` | `SetMoveVelocity`, `ApplyRunVelocity`, `StopHorizontal`, `HoldMoveControl`(넉백 뒤 `knockbackMoveHold`초 동안 속도를 덮어쓰지 않음 — 속도를 직접 넣으면 넉백 임펄스가 다음 물리 스텝에 지워지기 때문) |
| `Movement/MechStateMachine.cs` | 상태 목록: Idle, Run, Sprint(키보드), SideStep, Lunge, Knockdown |
| `Movement/SprintState.cs` | 트윈스틱 유지·해제·슬라이딩 대기 삭제, 버튼 경로만. 이동은 속도 직접 지정 |
| `Movement/SideStepState.cs` | 끝나면 Walk 대신 Run |
| `Combat/LockOnSystem.cs` | 하드락 = 상대가 기체 기준 3D 거리 10m 안이면 걸림, 12m 밖이면 풀림. 시야 각도·가려짐 조건 없음(소프트 락온과 무관). 하드락 중엔 `Target` = 상대. 소프트 락온(앵커 클로·HUD용)은 그대로 |
| `Combat/WeaponSystem.cs`, `Combat/MeleeDummy.cs` | 펀치/런지 전환 = 상대까지 거리 ≤ `meleeSwitchRange` 6m (하드락 여부와 따로) |
| `Combat/MeleeSystem.cs` | 넉백을 줄 때 맞은 기체의 `MechController.HoldMoveControl()` |
| `Mech/BoostGauge.cs` | 회복 조건: Idle 또는 Run(달리기·근접 이동·정지) 그리고 가드 아님. 탈진 0%·해제 50% 그대로 |
| `Mech/BernardTuning.cs` | 추가: 이동 `closeMoveSpeed`(8), `knockbackMoveHold`(0.35), 락온 `hardLockEnterRange`(10), `hardLockExitRange`(12), `meleeSwitchRange`(6). `maxRunSpeed` 기본 20(에셋에도 20) |
| `Input/InputReader.cs`, `Input/StickGestureDetector.cs` | 위 삭제 반영. `MoveDeadzone` 공개 |
| `Core/PlayLogger.cs` | `slide`·트윈스틱 해제 이유 삭제, 질주 시작/해제(버튼)만 |
| `Visual/MechVisualRig.cs`, `MechVisualRig.Gait.cs` | `IsWalking` 분기 삭제, 로그의 `maxWalkSpeed` → `closeMoveSpeed` |

### 연습 봇
`MeleeDummy`는 상태머신 없이 자기 이동을 쓰므로 상태 삭제의 영향이 없다. 바뀌는 건 펀치 판정 거리 필드 이름(`hardEnterRange` → `meleeSwitchRange`, 값 6 그대로)뿐. 봇 `BoostGauge`는 상태머신이 없으면 지금처럼 가드 아닐 때 회복.

### 남는 위험
- 대시·점프가 없어 하드락(10m)에서 빠져나갈 수단이 사이드스텝뿐(3·5단계에서 해결).
- 넉백 유지 시간(0.35초) 동안은 스틱 입력이 속도를 바꾸지 않는다. 너무 길면 조작이 끊긴 느낌, 짧으면 넉백이 짧아짐 → 튜닝.
