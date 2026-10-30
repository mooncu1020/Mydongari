# Phase A 작업 계획

현재 씬 구성(코드 판단용): `Mech`(Player 태그, 전 컴포넌트) / `DummyTarget`(Enemy 태그, Health + MeleeSystem + MeleeDummy + Rigidbody) / `Camera`(MainCamera 태그, MechCamera) / `GameManager`(player1 = Mech, player2 = DummyTarget). 
→ GameManager가 player1·player2를 서로의 Opponent로 연결하면 추가 배선 없이 1인 + 더미 구성이 동작하게 만든다.

---

## A0. 1인 전제 구조 걷어내기

**신규**
- `Assets/Scripts/Core/MechIdentity.cs` (`MyDongari.Core`)
  - `PlayerIndex`(int, 1/2), `Opponent`(MechIdentity, 인스펙터 지정 가능 + `SetOpponent()`), `CameraTransform`(비어 있으면 `MechController.CameraTransform`으로 대체), `MechCamera`, `ViewCamera`(UnityEngine.Camera).
  - `IsOpponentCollider(Collider)`: 콜라이더의 `GetComponentInParent<MechIdentity>() == Opponent`. 자기 자신은 절대 true가 안 된다.
  - `static GetOrAdd(GameObject)`: 컴포넌트가 없으면 런타임에 붙인다 (더미 세팅 없이 동작하게).
- `Assets/Scripts/Input/ICombatInputSource.cs`: `AttackHeld`, `GuardHeld`, `GuardPressedThisFrame`. InputReader가 구현. 더미는 구현체가 없어 가드 false.

**수정**
- `InputSystem_Actions.inputactions` (추가만): Player 맵에 `Guard`(Button), `StepLeft`/`StepRight`(Button, 키보드 A/D). 바인딩 추가: Attack ← `<Gamepad>/rightTrigger`, Guard ← `<Gamepad>/leftTrigger`·`<Mouse>/rightButton`, Skill1 ← `<Gamepad>/leftShoulder`, Skill2 ← `<Gamepad>/rightShoulder`.
- `InputReader`: `AttackHeld`, `GuardHeld`, `GuardPressedThisFrame`, `IsLookFromGamepad` 추가. 사이드스텝 더블탭을 `StepLeft/StepRight` 액션의 `WasPressedThisFrame`으로, 더블탭 간격은 SerializeField.
- `MeleeSystem`: `Mouse.current` → `ICombatInputSource`. 펀치/태클 대상 → `MechIdentity.IsOpponentCollider`.
- `WeaponSystem`: `Mouse.current` → `InputReader.AttackHeld`. 근접 판정 태그 → Opponent 거리. 발사 방향 → 자기 카메라. `Projectile.Init`에 발사자 전달.
- `Projectile.Init(direction, target, aimCamera, owner)` (public 시그니처 변경, 참조: `WeaponSystem.TryFire` 1곳). 명중은 owner의 Opponent만.
- `LockOnSystem`: 카메라는 `MechIdentity.CameraTransform`, 대상 판정은 Opponent.
- `ImpactSlam`, `SprintState`(태클 접촉), `MeleeDummy`: 태그 → Opponent.
- `MechController`: `Identity` 프로퍼티 추가 (SprintState에서 사용).
- `HUDController`: `Camera.main` → `[SerializeField] viewCamera`, 비어 있으면 lockOnSystem 기체의 `MechIdentity.ViewCamera`.
- `MechCamera`: `stickSensitivity`(초당 각도) 추가. 패드면 `LookInput × stickSensitivity × deltaTime`, 마우스는 기존 그대로.
- `GameManager`: Awake에서 player1/player2에 MechIdentity를 붙이고 PlayerIndex·Opponent 연결(인스펙터에서 이미 지정했으면 덮어쓰지 않음).

## A1. 락온 거리 기체 기준
- `LockOnSystem.DetectTarget`: OverlapSphere 대신 Opponent 하나만 검사 (카메라 기준 원뿔 30°, 50m, 레이캐스트 차폐 — 자기 기체 콜라이더는 무시).
- `UpdateHardLock`: 거리 = `transform.position` ↔ Target. 각도 판정은 카메라 정면 기준 유지. 기본값 6/7.
- `WeaponSystem`: `meleeAutoRange`·`IsEnemyInMeleeRange()` 삭제(private). 펀치 조건은 `IsHardLocked` 하나.

