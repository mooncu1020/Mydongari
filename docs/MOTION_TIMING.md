# 공격 모션 시간표 (M0)

모션 단계는 아래 게임플레이 시간에 맞춘다. **이 값들은 모션 작업(M1~M7)에서 바꾸지 않는다.**
(예외: M4 런지 새 판정 방식이 추가하는 "박기·뻗기" 시간 — 아래 런지 줄 참고. 예전 방식 스위치로 되돌릴 수 있다.)
값 위치는 전부 `Assets/Scripts/Mech/BernardTuning.cs`(에셋 `Assets/Resources/BernardTuning.asset`)이고, 쓰는 코드를 같이 적었다.
시간은 전부 게임 시간(scaled — 히트스톱 동안 멈춤).

| 기술 | 발동(누름 → 판정) | 판정 | 회복 / 뒤처리 | 값 위치 · 코드 |
| --- | --- | --- | --- | --- |
| 펀치 | **0.1초** `Startup.punch` | 0.1초 시점 한 번. `Punch.range` 3m(콜라이더 표면), 상체 정면 ±`Punch.halfAngle` 45° | 쿨다운 `Punch.cooldown` **0.5초**(누른 순간부터) → 판정 뒤 0.4초. 맞딜 창 `Startup.clashWindow` 0.1초. 발생 중 스턴이면 취소 | `MeleeSystem.TryMeleeAttack` / `ResolvePunch` |
| 스러스터 런지 | 없음(누르면 바로 돌진) | 돌진 중 매 물리 스텝 구 판정(정면 `hitForwardOffset` 1m, 반경 `hitRadius` 1.2m). 돌진 = `distance` 8m ÷ `speed` 30m/s ≈ **0.27초**, 최대 ×`timeoutMultiplier` 1.5 = 0.4초 | 명중: 상대 스턴 `hitStun` **0.5초**, 공격자 경직 없음. 빗나감: 하드 락온 금지 `missLockDelay` **0.6초**. 패리: 공격자가 피해 100%·넉백(`Parry.reflectRatio`) | `ThrusterLunge.TickDash` |
| 대시 어택 (질주 1.5초 미만) | **0.12초** `Startup.dashAttack` | 한 번. `Tackle.range` 5m, ±`Tackle.halfAngle` 45° | 공격자 경직 `Tackle.recoveryStun` **0.5초**(패리당하면 없음), 재사용 `Tackle.cooldown` 1초 | `SprintState.FinishTackle`, `MeleeSystem.ResolveTackle` |
| 차징 태클 (1.5초 이상) | **0.1초** `Startup.chargedTackle` | 대시 어택과 같은 범위 | 피격자 스턴 `Tackle.victimStun` 1.7초, 공격자 경직 0.5초, 재사용 1초. 차징 `chargeStartTime` 1.5 ~ `fullChargeTime` 3초 | 같음 |
| 가드 | 즉시(버튼 누름) | — | 내린 뒤 재사용 대기 `Guard.reraiseDelay` **0.4초**(패리 성공 직후 면제) | `MeleeSystem.HandleGuard` |
| 패리 | 가드를 새로 누른 순간부터 | 창 `Parry.window` **0.15초** | — | `MeleeSystem.HandleParry` |
| 가드 브레이크 | 과열 중 가드로 맞은 순간 | — | 스턴 `Guard.breakStun` **0.9초**(공격 스턴이 더 길면 그쪽) | `MeleeSystem.ReceiveHit` |
| 앵커 클로 | 예비 동작 `AnchorClaw.windupTime` **0.2초** | 비행 `range` 12m ÷ `projectileSpeed` 40m/s = 최대 0.3초, 지나간 길 SphereCast | 명중: 상대 경직 `hookImpactStun` 0.25초, 당기기 최대 `pullDuration` 1.5초(`pullStopDistance` 3m 안이면 끝, 처음 `hookEaseTime` 0.2초 훅). 빗나감: `retractSpeed` 40m/s로 감김. 쿨다운 `cooldown` 5초(발사 순간부터) | `AnchorClaw` |
| 파일 드라이버 | **0초**(누른 프레임 즉시 판정) | 잡은 뒤 `pileDriverRange` 4m 안, 피해 `pileDriverDamage` 50, 가드·패리 무시 | 준비 상태는 쓸 때까지(시간 제한 없음). 히트스톱 `HitStop.other` 0초(→ M6에서 전용 칸 추가) | `AnchorClaw.TryPileDriver` |
| 임팩트 슬램 | **0초**(누른 프레임 즉시 판정) | `ImpactSlam.range` 8m, 부채꼴 90° | 쿨다운 `ImpactSlam.cooldown` 8초 | `ImpactSlam.TryActivate` |

