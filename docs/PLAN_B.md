# Phase B 작업 계획

현재 씬: `Mech`(카메라가 자식: `Mech/CameraRoot/Camera`), `DummyTarget`, `Canvas`(Screen Space Overlay, 자식 `HUD` + HUDController), `GameManager`, `PlayLogger`, 스폰 포인트 2개.
카메라가 Mech의 자식이라 Mech를 복제하면 카메라도 같이 복제되고, 기체 내부 참조(MechController → 카메라, MechCamera → target)는 Unity가 복제본끼리 자동으로 다시 연결한다.

---

## B1. 2인 화면 분할
**신규**
- `Assets/Scripts/Input/VersusInputPairing.cs` (`MyDongari.Input`): P1/P2 `PlayerInput`에 장치를 고정 배정. 패드 2개 이상 → P1=패드0, P2=패드1. 패드 1개 → P2=패드, P1=키보드·마우스. P1에 키보드·마우스 추가 허용(`allowKeyboardForP1`). 자동 스킴 전환을 끄고 바인딩 마스크를 비워 페어링된 장치의 모든 바인딩을 쓴다. 패드 연결/해제 시 다시 배정.
- `Assets/Editor/VersusSceneSetup.cs`: 메뉴 `Mydongari/Setup Versus Scene`.
  1. `Mech` 복제 → `Mech_P2` (스폰 P2 위치·회전). 복제본 카메라의 AudioListener 제거, 태그 Untagged.
  2. 카메라 viewport: P1 `(0,0,0.5,1)`, P2 `(0.5,0,0.5,1)`.
  3. `Canvas`를 Screen Space - Camera(P1 카메라)로 바꾸고 복제해 `Canvas_P2`(P2 카메라). 복제 HUD의 HUDController 참조를 P2 기체 컴포넌트로 교체. 각 HUD에 상대 체력 바(HPBar 복제, 작게) 추가.
  4. `GameManager`: player1=Mech, player2=Mech_P2, dummyOpponent=DummyTarget, versusOnlyObjects=[Mech_P2], player1Camera. `VersusInputPairing` 추가·연결.
  5. `PlayLogger` player1/player2 연결.
  6. 전부 Undo 그룹 하나로. 이미 `Mech_P2`가 있으면 중단.

**수정**
- `GameManager`: `versusMode` 토글, `dummyOpponent`, `versusOnlyObjects`, `player1Camera`. 1인 모드면 P2 쪽 오브젝트를 끄고 player2=더미, P1 카메라 전체 화면. `Player1`/`Player2`/`VersusMode` public getter.
- `HUDController`: Screen Space - Camera에서도 락온 마커 위치가 맞게(스크린 → 캔버스 평면 변환), 카메라 뒤 목표는 숨김. HP는 `Health.MaxHealth` 기준. 상대 체력 바(`opponentHpBarFill`, 상대는 MechIdentity.Opponent).
- `Health`: `MaxHealth` getter.

## B2. 패드 스틱 제스처 (`InputReader`)
- 튕기기 = 중립(<0.3) 이탈 후 `flickReachTime`(0.15초) 안에 끝(>0.8) 도달 → `flickReleaseTime` 안에 중립 복귀. 방향은 끝에 도달한 순간의 스틱 원래 방향(카메라 무관), 정방향 ±`flickSectorHalfAngle`(30°).
- 출력(기존 키보드 경로와 OR): `QuickTurnInput`(↓ 튕기기), `SideStepDirection`(←/→ 튕기기), `SprintInput`(↑ 튕기기 직후 `doubleFlickWindow` 안에 다시 ↑ 끝 도달 → 스틱이 중립으로 돌아올 때까지 래치), `SlideInput`(중립 진입 후 `slideWindow` 0.2초 안에 ↗/↖ 끝 도달), `SlideDirection`.
- 키보드 WASD는 제스처 대상이 아님(Move의 activeControl 장치가 Gamepad/Joystick일 때만).
- 패드 걷기/달리기: `RunInput` = Shift || 스틱 크기 ≥ `runThreshold`(0.8). 이동 시작 판정은 `IsMoveInputActive`(크기 ≥ `moveDeadzone` 0.3)로 바꿔 상태들의 `sqrMagnitude >= 0.64` 하드코딩 교체(키보드는 크기 1이라 영향 없음).
- 스펙대로 `SprintState`·`BrakeState`에서도 퀵턴·사이드스텝 허용.
- 시간은 `Time.unscaledTime` (B7 히트스톱 대비).

