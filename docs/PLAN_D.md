# Phase D 작업 계획

전제: Phase C 에디터 확인 완료. C 질문 중 로그 폴더·STUN 팝업·런지 명중 효과음은 C4로 반영했다.
현재 DummyTarget 구성: Health, MeleeSystem, MeleeDummy, MechTuning, Rigidbody, CapsuleCollider (상태 머신·게이지·런지 없음).

---

## D1. 연습 봇

**원칙**: 봇 전용 판정은 만들지 않는다. 봇의 공격·피격·가드·패리·히트·가드 깨짐은 플레이어와 같은 `MeleeSystem`/`HeatGauge`/`BoostGauge`/`ThrusterLunge` 코드를 그대로 지난다. 봇이 다른 점은 "입력을 누가 넣느냐"뿐이다.

**신규**
- `Input/BotInput.cs` (`ICombatInputSource` 구현): 봇 두뇌가 `SetGuard(bool)`로 가드 버튼을 누르고 뗀다. 누른 프레임에만 `GuardPressedThisFrame` = true (패리 창 규칙 그대로).

**수정**
- `MeleeDummy` → 연습 봇 두뇌. `enum BotMode { Idle, AlwaysGuard, Approach, Lunger, Parrier, Mixed }`.
  - `[DefaultExecutionOrder(-50)]`: Awake에서 없는 `BotInput`·`HeatGauge`·`BoostGauge`·`ThrusterLunge`를 붙인다(다른 컴포넌트 Awake보다 먼저 → MeleeSystem이 히트 게이지·입력 소스를 정상으로 찾음). 씬 편집 없음.
  - Idle: 아무것도 안 함. AlwaysGuard: 상대를 보며 제자리 가드 유지(가드 히트 → 과열 → 가드 깨짐 규칙 그대로). Approach: 기존 동작(다가와서 펀치).
  - Lunger: 상대와 `lungerMinDistance`(6)~`lungerMaxDistance`(12)를 유지(가깝면 물러나고 멀면 다가감), 조준이 맞으면 `lungeInterval` 무작위 간격으로 `ThrusterLunge.TryLunge()`.
  - Parrier: 상대의 공격 발동 이벤트를 구독해 `parryChance` 확률로 가드를 새로 누른다.
    - 런지: 돌진 시작을 보고 `reactionTime` 뒤, 예상 도착 시각에 패리 창이 걸치도록 누름 (거리 ÷ 런지 속도). 도착이 반응 시간보다 빠르면 못 막음.
    - 펀치·대시 어택·태클: 발동과 명중이 같은 프레임이라 "보고 반응"이 불가능 → 발동 순간 확률로 "읽었다" 치고 즉시 누름(`MeleeSystem.ApplyGuardInputNow()` — 일반 가드 처리를 지금 한 번 돌리는 범용 함수, 재사용 대기·스턴 규칙 그대로).
    - 패리 성공 후 `counterDelay` 뒤 반격 펀치(옵션). 평소엔 Approach처럼 다가가되 먼저 때리지 않는다.
  - Mixed: AlwaysGuard/Approach/Lunger/Parrier 중 하나를 `mixedSegment` 무작위 시간마다 바꿔 가며.
  - 공개: `Mode`, `SetMode`, `CycleMode()`, `ActiveBehaviour`(Mixed일 때 현재 하위 행동).
- `MeleeSystem`: `SetInputSource(ICombatInputSource)`, `ApplyGuardInputNow()`.
- `ThrusterLunge`: 상태 머신이 없는 기체는 스스로 돌진을 진행(TryLunge → BeginDash, FixedUpdate에서 TickDash, 스턴·잡힘이면 EndDash). 판정·이벤트는 동일.
- `HeatGauge`/`BoostGauge`: 상태 머신이 없으면 "질주 안 함, 가드 안 할 때 회복"으로 동작 (지금은 아예 멈춤).

## D2. 타이틀과 모드 선택

- `GameManager`: `enum GameMode { Versus, Practice }`, `MatchPhase.Title` 추가. 씬 하나, 상태로 전환.
  - 시작 → Title (`showTitleOnStart`, 끄면 예전처럼 `versusMode` 값으로 바로 시작).
  - 배치 전환 `ApplyLayout(mode)`: Versus = P2 기체·P2 HUD 켬, 더미 끔, P1 카메라 왼쪽 절반 / Practice = P2 쪽 끔, 더미(봇) 켬, P1 카메라 전체. 상대 연결을 다시 하고 `VersusInputPairing.Apply(versus)`로 장치 재배정(Practice에서는 패드 0이 P1).
  - Title 배경은 Versus 배치(두 사람 다 메뉴 조작 가능). 2인 구성이 없으면 VERSUS 비활성.
  - Practice: 라운드 제한 없음. KO → `practiceRespawnDelay`(1초) 뒤 두 기체 리셋하고 계속(카운트다운 없음, 승수 없음).
  - 메뉴: Title [VERSUS / PRACTICE], Versus 매치 종료 [REMATCH / TITLE], Practice 중 Start → [CONTINUE / TITLE] (연습에서 나갈 길). 메뉴 동안 게임 입력 잠금.
- `InputReader`: 메뉴 입력(잠금 중에도 읽힘) `MenuVertical`(이동 스틱·D패드·W/S 위아래 한 번씩), `MenuConfirmPressed`(Start·A·Enter).
- `.inputactions`(추가만): `Submit`(패드 South) 등 (D3 단축키와 함께).
- 신규 `UI/MenuUI.cs`: 런타임에 Overlay 캔버스를 만들어 타이틀·메뉴·연습 봇 모드 표시(왼쪽 위 구석)를 그린다. 씬 편집·메뉴 실행 불필요. GameManager가 자동 부착.
- `RoundUI`: Title에선 숨김, Practice에선 승수 숨김·KO 표시, 매치 종료 문구 아래 안내는 MenuUI가 대신.
- `PlayLogger`: 모드 전환 대응(P2/더미 모두 구독, 현재 P2를 매 프레임 따름), match_start에 모드 기록, 타이틀로 나갈 때 요약 기록.
- `HitStop`·`CombatFeedback`: P2와 더미 모두 구독(꺼진 기체는 이벤트가 안 나므로 안전).

## D3. 연습 단축키 (Practice에서만, P1 입력)

- `.inputactions` 추가: `Select`(패드 Select/Back/Share), `BotCycle`(Tab), `PracticeReset`(R), `HeatPreset1~4`(1~4).
- `InputReader`: Select 짧게 = 봇 모드 순환(뗄 때), Select 길게(`selectHoldTime` 0.6초) = 리셋. Tab = 순환, R = 리셋, 1~4 = 히트 0/50/90/100%.
- 신규 `Core/PracticeHotkeys.cs`(GameManager 자동 부착): Practice + Fight일 때만 처리.
  - 리셋: 두 기체 체력·부스트·히트·위치(스폰)·상태 즉시 초기화 (`GameManager.ResetPracticeNow()` — 라운드 리셋과 같은 함수).
  - 히트 프리셋: `HeatGauge.SetHeatPercent(p)` (100%면 과열 상태로).
- **충돌**: 키보드 R은 이미 초신지선회. Practice에서 R은 리셋으로 쓰고, 리셋 직후 그 프레임 입력을 비워 퀵턴이 같이 나가지 않게 한다(QUESTIONS에 기록).
