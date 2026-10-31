# Claude Code에 붙여넣을 명령

## 1차 명령 (Phase A: 기존 코드 수정)

```
이 저장소는 Unity 6 메카 격투 게임 "중장기갑 파트라슈"(mydongari)야. 3주 뒤 로케테스트(한 PC, 패드 2개, 화면 분할, 버나드 표준형 미러전, 3판 2선승)를 위해 코드를 고칠 거야.

먼저 CLAUDE.md, docs/GAME_SPEC.md, docs/TASKS.md를 끝까지 읽어. 그다음 Assets/Scripts 아래 스크립트를 전부 읽고 현재 구조를 파악해.

작업 시작 전에 이렇게 해:
1. 브랜치 loketest를 만들거나 체크아웃해. push는 하지 마.
2. TASKS.md Phase A의 각 태스크에 대해, 어떤 파일의 어떤 부분을 어떻게 바꿀지 짧은 계획을 docs/PLAN_A.md에 써. 바꾸는 public 멤버와 그걸 참조하는 곳도 적어.

그다음 Phase A를 A0부터 A8까지 순서대로 처리해:
- 태스크 하나 끝날 때마다 커밋해. 커밋 메시지는 "A0: 요약" 형식, 한국어.
- 이 환경에선 Unity를 실행할 수 없으니, 바꾼 멤버를 참조하는 곳을 grep으로 전부 찾아 같이 고쳐서 컴파일 에러가 남지 않게 해.
- 태스크마다 docs/TEST_CHECKLIST.md에 내가 에디터에서 확인할 항목을, docs/SETUP.md에 인스펙터에서 새로 연결할 참조를 추가해.
- 스펙이 애매하면 멈추지 말고 보수적인 기본값으로 구현한 뒤 docs/QUESTIONS.md에 질문과 고른 기본값을 적어.
- .unity/.prefab 파일은 손으로 편집하지 마.
- TASKS.md 체크박스를 체크하고 "작업 메모"에 바뀐 파일과 남은 위험을 한두 줄 남겨.

Phase A가 다 끝나면 멈추고 다음을 보고해:
- 태스크별로 한 일 한 줄씩
- 컴파일이 깨질 수 있는 위험 지점
- 내가 에디터에서 해야 할 일 (SETUP.md 요약)
- QUESTIONS.md에 쌓인 질문
Phase B는 내가 확인한 다음에 시작할 거야.
```

## 2차 명령 (Phase B: 새로 만드는 것) — Phase A를 에디터에서 확인한 뒤

```
Phase A는 에디터에서 확인했어. (여기에 발견한 문제나 QUESTIONS.md 답변을 적는다)

이제 TASKS.md Phase B를 B1부터 순서대로 처리해. 규칙은 Phase A 때와 같아:
- 시작 전에 docs/PLAN_B.md에 계획을 써.
- 태스크마다 커밋("B1: 요약"), TEST_CHECKLIST.md, SETUP.md, QUESTIONS.md 갱신.
- 씬 구성은 Assets/Editor/VersusSceneSetup.cs 메뉴로 하고 .unity를 직접 편집하지 마.
- B7(히트스톱)은 B1~B6이 끝나고 여유가 있을 때만.

끝나면 Phase A 때와 같은 형식으로 보고해.
```

## 중간에 끊겼을 때

```
CLAUDE.md와 docs/TASKS.md를 다시 읽고, 체크 안 된 첫 태스크부터 이어서 해. git log로 마지막 커밋을 확인해서 반쯤 된 작업이 있으면 그걸 먼저 마무리해.
```