## B3. 스러스터 런지
- 신규 `Combat/ThrusterLunge.cs`: 거리 8, 속도 30, 부스트 15, 피해 15, 스턴 0.5, 빗나감 락 지연 0.6, 판정 반경·전방 오프셋. `TryLunge()`(조건 검사 → `LungeState`로 전환), `BeginDash/TickDash/EndDash`, 이벤트 `OnLungeStarted`, `OnLungeEnded(HitResult?)`(null=빗나감).
- 신규 `Movement/LungeState.cs`: 이동 입력 무시, 속도 직접 지정(SideStep/Slide 방식), 외부 잠금·스턴이면 종료.
- `LockOnSystem`: `SetHardLockSuppressed(bool)`, `BlockHardLockFor(seconds)`. 돌진 중 금지, 빗나감 후 0.6초 금지. 상대의 슬라이딩도 이걸로 내 락온 강제 해제(스펙 3장).
- `MechStateMachine.LungeState`, `AllowsHardLockFacing`에 LungeState 제외 추가.
- `WeaponSystem`: GAME_SPEC 4장 순서로 재작성. `enum RangedMode { Lunge, Projectile }`, `rangedMode` 기본 Lunge. 런지는 `AttackPressedThisFrame`.
- `InputReader`: `AttackPressedThisFrame`.

## B4. 대시 어택 / 버튼 태클
- `SprintState.RequestTackle()`: 버튼 요청과 차징 자동 접촉을 FixedUpdate 한 곳에서 한 번만 처리.
- `MeleeSystem.TryTackle(chargePower, AttackType)`: 1.5초 미만 `DashAttack`(가드에 막힘, 스턴 없음), 이상 `Tackle`(관통 + 스턴, 기존 공식). 대시 어택 수치 필드 신설.
- `WeaponSystem` 우선순위 2에 연결.

## B5. 라운드 연출과 매치 흐름
- `GameManager`: `MatchPhase { Countdown, Fight, RoundOver, MatchOver }`. 카운트다운 3초 동안 입력 잠금 → Fight. 라운드 종료 3초 후 다음 라운드. 매치 종료 → 아무 플레이어의 Start(패드 Start / 키보드 Enter)로 재매치(상태 초기화, 씬 리로드 없음). 이벤트 `OnRoundStarted(round)`, `OnFight`, `OnRoundOver(winner)`, `OnMatchOver(winner)`.
- `InputReader`: `SetInputLocked(bool)`, `StartPressed`. `.inputactions`에 `Start` 액션 추가.
- `MeleeDummy`: Fight가 아니면 정지.
- 신규 `UI/RoundUI.cs`: 중앙 공용 텍스트(카운트다운/FIGHT/라운드 승자/매치 결과) + 승수 표시. GameManager 상태를 읽기만 함.
- `VersusSceneSetup`에 공용 UI 캔버스 생성 단계 추가 (레거시 `UnityEngine.UI.Text` + 기본 폰트).

## B6. PlayLogger 확장
- 플레이어 참조는 GameManager의 현재 player1/player2(1인 모드면 더미). 프레임 로그에 `round`, `dist_band` 컬럼.
- 이벤트 구독만 사용(전투 코드에 로거 의존 없음): `MeleeSystem.OnAttackStarted/OnHitResolved`, `ThrusterLunge.OnLungeStarted/OnLungeEnded`, `AnchorClaw.OnGrabbed`(신규 이벤트), `MechStateMachine.OnStateChanged`(신규, 사이드스텝 카운트), GameManager 라운드 이벤트.
- `events_*.csv`(time, round, player, event, distance), `summary_*.csv`(플레이어별 카운트 + 거리 구간 체류 시간), 매치 종료·종료 시 저장.

## B7. 히트스톱 (여유 있으면)
- 신규 `Core/HitStop.cs`: `Time.timeScale = 0`을 실제 시간으로 잠깐 유지. `OnHitResolved` 구독으로 결과별 길이(펀치 0.05, 런지·태클 0.08, 맞딜 0.12).
- 스턴 타이머는 scaled `deltaTime`이라 히트스톱 동안 자동으로 멈춤 → 런지 스턴이 히트스톱 이후부터 흐른다. 입력 타이밍은 unscaled.
