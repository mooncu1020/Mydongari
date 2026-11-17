# 최종 빌드 체크리스트 (10/27 화 확정 → 10/29 목 로케테스트)

10/27에 이 목록을 위에서부터 끝까지 확인하고 빌드를 확정한다. **확정 이후엔 버그 수정만** 한다(기능·수치 변경 없음).
세부 빌드 설정은 `docs/BUILD.md`, 에디터 세팅 단계는 `docs/SETUP.md`.

---

## 1. 저장소

- [ ] `loketest` 브랜치, 작업 폴더 깨끗함(`git status`에 커밋 안 된 씬·에셋·`.meta` 없음)
- [ ] Unity를 열었을 때 Console에 컴파일 오류 0개
- [ ] 새로 생긴 `.meta`가 전부 커밋됨
- [ ] `Assets/` 안에 예전 로그 CSV(`playlog_*`, `events_*`, `summary_*`)가 남아 있지 않음

## 2. 씬 세팅 (메뉴 실행 순서)

- [ ] `Mydongari/Setup Versus Scene` — `Mech_P2`, `Canvas_P2`, 화면 분할 (이미 했으면 건너뜀)
- [ ] `Mydongari/Add Missing Mech Components` — 기체: ThrusterLunge·MechTuning / GameManager: HitStop·CombatFeedback·AppRuntime
- [ ] `Mydongari/Tuning/Create Default Tuning And Assign` — 세 기체 모두 같은 `BernardTuning` 연결
- [ ] 외형: `Mydongari/Visual/Create Placeholder Parts` 또는 모델이 왔으면 `Mydongari/Visual/Apply Model To Mechs` (`docs/MODEL_IMPORT.md`)
- [ ] 스폰 포인트 두 개가 서로 마주 봄
- [ ] `GameManager`: `Show Title On Start` 켬, `Rounds To Win` 2, `Idle Return To Title`은 현장 운영 방식에 맞게(전시면 켬, 진행자가 있으면 끔)
- [ ] `CombatFeedback` 효과음 클립 연결 상태 확인 (비어 있으면 무음 — 의도인지)
- [ ] 씬 저장 후 커밋

## 3. 튜닝 값

- [ ] `Assets/Resources/BernardTuning.asset`을 `git diff`로 마지막 확인 커밋과 비교 — **플레이 중 바꾼 값이 에셋에 남아 있을 수 있다**
- [ ] 런지(거리 8·속도 30), 펀치(피해 25), 가드 감쇄(0.5), 패리 창(0.15), 히트스톱, 외형(Visual) 값이 의도한 값
- [ ] `MeleeDummy`(DummyTarget) 봇 수치(Parrier 확률 등) 확인

## 4. Player Settings / 빌드 설정 (`docs/BUILD.md` 2~4장)

- [ ] Windows, Intel 64-bit, Scene List = SampleScene 하나, **Development Build 끔**
- [ ] Fullscreen Window, Default Is Native Resolution
- [ ] **Run In Background 켬**, **Force Single Instance 켬**
- [ ] Active Input Handling = Input System Package (New), Input System `Background Behavior = Ignore Focus`
- [ ] Company / Product Name, Version(예: 1.0.0-loke)
- [ ] 출력 폴더가 쓰기 가능한 곳(Program Files 아님)

## 5. 빌드 실행 확인 (가능하면 로케 PC 또는 같은 사양)

**패드 2개**
- [ ] 실행 → 전체 화면, 커서 안 보임, 타이틀 DRAFT HOUND PATRASCHE
- [ ] 패드 1 = 왼쪽(P1), 패드 2 = 오른쪽(P2), 서로 간섭 없음
- [ ] 플레이 중 패드 하나를 뽑았다 꽂아도 같은 플레이어로 복귀

**VERSUS 완주**
- [ ] 타이틀 → VERSUS → 카운트다운 → FIGHT → **3판 2선승을 끝까지** (무승부 라운드가 나와도 진행)
- [ ] 매치 종료 → REMATCH로 한 판 더 → 다시 끝까지
- [ ] 매치 종료 → TITLE → 타이틀로 돌아옴

**PRACTICE**
- [ ] 타이틀 → PRACTICE → 봇 모드 순환(Select/Tab), 리셋(Select 길게/Backspace), Start → TITLE로 복귀

**운영**
- [ ] ESC 1초 → 종료 진행 표시 후 종료, 짧게 누르면 아무 일 없음
- [ ] Alt+Tab 다녀와도 입력·커서 정상, 게임이 멈추지 않음
- [ ] (켰다면) 2분 무입력 → 타이틀 복귀
- [ ] 30분 이상 연속 매치 → 프레임·메모리 이상 없음

**로그**
- [ ] 실행 파일 옆 `Logs/PlayLogs/`에 `playlog_*`, `events_*`, `summary_*` 생성, 매치마다 요약 추가
- [ ] `Logs/PlayLogs/errors.txt`가 **없거나 비어 있음** (있으면 내용 확인 후 수정 여부 판단)

## 6. 확정

- [ ] 위 항목 통과 후 빌드 폴더를 압축해 백업 (`Mydongari_loke_YYYYMMDD.zip`)
- [ ] 확정 커밋에 태그: `git tag loke-final-1027` (push 여부는 팀 규칙대로)
- [ ] 현장 PC에 복사 후 실행 확인, 패드 2개 배정 확인

## 7. 확정 후 버그 수정 절차 (10/27 ~ 10/29)

1. 재현 방법과 `errors.txt`·`Player.log`(`%USERPROFILE%\AppData\LocalLow\<Company>\Mydongari\Player.log`)를 남긴다.
2. 수정은 최소 범위로, 커밋 메시지 `fix: …`. 수치·기능 변경은 하지 않는다.
3. 다시 빌드하고 **5장 전체**를 다시 확인한 뒤 새 압축본·태그(`loke-final-1028` 등)로 교체.