## A2. 부스트 규칙
- `BoostGauge.IsOverheated` → `IsExhausted` (참조: `WalkState`×3, `RunState`, `SprintState`, `MeleeSystem`, `HUDController`).
- `exhaustRecoverRatio`(0.5) 도달 시 탈진 해제. 가드 중(`MeleeSystem.IsGuarding`) 회복 정지 + 회복 지연 타이머 리셋.
- `MeleeSystem.guardBoostDrain` 삭제, 가드의 탈진 체크 삭제.

## A3. 히트 규칙
- `HeatGauge.CanPunch()` 추가. `MeleeSystem.TryMeleeAttack`: 과열이면 return, 발동 시 `AddHeat(heatPerPunch=6)`.
- `IsSkillActive`는 어디서도 true로 세팅되지 않음 → 동작 영향 없음. QUESTIONS에 기록.

## A4. 가드 규칙 / 판정 일원화
- 신규 `Assets/Scripts/Combat/HitTypes.cs`: `enum AttackType`, `enum HitResult`, `struct HitInfo`(Type, Damage, Attacker, KnockbackDirection, KnockbackForce, StunDuration).
- `MeleeSystem.ReceiveHit(HitInfo) : HitResult` — 맞딜(펀치끼리) → 패리 → 관통(Lunge/Tackle) → 가드(깨짐 포함) → 피격 순서로 판단하고 피해·넉백·스턴·히트까지 적용. `OnHitResolved` 이벤트 발생.
- `ApplyDamage(float)` → `ApplyDamage(float, AttackType)` (부작용 없는 감쇄 계산, 내부 사용). 기존 참조(`Projectile`, `AnchorClaw.TryPileDriver`, `MeleeSystem` 내부)는 모두 `ReceiveHit`으로 교체.
- 신규 SerializeField: `guardMeleeReduction` 0.5, `guardProjectileReduction` 0.7, `guardHeatPerBlock` 10, `guardBreakStun` 0.9, `guardReraiseDelay` 0.4, `guardDamping` 8, `clashKnockback` 10. `guardDamageReduction` 삭제.
- 스턴 중엔 가드·패리 강제 해제.

## A5. 패리 규칙
- 패리: 피해 0, 히트 0, 가드 재사용 대기 면제 플래그.
- Tackle/Lunge 패리 시 공격자에게 `피해 × parryReflectRatio(1.0)` + 반대 방향 넉백. 스턴 없음.
- `attackerStunDuration` 삭제. `TryTackle` 반환형 `void` → `HitResult?` (참조: `SprintState` 1곳). SprintState의 하드코딩 자기 스턴 0.5 → `MeleeSystem.TackleRecoveryStun`, 패리당했으면 적용 안 함.

## A6. 펀치 판정
- 사거리 3m(`meleeRange`) + 정면 ±45°(`punchHalfAngle`) + Opponent 콜라이더만. 맞딜은 ReceiveHit 안에서 유지.
- `TryMeleeAttack` 반환형 `void` → `HitResult?` (참조: `WeaponSystem`, `MeleeDummy` — 반환값 무시라 호환). `OnAttackStarted` 이벤트.
- `MeleeDummy`가 상대를 바라보게 회전 추가 (부채꼴 판정 때문에 안 돌면 더미 펀치가 안 맞음).

## A7. 임팩트 슬램
- Opponent만, 전방 90°/8m, `ReceiveHit(Slam)`. 넉백은 ReceiveHit이 Parried가 아닐 때만 적용.

## A8. 라운드 리셋
- `ResetForRound()` 추가: `BoostGauge`, `HeatGauge`, `MeleeSystem`, `AnchorClaw`, `ImpactSlam`, `LockOnSystem`, `MechStateMachine`, `ExternalControlLock`, `MechCamera`(`ResetView(yaw)` — 안 하면 Idle의 RotateToCamera가 기체를 이전 카메라 방향으로 돌려버림).
- `GameManager.ResetMech(mech, spawn)`: 위치·회전(Transform + Rigidbody), 속도·각속도 0, 위 리셋 순서대로 호출. 
- 양쪽 동시 사망 → 무승부, 승수 증가 없이 재시작.