히트스톱(실제 시간): 펀치 `HitStop.punch` 0.05초, 런지·태클·대시 어택 `lungeTackle` 0.08초, 맞딜 `clash` 0.12초, 그 외(슬램·파일 드라이버) `other` 0초. 패리는 멈추지 않음. `Core/HitStop.cs`.

## 모션이 이 시간에 맞추는 방법

- 예비 동작 길이 = 발동 시간(펀치 0.1초, 대시 어택 0.12초, 태클 0.1초, 클로 0.2초). 뻗는 포즈가 판정 순간에 도착한다.
- 회복 길이 = 쿨다운·경직 남은 시간(펀치 0.4초, 태클·대시 어택 0.5초, 가드 브레이크 0.9초).
- 발동 0초 기술(파일 드라이버·슬램)은 판정이 누른 순간에 끝나므로, 모션은 아주 짧은 올리기 뒤 타격 포즈 — 외형 타격 순간에 맞춰 히트스톱만 늦춘다(M6, 판정·피해 시간은 그대로).
- 런지 새 판정(M4): 돌진 중 상대가 정면 타격 거리 안에 들어오면 멈추고 "박기 → 뻗기", 판정은 뻗는 구간에만. 박기·뻗기 시간(`Lunge.plantTime`·`strikeExtendTime`)이 새 게임플레이 시간이다. `Lunge.judgeMode`를 예전 방식으로 두면 위 표 그대로.

## 이동 속도·보폭 (N3)

다리 치수는 씬의 Mech_P2 bernard 뼈 위치로 잰 값(엉덩이 관절 Leg_L → 무릎 Shin.L → 발목 Ankle.L): 허벅지 0.32m, 정강이 0.45m, 엉덩이 관절 높이(발바닥까지) 약 1.03m.
최대 보폭 = 2 × 엉덩이~발목 × sin(45°), 무릎 굽힘 = 기본 자세 18° + 걷기 10° / 달리기 18°. 플레이 중 `MechVisualRig → Log Gait Numbers`를 켜면 실제 측정값으로 같은 표가 콘솔에 나온다.

| 상태 | 최대 속도 | 예전 보폭 → 초당 걸음 | 새 초당 걸음 → 필요 보폭 | 다리 최대 보폭 | 체공(걸음 중 두 발 다 뜬 비율) | 걸음 수로 맞추면 |
| --- | --- | --- | --- | --- | --- | --- |
| 걷기 | 5 m/s | 1.6m → 3.1걸음/초 | 1.4 → 3.57m | 1.06m | 70% | 4.7걸음/초 |
| 달리기 | 10 m/s | 2.4m → 4.2걸음/초 | 2.0 → 5.0m | 1.04m | 79% | 9.6걸음/초 |
| 전력질주 | 20 m/s | 3.0m → 6.7걸음/초 | 2.4 → 8.33m | 1.04m | 88% | 19.2걸음/초 |

필요 보폭이 다리 최대 보폭의 3~8배라, 기본(체공)으로 두면 걷기부터 걸음의 70%를 날아가는 깡충 뛰는 걸음이 된다. 이동 속도는 바꾸지 않았다(QUESTIONS N3).

### 정정 (O1)

위 N3 표의 속도(5 / 10 / 20 m/s)는 **코드 기본값**이었다. 실제 게임이 쓰는 `Assets/Resources/BernardTuning.asset` 값은 **걷기 10 / 달리기 20 / 전력질주 30 m/s**다(가속도 10 / 20 / 30). O1부터 이동 방식이 상태별로 나뉘어 아래가 기준이다.

| 상태 | 속도 (에셋) | 이동 모션 | 계산 |
| --- | --- | --- | --- |
| 걷기 | 10 → **2.5 m/s** (메뉴 `Mydongari/Tuning/Apply Feedback Values`로 에셋에 적용) | 체공 없는 걸음, 보폭 1.0m | 2.5걸음/초. 다리 최대 보폭 약 1.06m라 체공 없음. 메뉴를 안 누르면 10m/s라 10걸음/초 |
| 달리기 | 20 m/s 유지 | 스러스터 보조 도약 1.6회/초 | 한 번에 12.5m, 도약의 78%가 공중 |
| 전력질주 | 30 m/s 유지 | 호버 활주 (걸음 없음) | — |

### 속도 구간별 다리 모션 (P2)

에셋 속도: 걷기 10 / 달리기 20 / 전력질주 30 m/s (P1에서 O1의 2.5 취소). 다리 모션은 상태가 아니라 실제 수평 속도로 고르고, 경계 ±1.5m/s에서 섞는다. 디딤걸음 최대 보폭 약 1.05m(무릎 34°).

