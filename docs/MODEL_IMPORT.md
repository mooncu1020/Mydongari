# 기체 모델 가져오기 (Blender → Unity)

공격·방어·피격 동작은 코드(`MechVisualRig`)가 **부품 단위로** 움직이고, 반복 동작(Idle/Walk/Run/Sprint)은 키프레임 클립으로 넣을 수 있다.
두 가지 방식을 지원한다.

- **A. 리깅 방식 (권장, E5)** — 아머처의 뼈 이름 = 슬롯 이름, 각 메시는 해당 뼈에 본 페어런트(웨이트 없음), Unity Rig = Generic. → **8장**
- **B. 부품 방식** — 아머처 없이 관절마다 따로 떨어진 메시 오브젝트 10개를 정해진 이름과 부모 관계로. → 2~6장

1·3장(크기, 관절 위치)은 두 방식 공통이다.

---

## 1. 크기 기준 (현재 콜라이더)

피격·충돌은 기체 루트의 **캡슐 콜라이더**가 맡는다. 모델은 보이기만 하므로 이 캡슐에 대략 맞춘다.

| 항목 | 값 |
| --- | --- |
| 캡슐 높이 | **2.0 m** (발바닥 ~ 머리 꼭대기) |
| 캡슐 폭 | **1.0 m** (반지름 0.5) |
| 캡슐 중심 | 기체 루트 위치 (= 발바닥에서 1.0 m 위) |
| 정면 | Unity **+Z** (파란 화살표) |

- 팔·주먹은 캡슐 밖으로 나와도 된다(펀치 사거리 3 m, 판정은 코드 수치). 다만 몸통 폭이 1 m를 크게 넘으면 겉보기와 피격 범위가 어긋난다.
- 모델 **원점(Origin)은 두 발 사이 바닥**에 둔다. 교체 메뉴가 모델을 캡슐 바닥(루트 기준 y = −1)에 맞춰 놓는다.

## 2. 부품 이름과 부모 관계

이름은 **정확히** 아래와 같아야 한다(대소문자 포함). 메뉴가 이 이름으로 찾는다.

```
(모델 루트, 원점 = 발바닥)
├─ Leg_L            왼다리 (엉덩이 관절)
├─ Leg_R            오른다리
└─ Body             몸통 (허리)
   ├─ Head          머리 (목)
   ├─ Arm_L_Upper   왼 위팔 (어깨)
   │  └─ Arm_L_Lower    왼 아래팔 (팔꿈치)
   │     └─ Fist_L      왼 주먹 (손목)
   └─ Arm_R_Upper   오른 위팔 (어깨)
      └─ Arm_R_Lower    오른 아래팔 (팔꿈치)
         └─ Fist_R      오른 주먹 (손목)
```

- **부모 관계가 중요하다**: 아래팔이 위팔의 자식이어야 어깨를 돌릴 때 팔 전체가 따라온다. 평평하게(전부 루트 자식) 두면 각 부품이 따로 논다.
- 이 10개 말고 장식 부품(어깨 장갑, 안테나 등)은 붙어 다닐 부품의 **자식**으로 넣으면 같이 움직인다. 이름은 자유.
- 한 부품에 메시가 여러 개면 빈 오브젝트(Empty)를 그 이름으로 만들고 메시들을 자식으로.

L = 기체 기준 왼쪽(Unity −X), R = 오른쪽(+X).

## 3. 피벗(원점) 규칙

각 부품의 **원점이 관절 위치**에 있어야 그 자리를 축으로 돈다. Blender에서 부품 선택 → 3D 커서를 관절에 두고 `Object > Set Origin > Origin to 3D Cursor`.

| 부품 | 피벗 위치 | 임시 상자 기준 위치(발바닥 = 0) |
| --- | --- | --- |
| Leg_L / Leg_R | 엉덩이 관절 | 높이 0.8 m, 좌우 ±0.22 m |
| Body | 허리(다리 위 가운데) | 높이 0.8 m |
| Head | 목 | 높이 1.6 m |
| Arm_?_Upper | 어깨 | 높이 1.52 m, 좌우 ±0.52 m |
| Arm_?_Lower | 팔꿈치 | 위팔 길이 0.42 m 아래 |
| Fist_? | 손목 | 아래팔 길이 0.38 m 끝 |

