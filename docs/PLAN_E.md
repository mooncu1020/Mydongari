# Phase E 작업 계획

일정: 10/27(화) 최종 빌드 확정, 10/29(목) 로케테스트. 확정 뒤엔 버그 수정만.
현재 기체 외형: 루트의 Unity 기본 캡슐(MeshRenderer, 반지름 0.5 · 높이 2, 중심 = 루트, 발바닥 y = −1). 콜라이더도 같은 캡슐.

---

## E1. 절차적 애니메이션 (`MechVisualRig`)

- 신규 `Visual/MechVisualRig.cs` (`MyDongari.Visual`): 기체 루트에 붙고, 시각 전용 부품 10개 슬롯(Body, Head, Arm_L_Upper/Lower, Fist_L, Arm_R_Upper/Lower, Fist_R, Leg_L, Leg_R)을 LateUpdate에서 움직인다.
  - 슬롯 = 관절 위치의 Transform. 쉴 때 자세(rest)를 기억하고 그 위에 오프셋(회전, 몸통만 위치)을 얹는다. 슬롯마다 회전 보정 값(E2 모델 축 대응).
  - 모든 움직임은 감쇠 스프링(주파수·감쇠비)으로 목표 오프셋을 따라간다. 튕김(패리·가드 깨짐)은 스프링에 각속도 충격.
  - 콜라이더·리지드바디는 루트에 그대로. 부품엔 콜라이더 없음.
- 입력(전투 코드는 리그를 모름): `MeleeSystem.OnAttackStarted`(펀치 좌우 교대·대시 어택·태클), `OnHitResolved`(패리·가드 깨짐), `IsGuarding`·`IsStunned`, `ThrusterLunge.IsLunging`, `AnchorClaw.OnGrabbed`, 리지드바디 속도(기울기·걷기).
  - 이벤트가 없던 두 곳에 일반 이벤트 추가: `AnchorClaw.OnPileDriverStarted`, `ImpactSlam.OnSlamStarted` (헛쳐도 발생, 로그 이벤트와 같은 방식).
- 예비 동작: 공격 이벤트 직후 `anticipationTime`(기본 2프레임) 동안 팔을 뒤로 → 뻗기. **판정은 이벤트 프레임에 이미 끝나 있으므로 판정 타이밍은 그대로**(QUESTIONS에 한계 기록).
- 수치: `BernardTuning`에 `visual` 묶음 하나(스프링, 예비 동작, 펀치, 가드, 패리, 런지, 태클, 클로, 파일드라이버·슬램, 스턴, 가드 깨짐, 이동).
- 신규 `Editor/VisualRigSetup.cs`: 메뉴 `Mydongari/Visual/Create Placeholder Parts` — 씬의 모든 기체(MeleeSystem 있는 오브젝트)에 `VisualPlaceholder` 상자 계층 생성(캡슐 크기에 맞춤, 기체 머티리얼 사용, 콜라이더 제거), 루트 캡슐 렌더러 끄기, `MechVisualRig` 추가·연결. Undo 한 번.
- 과열 김 파티클(선택)은 시간 남으면.

## E2. 모델 교체 메뉴

- `Mydongari/Visual/Apply Model To Selected Mech`: FBX(모델 에셋) 선택 창 → 선택한 기체 아래 `VisualModel`로 인스턴스, 이름으로 부품 10개를 찾아 리그 슬롯에 연결, `VisualPlaceholder` 끔. 못 찾은 이름은 다이얼로그로 목록 경고(그 슬롯은 비움 → 그 부품만 안 움직임).
- `docs/MODEL_IMPORT.md`: Blender 내보내기 설정, 콜라이더 크기, 부품 이름·계층, 피벗 규칙, 회전 보정 사용법.

## E3. 안정화

- 신규 `Core/SafeRun.cs`: 예외를 잡아 `Logs/PlayLogs/errors.txt`에 시각·위치·스택을 남기는 공용 도우미(같은 예외 반복은 횟수만). PlayLogger·CombatFeedback·StatusPopup·MechTint·MechVisualRig·HitStop의 Update/이벤트 처리를 감싼다.
- `Application.logMessageReceived`로 그 밖의 처리 안 된 예외도 같은 파일에.
- GameManager `idleReturnToTitle`(기본 끔) + `idleTimeout` 120초: 아무 플레이어 입력이 없으면 타이틀로.
- `docs/RELEASE_CHECKLIST.md`: 10/27 최종 빌드 전 확인 항목.
