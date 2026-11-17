# Phase C 작업 계획

전제: Phase B까지 Unity에서 컴파일 확인, 1인 모드 동작 확인. 2인(패드) 확인은 아직.
B 질문 답은 B8(태클 정면 ±45°)로 먼저 반영했다.

---

## C1. 튜닝 에셋 통합 (`BernardTuning`)

**신규**
- `Assets/Scripts/Mech/BernardTuning.cs` (`MyDongari.Mech`, ScriptableObject, `CreateAssetMenu`).
  묶음(직렬화 클래스, 필드는 `StickGestureSettings`처럼 public + Tooltip):
  - 이동: 걷기/달리기/질주 가속·최대 속도, 회전, 하드 락온 몸통 회전, 관성(turnResistance·spinUpTime),
    사이드스텝(시간·속도), 슬라이딩(시간·속도), 초신지선회 시간, 제동 시간·슬라이딩 입력 창.
    (지금 상태 클래스에 `const`로 박혀 있는 값도 같이 옮긴다)
  - 부스트, 히트, 펀치(+맞딜 넉백), 가드, 패리, 태클(+차징 시작·최대 시간, 자동 접촉 반경), 대시 어택,
    런지, 앵커 클로(+당김 종료 거리·파일드라이버 거리), 임팩트 슬램, 락온, 히트스톱.
  - 범위 밖: 체력(`Health`는 더미·기타 오브젝트와 공용), 입력 판정(스틱 데드존·제스처), 카메라, 투사체(사격 기체용), 더미 AI.
- `Assets/Scripts/Mech/MechTuning.cs`: 기체 루트에 붙는 참조 하나(`tuning`). 컴포넌트들은 이걸 통해 에셋을 읽는다.
  - 찾는 순서: 기체의 `MechTuning.tuning` → `Resources/BernardTuning` → 코드 기본값 인스턴스.
  - 값은 쓸 때마다 에셋에서 읽는다(캐시 안 함) → 플레이 중 에셋 값을 바꾸면 바로 반영.
  - 조회는 지연 1회(`TuningRef` 구조체): 런타임에 붙는 컴포넌트·Awake 순서와 무관.
- `Assets/Editor/TuningSetup.cs`
  - `Mydongari/Tuning/Create Default Bernard Tuning`: `Assets/Resources/BernardTuning.asset` 생성(있으면 선택만) + 씬의 모든 기체(MeleeSystem이 있는 오브젝트)에 `MechTuning` 추가·연결.
  - `Mydongari/Tuning/Copy P1 Values To Tuning`: GameManager Player1 기체의 (숨겨진) 기존 필드 값 + GameManager의 HitStop 값을 에셋으로 복사. Undo 가능, 바뀐 항목 목록을 다이얼로그로.
- `VersusSceneSetup`: 복제 전에 P1에 에셋 연결(없으면 생성) → P2가 같은 에셋을 물려받음. P2도 명시적으로 다시 연결.

**수정**: `MechController`, 상태 6개(SideStep/Slide/QuickTurn/Brake/Sprint/Lunge 감속 값은 기존 2 유지), `BoostGauge`, `HeatGauge`, `MeleeSystem`, `ThrusterLunge`, `LockOnSystem`, `AnchorClaw`, `ImpactSlam`, `HitStop`.
- 기존 개별 필드는 **`[HideInInspector]`로 숨기고 남긴다** (삭제하면 씬에 저장된 값을 복사 메뉴가 읽을 수 없다). 런타임 코드는 읽지 않는다. 경고 억제 `#pragma warning disable 0414`.
- 복사가 끝나면 나중 태스크에서 삭제 가능(SETUP에 적음).

## C2. 상태 피드백 (`Assets/Scripts/Feedback/`, `MyDongari.Feedback`)

- `CombatFeedback` (GameManager 오브젝트, 없으면 GameManager가 자동 부착): 두 기체의 기존 이벤트만 구독.
  - `MeleeSystem.OnHitResolved`(피격자 쪽): Parried → 피격자 머리 위 PARRY, GuardBroken → GUARD BREAK, Clash → 양쪽 CLASH.
  - 이벤트가 없는 상태(STUN/OVERHEAT/EXHAUSTED)는 공개 프로퍼티(`IsStunned`, `IsOverheated`, `IsExhausted`)의 false→true 변화를 매 프레임 확인 — 전투 코드 수정 없음.
  - 틴트: 스턴(노랑) > 과열(빨강, 깜빡임) > 가드(파랑). `MaterialPropertyBlock`으로 `_BaseColor`/`_Color`만 덮어쓰고 머티리얼 자체는 건드리지 않음 → 블록을 비우면 원래 색. OnDisable·라운드 리셋에도 복귀.
  - 효과음: 펀치 명중, 가드로 막힘, 패리, 가드 깨짐, 맞딜, 런지 발동(`ThrusterLunge.OnLungeStarted`), 태클 명중(대시 어택·차징 태클 Hit). `AudioClip` 필드만, 비어 있으면 무음. 2D 재생(화면 분할에서 양쪽 동일).
- `StatusPopup`: 레거시 `TextMesh`(기본 폰트, 패키지 추가 없음) 월드 텍스트. 위로 떠오르며 사라짐(실제 시간 기준 → 히트스톱 중에도 보임).
  - 화면 분할 대응: 카메라마다 렌더 직전(`RenderPipelineManager.beginCameraRendering`, 내장 파이프라인은 `Camera.onPreCull`)에 그 카메라를 향하도록 회전 → 한 오브젝트가 양쪽 화면에서 모두 정면으로 보인다. 거리 비례 크기 보정.

## C3. 로케테스트 빌드 준비

- `PlayLogger` 저장 경로: 에디터 = 프로젝트 루트 `Logs/`, 빌드 = 실행 파일 옆 `Logs/`. 폴더 없으면 생성. (루트 `Logs/`는 이미 .gitignore 대상)
- 신규 `Core/AppRuntime.cs` (GameManager 자동 부착):
  - ESC 1초 길게 누르기 → 종료 (빌드만, 에디터는 플레이 중지). 운영자 키라 패드 페어링과 무관하게 전용 `InputAction`(`<Keyboard>/escape`, hold 1초)으로 읽는다 — `Keyboard.current` 직접 읽기 아님.
  - 빌드: 커서 숨김·잠금, 시작 시 전체 화면(모니터 기본 해상도, FullScreenWindow).
- `docs/BUILD.md`: Windows 빌드 설정 체크리스트(Build Profiles, 씬, Player Settings 해상도·전체 화면, 입력 시스템, 아키텍처) + 빌드 후 확인 항목.