## 4. 축 방향과 쉴 때 자세

- 코드의 동작 각도는 **기준 축(X = 오른쪽, Y = 위, Z = 앞)** 으로 정의돼 있다. 부품의 로컬 축이 쉴 때 이 방향이면 보정 없이 맞는다(Blender에서 각 부품의 회전을 Apply해 두면 대개 그렇다).
- 모델링한 그 자세가 **쉴 때 자세(rest)** 가 된다. 동작은 그 위에 더해진다. 임시 상자는 "위팔 살짝 앞, 아래팔을 앞으로 굽힌 복서 자세" — 비슷하게 잡으면 기본 각도 수치가 그대로 맞는다.
- E5부터 코드는 **처음 자세에서 각 부품(뼈)의 방향을 재서** 동작 각도를 기체 축 기준으로 바꿔 넣는다. 그래서 부품·뼈의 로컬 축이나 롤이 달라도 같은 각도는 같은 방향으로 움직인다.
- 그래도 방향이 어긋나는 부품만 Unity에서 기체 → `MechVisualRig` → 해당 슬롯 → **Rotation Correction**(°)으로 추가 보정한다.

## 5. Blender 내보내기 설정 (File > Export > FBX)

| 항목 | 값 |
| --- | --- |
| Limit to | Selected Objects (기체 부품만 선택) |
| Object Types | Mesh, Empty |
| Scale | 1.0, **Apply Scalings: FBX Units Scale** (Blender 1단위 = 1 m) |
| Forward | **-Z Forward** |
| Up | **Y Up** |
| Apply Unit | 켬 |
| **Apply Transform** | 켬 |
| Armature / Bake Animation | 부품 방식(B)은 끔. 리깅 방식(A)은 8장 |

Blender 장면 단위는 Metric, Unit Scale 1.0. 내보내기 전에 모든 부품 `Ctrl+A > All Transforms` 하지 말 것 — **피벗(원점)이 관절 위치에서 움직이면 안 된다.** 회전·크기만 Apply(`Rotation & Scale`).

## 6. Unity로 가져오기

1. FBX를 `Assets/Models/` 아래에 복사.
2. FBX 선택 → Inspector → Model 탭: `Scale Factor` 1, `Convert Units` 켬, `Import Cameras/Lights` 끔. Rig 탭: `Animation Type = None`. Animation 탭: `Import Animation` 끔. Materials는 원하는 대로.
3. FBX를 씬에 잠깐 끌어 놓고 **정면이 +Z(파란 화살표)** 인지, 키가 2 m 근처인지 확인 후 지운다. 뒤를 보면 Blender에서 180° 돌려 다시 내보낸다.
4. 기체를 선택(두 기체·더미 모두 같은 모델이면 아무것도 선택하지 않음) → 메뉴 **`Mydongari/Visual/Apply Model To Mechs`** → FBX 선택.
   - 기체 아래에 `VisualModel`로 들어가고, 부품 10개를 찾아 리그에 연결, 임시 상자(`VisualPlaceholder`)는 꺼진다. 모델 안 콜라이더는 끈다.
   - 못 찾은 이름은 다이얼로그에 목록으로 나온다(그 부품은 안 움직임). 이름을 고쳐 다시 내보낸 뒤 메뉴를 다시 실행하면 이전 `VisualModel`을 지우고 새로 붙인다.
   - Ctrl+Z 한 번으로 되돌린다(임시 상자 다시 보임).
5. 플레이해서 펀치·가드·걷기를 보고, 방향이 이상한 부품은 Rotation Correction, 동작 크기는 `BernardTuning → Visual`로 조정. 저장.

## 7. 자주 생기는 문제