| 속도 | 다리 모션 | 계산 |
| --- | --- | --- |
| 0 ~ 3 m/s | 걸음 (체공·미끄러짐 없음) | 보폭 1.0m, 3m/s에서 3걸음/초 |
| 3 m/s | 스러스터 디딤걸음 | 2.5걸음/초(상한), 걸음당 1.2m 중 미끄러짐 0.15m |
| 10 m/s (걷기 상태 최대) | 〃 | 2.5걸음/초, 걸음당 4.0m 중 미끄러짐 2.95m |
| 12 m/s | 〃 → 도약 | 2.5걸음/초, 걸음당 4.8m 중 미끄러짐 3.75m |
| 12 m/s ~ (달리기 20m/s) | 스러스터 보조 도약 | 1.6회/초, 20m/s에서 한 번에 12.5m |
| 전력질주 상태 | 호버 활주 | 걸음 없음 |

## NaN이 생길 수 있는 지점 점검 (Q1)

| 위치 | 위험 | 상태 (Q1 시점) |
| --- | --- | --- |
| `MechVisualRig.Spring` (모든 관절 회전·위치, 스커트) | ω·step > 한계면 발산 → 무한대 → NaN. 8Hz·감쇠 0.6은 44fps 아래 | **원인** — 막혀 있지 않음 |
| `Gait.StepLeg` 엉덩이 역산 `Asin(x / 다리)` | 다리 길이 0, x가 범위 밖 | `Clamp(-0.99, 0.99)`, 다리 ≥ 0.05 |
| `Locomotion.KneeBendForDrop` `Acos` | 무릎 역산 cos 범위 밖, 허벅지·정강이 0 | `Clamp(-1, 1)`, 길이 ≥ 0.01 확인 |
| `Gait.HipToAnkle` `Sqrt` | 음수 | 항상 0 이상(a² + b² + 2ab·cos ≥ (a-b)²) |
| `Gait.SlideTravel` `Pow`, `/ slideTime`, `/ slideDistance`, `plant / speed` | 0으로 나누기, 음수 밑 | 0.0001 아래면 그 분기 안 탐, 속도 ≥ 0.01 |
| `Gait.PoseStepping` 속도 ÷ 보폭, 1 ÷ 걸음 수 | 보폭·걸음 수 0 | 보폭 ≥ 0.1, 걸음 수 ≥ 0.01 / 0.5 |
| `Gait.StepLeg`·`Locomotion.PoseLeg` 발 들기 `p / peak`, `/(1 - peak)` | `swingLiftPeak`가 0 또는 1 | 인스펙터 Range 0.3~0.95라 보통은 안전 — 막혀 있지 않음 |
| `Gait.SpeedBand` `/ (2 × halfWidth)` | 0 | 0이면 계단 함수로 |
| `Gait.PoseLeap` `/(1 - contact)` | contact 1 | `Clamp(0.05, 0.6)` |
| `Motion.LeadTime` `anchor / arrive` | arrive 0 | 앞당김 ≤ 기준 × 0.8 |
| `Motion.FindSegment`·`ApplyCarry` `/ span`, `/ time` | 0 | 0이면 1로 |
| `Moves.ReactProfile` `t / outTime` | outTime 0 | t < 0이면 안 들어감 |
| `Moves` 가드 브레이크 `t / _breakLength`, 막음·피스톤 `/ 시간` | 0 | 구간 밖이면 안 들어감, 0.0001 최소 |
| `Grounding` 접지 보정량 | 발바닥 높이가 NaN이면(뼈가 이미 NaN) 그대로 퍼짐 | 막혀 있지 않음 |
| `Locomotion.FixedUpdate` 가속도 `/ fixedDeltaTime` | 0 | > 0 확인 |
| `Gait.UpdateMoveFx` `LookRotation` | 몸 뼈가 NaN이면 방향 NaN | 0 벡터는 아님 — 막혀 있지 않음 |
| `Moves.ApplyPiston`·`AwayFrom`·`HitSparkFx`·`MechCamera.AddDirectionalShake` 정규화 | 0 벡터 | 길이 검사 있음 |
| `MechCamera` 퀵턴 `elapsed / duration`, FOV `speed / maxSpeed` | 튜닝 0 | 막혀 있지 않음(뼈와 무관) |
| 첫 프레임 dt 0, 히트스톱 timeScale 0 | 0으로 나누기 | 스프링은 dt ≤ 0이면 건너뜀, 나머지 `dt / 시간`은 시간 > 0 확인 |