| 증상 | 원인 / 해결 |
| --- | --- |
| 부품이 엉뚱한 점을 축으로 돈다 | 피벗이 관절에 없음 → 3장 |
| 팔을 돌리면 아래팔·주먹이 안 따라온다 | 부모 관계가 평평함 → 2장 |
| 모델이 100배 크거나 작다 | Apply Scalings / Convert Units 설정 → 5·6장 |
| 모델이 누워 있다(90° 회전) | Forward/Up 축 또는 Apply Transform 누락 → 5장 |
| 모델이 땅에 묻히거나 뜬다 | 원점이 발바닥에 없음 → 1장 |
| 색 틴트(가드 파랑 등)가 안 보인다 | 머티리얼 셰이더에 `_BaseColor`/`_Color`가 없음 → URP Lit 또는 기존 툰 셰이더 사용 |

## 8. 리깅 방식 (아머처 + 본 페어런트, Generic)

공격·방어·피격은 코드가, 반복 동작(Idle/Walk/Run/Sprint)은 키프레임 클립이 맡는다. 코드는 매 프레임 **Animator가 만든 자세 위에** 공격·방어 동작을 덧씌운다.

### 8-1. 아머처

- 뼈 이름 = 슬롯 이름 **정확히**: `Body`, `Head`, `Arm_L_Upper`, `Arm_L_Lower`, `Fist_L`, `Arm_R_Upper`, `Arm_R_Lower`, `Fist_R`, `Leg_L`, `Leg_R`. 그 밖의 뼈(루트, 장식용)는 이름 자유.
- 뼈 계층은 2장과 같다: `Leg_L`·`Leg_R`·`Body`는 루트 뼈(예: `Root`, 발바닥 원점) 아래, `Head`·양 위팔은 `Body` 아래, 아래팔은 위팔 아래, 주먹은 아래팔 아래. 부모 뼈와 연결(Connected)은 해도 안 해도 된다.
- **뼈 시작점(Head) = 관절 위치** (3장 표): `Body` = 허리, `Head` = 목, `Arm_?_Upper` = 어깨, `Arm_?_Lower` = 팔꿈치, `Fist_?` = 손목, `Leg_?` = 엉덩이. 코드는 뼈 시작점을 축으로 돌린다.
- **뼈 방향(Tail)**: 다음 관절 쪽으로 — 위팔은 어깨→팔꿈치, 아래팔은 팔꿈치→손목, 주먹은 손목→주먹 끝, 다리는 엉덩이→발, 몸통은 허리→목(위), 머리는 목→정수리(위). **Roll은 0**으로 통일. (코드는 처음 자세의 뼈 방향을 재서 맞추므로 방향·롤이 조금 달라도 동작은 하지만, 이 규칙을 지키면 키프레임 작업과 코드 동작이 같은 축을 쓴다.)
- 처음 자세(Rest Pose)가 쉴 때 자세다. 4장처럼 복서 자세로 잡으면 기본 동작 각도가 잘 맞는다.

### 8-2. 메시 연결

- 각 부품 메시는 해당 뼈에 **본 페어런트**: 메시 선택 → 아머처 Pose Mode에서 뼈 선택 → `Ctrl+P > Bone`. **웨이트(Armature Deform)는 쓰지 않는다.**
- 메시 이름은 자유(`Body_Mesh` 등). 뼈와 같은 이름이어도 교체 메뉴는 **메시가 없는 Transform(뼈)을 먼저** 고른다.

### 8-3. 반복 동작 클립 (Blender Action)

- Action 이름 = 상태 이름 **정확히**: `Idle`, `Walk`, `Run`, `Sprint`. 각각 반복(루프)되게 첫 프레임 = 끝 프레임 자세로.
- 다리 교차·몸통 상하 흔들림·숨쉬기 같은 **반복 동작만** 넣는다. 펀치·가드·런지·피격 동작은 넣지 않는다(코드가 함).
- 이동하면서 기체가 앞으로 나가는 루트 이동은 넣지 않는다(제자리 걸음). 위치는 물리가 움직인다.
- 상태와 속도 기준: Walk = 걷기(최대 5 m/s), Run = 달리기(최대 10 m/s), Sprint = 전력질주(최대 20 m/s). 클립 재생 속도는 Unity에서 맞춘다.
- 넣지 않은 상태가 있어도 된다 — 그 상태에선 코드 걷기 동작(다리 교차·상하 흔들림)이 계속 대신한다.

### 8-4. FBX 내보내기 (리깅 방식)

5장 표에서 아래만 다르다.

| 항목 | 값 |
| --- | --- |
| Object Types | **Armature**, Mesh, Empty |
| Armature > Primary / Secondary Bone Axis | 기본값 (Y / X) |
| Armature > **Only Deform Bones** | **끔** (본 페어런트만 쓰므로 변형 뼈가 없다 — 켜면 뼈가 빠진다) |
| Armature > **Add Leaf Bones** | **끔** |
| **Bake Animation** | **켬** (Actions 켬, NLA Strips 끔, All Actions 켬) |
| Bake Animation > Simplify | 0 (반복 동작이 어긋나지 않게) |
| Apply Transform | 켬 (5장과 같음) |

### 8-5. Unity 가져오기 (리깅 방식)

1. FBX 선택 → **Rig 탭: Animation Type = Generic**, Avatar Definition = Create From This Model, Root node = 루트 뼈(예: `Root`), **Optimize Game Objects = 끔** (켜면 뼈 Transform이 숨겨져 코드가 못 찾는다). Apply.
2. **Animation 탭**: `Import Animation` 켬. 클립 목록에서 각 클립 이름을 `Idle`/`Walk`/`Run`/`Sprint`로 맞추고(Blender에선 `Armature|Walk`처럼 들어올 수 있음) **Loop Time 켬**, Root Transform 위치·회전 `Bake Into Pose` 켬. Apply.
3. 메뉴 **`Mydongari/Visual/Apply Model To Mechs`** → FBX 선택. 뼈 10개를 슬롯에 연결하고, 모델의 Animator를 리그에 연결한다(루트 모션 끔). 컨트롤러가 이미 있으면 같이 붙인다.
4. 메뉴 **`Mydongari/Visual/Create Locomotion Animator Controller`** → `Assets/Animations/MechLocomotion.controller` 생성(있으면 재사용) + 씬의 모델 Animator 전부에 연결.
   - 파라미터: `Speed`(수평 속도 m/s), `MoveState`(0 Idle, 1 Walk, 2 Run, 3 Sprint — 기체 상태에서 코드가 넣음. 제동·슬라이딩·사이드스텝·퀵턴·런지 중엔 직전 상태 유지, 연습 봇은 속도로만 0/1).
   - 상태 Idle/Walk/Run/Sprint의 **Motion 칸은 비어 있다** → 컨트롤러를 열어(Animator 창) 각 상태에 2번의 클립을 끌어 넣는다. 필요하면 상태의 Speed에 `Speed` 파라미터를 Multiplier로 연결해 걸음 속도를 맞춘다.
   - 비어 있는 상태는 자세 변화 없이(쉴 때 자세) 지나가고, 그 상태에선 코드 걷기 동작이 대신한다.
5. 플레이해서 확인: 키프레임 반복 동작 위에 펀치·가드 등 코드 동작이 겹쳐 나오면 정상.

### 8-6. 리깅 방식 문제 해결

| 증상 | 원인 / 해결 |
| --- | --- |
| "찾지 못한 부품"에 뼈 이름이 다 나온다 | Optimize Game Objects가 켜짐 / Only Deform Bones가 켜져 뼈가 빠짐 / 뼈 이름 오타 |
| 뼈 이름 끝에 `_end`가 붙은 뼈가 생긴다 | Add Leaf Bones 끄기 |
| 키프레임 동작이 안 나온다 | 컨트롤러 상태의 Motion이 비어 있음 / 클립 Loop Time 꺼짐 / 모델 Animator에 컨트롤러 없음(4번 메뉴) |
| 걸을 때 다리가 두 번 흔들린다 | 상태에 클립이 들어 있는데 코드 걷기도 도는 경우는 없어야 함 — 클립이 실제로 재생 중인지(Animator 창) 확인 |
| 펀치가 엉뚱한 방향으로 뻗는다 | 뼈 시작점이 관절에 없음(8-1) → 그래도 어긋나면 슬롯 Rotation Correction |
| 모델이 기체에서 미끄러져 나간다 | 클립에 루트 이동이 들어감 → Bake Into Pose 켜기, 루트 모션은 코드가 끔 |
