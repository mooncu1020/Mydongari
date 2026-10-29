using System;
using UnityEngine;

namespace MyDongari.Mech
{
    /// <summary>
    /// 버나드 표준형의 조정 가능한 전투·이동 수치 전부. 두 기체가 같은 에셋을 참조한다(미러전).
    /// 컴포넌트들은 값을 캐시하지 않고 쓸 때마다 여기서 읽으므로, 플레이 중 에셋을 고치면 바로 반영된다.
    /// 주의: 플레이 중 고친 에셋 값은 플레이를 멈춰도 그대로 남는다(컴포넌트 값과 다름).
    /// </summary>
    [CreateAssetMenu(menuName = "Mydongari/Bernard Tuning", fileName = "BernardTuning")]
    public class BernardTuning : ScriptableObject
    {
        /// <summary>Resources 폴더 기준 기본 에셋 이름. 기체에 연결이 안 돼 있으면 이걸 찾는다.</summary>
        public const string ResourcesName = "BernardTuning";

        [Serializable]
        public class MovementValues
        {
            [Header("가속 (AddForce, m/s²)")]
            public float walkSpeed = 10f;
            public float runSpeed = 20f;
            public float sprintSpeed = 35f;

            [Header("최대 속도 (m/s)")]
            public float maxWalkSpeed = 5f;
            public float maxRunSpeed = 10f;
            public float maxSprintSpeed = 20f;

            [Header("회전")]
            public float rotationSpeed = 15f;
            [Tooltip("하드 락온 중 몸통이 상대 쪽으로 자동으로 도는 속도 (수평 회전만)")]
            public float hardLockTurnSpeed = 10f;

            [Header("무게감 (관성 보정)")]
            [Tooltip("0 = 관성 없음, 1 = 방향 반대로 꺾을 때 추진력 최대 90%까지 깎임")]
            public float turnResistance = 0.6f;
            [Tooltip("정지 상태에서 출발할 때 목표 가속도까지 도달하는 데 걸리는 시간(초). 0이면 즉시 풀파워")]
            public float spinUpTime = 0.25f;

            [Header("사이드스텝")]
            public float sideStepDuration = 0.3f;
            public float sideStepSpeed = 20f;

            [Header("슬라이딩")]
            [Tooltip("미끄러지는 시간(초). 이 동안 상대의 락온이 끊긴다")]
            public float slideDuration = 0.8f;
            public float slideSpeed = 25f;

            [Header("초신지선회 / 제동")]
            public float quickTurnDuration = 0.15f;
            [Tooltip("전력질주를 놓은 뒤 제동 상태 시간(초)")]
            public float brakeDuration = 1f;
            [Tooltip("제동 시작 후 이 시간(초) 안에 슬라이딩 입력이 들어오면 슬라이딩 (키보드 Ctrl 경로)")]
            public float brakeSlideWindow = 0.5f;

            [Header("전력질주 (패드 두 스틱: 왼쪽 = 방향, 오른쪽 = 가속)")]
            [Tooltip("오른쪽 스틱 크기가 이 값 미만이면 중립")]
            public float sprintStickNeutral = 0.3f;
            [Tooltip("두 스틱 크기가 이 값 이상이어야 전력질주 진입 (왼쪽은 달리기 80% 경계와 같게 둔다)")]
            public float sprintStickThreshold = 0.8f;
            [Tooltip("두 스틱 크기가 이 값 이상이어야 전력질주 유지. 진입 기준과 따로 조정한다 (이 값 미만 = 스틱 크기 미달, 중립 미만 = 스틱 놓음)")]
            public float sprintHoldStickThreshold = 0.8f;
            [Tooltip("해제 조건(각도 초과·스틱 크기 미달·놓음)이 이 시간(초) 이상 계속돼야 해제. 순간적인 흔들림 무시. 0이면 즉시")]
            public float sprintExitConfirmTime = 0.1f;
            [Tooltip("오른쪽 스틱이 중립을 벗어난 뒤 이 시간(초) 안에 임계값까지 꺾여야 진입")]
            public float sprintEntryTime = 0.15f;
            [Tooltip("진입 순간 두 스틱 방향 차이가 이 각도(°) 이하여야 진입")]
            public float sprintEntryAngle = 20f;
            [Tooltip("유지 중 두 스틱 방향 차이가 이 각도(°)를 넘으면 해제")]
            public float sprintExitAngle = 20f;

            [Header("전력질주 카메라 (패드 오른쪽 스틱)")]
            [Tooltip("진입 판정 중·전력질주 중 카메라가 기체 진행 방향 뒤로 따라오는 빠르기 (클수록 빨리)")]
            public float sprintCameraFollowSpeed = 5f;
            [Tooltip("전력질주가 풀린 뒤 오른쪽 스틱 크기가 이 값 미만(중립)으로 한 번 돌아와야 카메라 회전 입력을 다시 받는다")]
            public float cameraLookResumeNeutral = 0.3f;

            [Header("슬라이딩 (패드 두 스틱)")]
            [Tooltip("전력질주가 스틱을 놓아서 풀린 뒤 이 시간(초) 안에 슬라이딩 입력을 받는다. 이 동안은 제동으로 안 넘어가고 대기")]
            public float slideInputWindow = 0.2f;
            [Tooltip("두 스틱 크기가 이 값 이상이어야 슬라이딩")]
            public float slideStickThreshold = 0.8f;
            [Tooltip("왼쪽 스틱 목표 각도(°, 오른쪽 = 0, 위 = 90). 45 = ↗")]
            public float slideLeftAngle = 45f;
            [Tooltip("오른쪽 스틱 목표 각도(°). 135 = ↖")]
            public float slideRightAngle = 135f;
            [Tooltip("목표 각도에서 이만큼(°) 벗어나도 인정")]
            public float slideAngleTolerance = 25f;
        }

        [Serializable]
        public class BoostValues
        {
            public float maxBoost = 100f;
            [Tooltip("걷기/정지로 바뀐 뒤 회복이 시작되기까지(초)")]
            public float recoveryDelay = 0.5f;
            [Tooltip("초당 회복량")]
            public float recoveryRate = 25f;
            [Tooltip("전력질주 초당 소모량")]
            public float sprintDrainRate = 5f;
            [Tooltip("탈진(0%) 후 걷기/정지로 회복해 최대치의 이 비율에 도달하면 탈진 해제")]
            [Range(0f, 1f)] public float exhaustRecoverRatio = 0.5f;

            [Header("소모량")]
            public float sideStepCost = 10f;
            public float quickTurnCost = 10f;
            public float slideCost = 10f;
        }

        [Serializable]
        public class HeatValues
        {
            public float maxHeat = 100f;
            [Tooltip("초당 냉각량")]
            public float coolingRate = 20f;
            [Tooltip("전력질주 중 냉각 속도 감소율. 0.7이면 냉각 속도가 평소의 30%로 떨어진다")]
            [Range(0f, 1f)] public float sprintCoolingReduction = 0.7f;
        }

        [Serializable]
        public class PunchValues
        {
            [Tooltip("펀치 사거리(m)")]
            public float range = 3f;
            [Tooltip("펀치 판정 부채꼴 반각(°). 기체 정면 기준 ±이 각도 안의 상대만 맞는다")]
            public float halfAngle = 45f;
            public float damage = 25f;
            public float cooldown = 0.5f;
            [Tooltip("펀치 한 방(발동 기준)마다 오르는 히트")]
            public float heatPerPunch = 6f;
            [Tooltip("맞딜: 양쪽 펀치가 거의 동시에 나갔을 때 서로 밀려나는 힘")]
            public float clashKnockback = 10f;
        }

        [Serializable]
        public class AttackStartupValues
        {
            [Tooltip("버튼을 누른 뒤 펀치 판정까지(초). 이 동안 예비 동작이 보인다. 0이면 누른 프레임에 즉시 판정")]
            public float punch = 0.1f;
            [Tooltip("대시 어택(질주 1.5초 미만 어깨 태클) 발생 시간(초). 0이면 즉시")]
            public float dashAttack = 0.12f;
            [Tooltip("차징 태클(질주 1.5초 이상) 발생 시간(초). 0이면 즉시")]
            public float chargedTackle = 0.1f;
            [Tooltip("양쪽 펀치의 판정 시점이 이 시간(초) 이내면 맞딜")]
            public float clashWindow = 0.1f;
            [Tooltip("공격 버튼 입력 버퍼(초). 쿨다운·발생 시간·런지·스턴 등으로 지금 못 나가는 공격은 이 시간 동안 기억했다가 가능해지는 순간 발동")]
            public float inputBuffer = 0.15f;
        }

        [Serializable]
        public class GuardValues
        {
            [Tooltip("가드로 받은 근접 계열(펀치·대시 어택·슬램) 피해 감쇄율. 0.5 = 절반만 들어감")]
            [Range(0f, 1f)] public float meleeReduction = 0.5f;
            [Tooltip("가드로 받은 투사체 피해 감쇄율 (사격 기체용)")]
            [Range(0f, 1f)] public float projectileReduction = 0.7f;
            [Tooltip("가드로 공격을 받을 때마다 오르는 히트")]
            public float heatPerBlock = 10f;
            [Tooltip("과열 상태에서 가드로 받으면 가드가 깨지고 이 시간(초) 동안 스턴")]
            public float breakStun = 0.9f;
            [Tooltip("가드를 내린 뒤 다시 올릴 수 있을 때까지 대기(초). 패리 성공 직후엔 면제")]
            public float reraiseDelay = 0.4f;
            [Tooltip("가드 중 리지드바디 감속. 방어 자세로 버티는 느낌")]
            public float damping = 8f;
            [Tooltip("가드를 내렸을 때 돌아갈 일반 이동 감속")]
            public float releaseDamping = 2f;
        }

        [Serializable]
        public class ParryValues
        {
            [Tooltip("가드를 올린 순간부터 이 시간(초) 안에 맞으면 패리")]
            public float window = 0.15f;
            [Tooltip("런지·태클·대시 어택을 패리하면 공격자가 그 공격 피해의 이 비율만큼 받는다")]
            public float reflectRatio = 1f;
            [Tooltip("반사 넉백 힘. 공격 자체에 넉백 값이 있으면 그 값을 쓰고, 없을 때만 이 값")]
            public float reflectKnockback = 8f;
        }

        [Serializable]
        public class TackleValues
        {
            [Tooltip("차징 태클 기본 피해. 차징이 길수록 최대 2배")]
            public float damage = 30f;
            [Tooltip("차징 태클 기본 넉백. 차징이 길수록 최대 2배")]
            public float knockback = 8f;
            [Tooltip("태클 판정 거리(m). 대시 어택 공통")]
            public float range = 5f;
            [Tooltip("태클 판정 부채꼴 반각(°). 대시 어택·자동 접촉 태클 공통")]
            public float halfAngle = 45f;
            [Tooltip("태클·대시 어택 재사용 대기(초)")]
            public float cooldown = 1f;
            [Tooltip("차징 태클을 그대로 맞은 상대의 스턴(초)")]
            public float victimStun = 1.7f;
            [Tooltip("태클을 부딪친 뒤(또는 헛친 뒤) 공격자 자신의 경직(초). 패리당했을 땐 적용 안 함")]
            public float recoveryStun = 0.5f;

            [Header("차징")]
            [Tooltip("전력질주를 이 시간(초) 이상 유지하면 차징 태클 (미만은 대시 어택)")]
            public float chargeStartTime = 1.5f;
            [Tooltip("전력질주를 이 시간(초) 유지하면 차징 최대 (피해·넉백 2배)")]
            public float fullChargeTime = 3f;
            [Tooltip("차징 상태로 이 거리(m) 안에 상대가 정면으로 닿으면 자동 태클")]
            public float contactRadius = 2f;
        }

        [Serializable]
        public class DashAttackValues
        {
            [Tooltip("가드에 막힌다(가드 규칙 적용). 차징 태클과 달리 관통·스턴 없음")]
            public float damage = 20f;
            public float knockback = 6f;
            [Tooltip("그대로 맞았을 때 상대 스턴(초). 0이면 없음")]
            public float stun = 0f;
        }

        [Serializable]
        public class LungeValues
        {
            [Header("돌진")]
            [Tooltip("돌진 거리(m)")]
            public float distance = 8f;
            [Tooltip("돌진 속도(m/s)")]
            public float speed = 30f;
            [Tooltip("돌진이 끝날 때 남기는 속도 비율. 0이면 딱 멈춤")]
            [Range(0f, 1f)] public float endSpeedRatio = 0.2f;
            [Tooltip("벽 등에 막혀 거리를 못 채울 때를 대비한 최대 돌진 시간 = (거리/속도) × 이 배수")]
            public float timeoutMultiplier = 1.5f;
            [Tooltip("발동 시 소모하는 부스트. 탈진이면 발동 안 함")]
            public float boostCost = 15f;

            [Header("명중")]
            [Tooltip("명중 피해 (펀치보다 약간 낮게)")]
            public float damage = 15f;
            [Tooltip("명중 시 상대 스턴(초). 후속 펀치 1회가 들어갈 틈")]
            public float hitStun = 0.5f;
            [Tooltip("명중 시 상대 넉백. 0이면 밀지 않음(후속 펀치 거리 유지)")]
            public float hitKnockback = 0f;
            [Tooltip("명중 판정 구의 반경(m)")]
            public float hitRadius = 1.2f;
            [Tooltip("명중 판정 구를 기체 앞쪽으로 내미는 거리(m)")]
            public float hitForwardOffset = 1f;

            [Header("빗나감")]
            [Tooltip("빗나간 뒤 이 시간(초) 동안 하드 락온이 다시 걸리지 않는다 (사이드스텝으로 피한 상대의 반격 틈)")]
            public float missLockDelay = 0.6f;
        }

        [Serializable]
        public class AnchorClawValues
        {
            public float pullSpeed = 15f;
            [Tooltip("최대 당김 시간(초)")]
            public float pullDuration = 1.5f;
            [Tooltip("상대가 이 거리(m) 안으로 끌려오면 당김 종료")]
            public float pullStopDistance = 3f;
            [Tooltip("쿨다운(초). 클로가 손을 떠난 순간(예비 동작 끝)부터 센다")]
            public float cooldown = 5f;

            [Header("발사 (투사체 — 유도 없음, 발사 순간 상대 위치로 일직선)")]
            [Tooltip("예비 동작(초): 팔을 뒤로 당기고 손끝이 빛난다. 이 동안 스턴되면 취소(쿨다운 안 씀)")]
            public float windupTime = 0.2f;
            [Tooltip("클로가 날아가는 속도(m/s)")]
            public float projectileSpeed = 40f;
            [Tooltip("사거리(m). 손에서 이만큼 날아가도 못 맞으면 빗나감")]
            public float range = 12f;
            [Tooltip("클로 판정 반지름(m). 이 굵기로 지나간 길을 훑는다")]
            public float hitRadius = 0.5f;
            [Tooltip("빗나간 뒤 와이어가 감겨 돌아오는 속도(m/s)")]
            public float retractSpeed = 40f;
            [Tooltip("외형 리그가 없는 기체의 발사 위치(기체 기준 로컬 m). 리그가 있으면 왼손 끝")]
            public Vector3 launchOffset = new Vector3(-0.55f, 0.6f, 0.5f);

            [Header("와이어·발광 표시")]
            public float wireWidth = 0.06f;
            public Color wireColor = new Color(0.75f, 0.75f, 0.8f);
            [Tooltip("날아가는 클로 머리 크기(m)")]
            public float clawHeadSize = 0.3f;
            [Tooltip("예비 동작 끝에 손끝 발광 구의 크기(m). 0에서 이 크기로 커진다")]
            public float glowSize = 0.45f;
            public Color glowColor = new Color(1f, 0.85f, 0.3f);

            [Header("훅 연출")]
            [Tooltip("그랩이 박히는 순간 확 끌려오는 초기 속도. pullSpeed보다 커야 '훅' 느낌이 남")]
            public float hookImpactSpeed = 32f;
            [Tooltip("초기 훅 속도에서 pullSpeed로 줄어드는 데 걸리는 시간(초)")]
            public float hookEaseTime = 0.2f;
            [Tooltip("그랩이 박히는 순간 상대에게 거는 짧은 경직(초)")]
            public float hookImpactStun = 0.25f;

            [Header("파일드라이버 (가드·패리 관통)")]
            public float pileDriverDamage = 50f;
            [Tooltip("이 거리(m) 안이어야 파일드라이버가 나간다")]
            public float pileDriverRange = 4f;
        }

        [Serializable]
        public class ImpactSlamValues
        {
            public float range = 8f;
            [Tooltip("전방 부채꼴 전체 각도(°)")]
            public float angle = 90f;
            public float damage = 60f;
            public float knockback = 15f;
            [Tooltip("넉백 방향의 위쪽 성분. 클수록 더 띄운다")]
            public float knockbackLift = 0.3f;
            public float cooldown = 8f;
        }

        [Serializable]
        public class LockOnValues
        {
            [Header("소프트 락온")]
            [Tooltip("카메라에서 상대까지 이 거리(m) 안이어야 소프트 락온")]
            public float softRange = 50f;
            [Tooltip("카메라 정면에서 상대 방향까지의 각도(°)가 이 값보다 작아야 소프트 락온 (반각)")]
            public float softAngle = 30f;

            [Header("하드 락온")]
            [Tooltip("기체 위치 기준. 이 거리(m) 안으로 들어오면 하드 락온 + 기본 공격이 펀치로 전환")]
            public float hardEnterRange = 6f;
            [Tooltip("기체 위치 기준. 이 거리(m) 밖으로 나가면 하드 락온 해제")]
            public float hardExitRange = 7f;
            [Tooltip("카메라 정면 기준 전체 각도(°). 120이면 ±60° 밖에서 해제")]
            public float hardMaxAngle = 120f;
            [Tooltip("하드 락온 중 카메라가 상대를 따라가는 속도")]
            public float hardTrackSpeed = 5f;
        }

        [Serializable]
        public class HitStopValues
        {
            public bool enabled = true;
            [Header("길이 (초, 실제 시간)")]
            public float punch = 0.05f;
            [Tooltip("런지·차징 태클·대시 어택")]
            public float lungeTackle = 0.08f;
            public float clash = 0.12f;
            [Tooltip("슬램·파일드라이버·투사체. 0이면 없음")]
            public float other = 0f;
        }

        [Serializable]
        public class VisualValues
        {
            [Header("스프링 (모든 부품 공통)")]
            [Tooltip("부품이 목표 자세를 따라가는 빠르기(Hz). 클수록 딱딱하고 빠르다")]
            public float springFrequency = 8f;
            [Tooltip("감쇠비. 1 = 출렁임 없이 멈춤, 작을수록 더 출렁인다")]
            [Range(0.05f, 1.5f)] public float springDamping = 0.6f;

            [Header("예비 동작 (버튼 → 판정 사이. 길이는 공격 발생 시간을 따른다)")]
            [Tooltip("발생 시간이 0(즉시 판정)인 공격에만 쓰는 예비 동작 길이(초). 0.033 ≈ 60fps 2프레임")]
            public float anticipationTime = 0.033f;
            [Tooltip("예비 동작 때 위팔을 뒤로 빼는 각도(°)")]
            public float anticipationPullback = 25f;

            [Header("펀치 (각도는 위팔·아래팔 X축, 음수 = 앞으로)")]
            public float punchExtendTime = 0.12f;
            public float punchHoldTime = 0.06f;
            public float punchArmAngle = -70f;
            [Tooltip("팔꿈치를 펴는 각도(쉬는 자세에서 굽혀 둔 만큼 +)")]
            public float punchElbowAngle = 75f;
            [Tooltip("때리는 쪽 어깨가 나가도록 몸통을 비트는 각도")]
            public float punchBodyTwist = 15f;
            [Tooltip("이 시간(초) 안에 다음 펀치가 나오면 반대 팔로 (원투)")]
            public float comboWindow = 0.8f;

            [Header("가드 / 패리")]
            public float guardArmAngle = -60f;
            public float guardElbowAngle = -40f;
            [Tooltip("가드 때 팔을 몸 안쪽으로 모으는 각도")]
            public float guardInward = 20f;
            [Tooltip("패리 성공 때 팔을 튕겨 올리는 순간 각속도(°/초)")]
            public float parryKick = 600f;

            [Header("런지")]
            public float lungeLean = 25f;
            public float lungeArmBack = 60f;

            [Header("대시 어택·태클 (어깨로 들이받기)")]
            [Tooltip("몸통 비틀기(음수 = 오른 어깨 앞으로)")]
            public float tackleTwist = -35f;
            public float tackleLean = 15f;
            public float tackleTime = 0.35f;

            [Header("앵커 클로 (왼팔 뻗기)")]
            public float clawArmAngle = -85f;
            public float clawHoldTime = 0.6f;

            [Header("파일드라이버·슬램 (내리찍기)")]
            [Tooltip("들어 올릴 때 위팔 각도(음수 = 앞·위로)")]
            public float smashRaiseAngle = -160f;
            public float smashDownAngle = -40f;
            public float smashRaiseTime = 0.1f;
            public float smashDownTime = 0.25f;
            public float smashLean = 20f;

            [Header("스턴 / 가드 깨짐")]
            public float stunShakeAngle = 6f;
            public float stunShakeFrequency = 9f;
            [Tooltip("가드 깨짐 때 팔이 바깥·뒤로 튕기는 순간 각속도(°/초)")]
            public float guardBreakFling = 700f;

            [Header("이동")]
            [Tooltip("속도 1m/s당 상체 기울기(°)")]
            public float leanPerSpeed = 1.2f;
            public float maxLean = 18f;
            [Tooltip("걷기 다리 흔들림 최대 각도(°)")]
            public float strideAngle = 30f;
            [Tooltip("1m 이동할 때 걸음 주기 수")]
            public float stepsPerMeter = 0.6f;
            [Tooltip("걸음마다 몸통 상하 흔들림(m)")]
            public float bobHeight = 0.06f;
            [Tooltip("이 속도(m/s)에서 걷기 동작이 최대 크기")]
            public float walkReferenceSpeed = 5f;

            [Header("대기 자세 (쉴 때 자세 위에 얹는 오프셋. 가드·공격·스턴 중엔 빠짐)")]
            [Tooltip("어깨를 바깥으로 벌리는 각도(°)")]
            public float idleShoulderSpread = 8f;
            [Tooltip("팔꿈치를 더 굽히는 각도(°)")]
            public float idleElbowBend = 25f;
            [Tooltip("위팔을 앞으로 내미는 각도(°). 주먹이 몸 앞쪽으로 온다")]
            public float idleArmForward = 10f;
            [Tooltip("상체를 앞으로 숙이는 각도(°)")]
            public float idleLean = 5f;
            [Tooltip("무릎(Shin.L·Shin.R 뼈가 있을 때만)을 굽히는 각도(°)")]
            public float idleKneeBend = 12f;
            [Tooltip("다리를 바깥으로 벌리는 각도(°). 11자 대신 벌린 자세")]
            public float idleLegSpread = 6f;
            [Tooltip("왼발을 앞으로 내미는 앞뒤 엇갈림(m 상당). 다리 길이로 각도를 정한다")]
            public float idleFootStagger = 0.15f;
            [Tooltip("발목 평행 보정 세기(1 = 다리·무릎 기울기를 발목에서 전부 되돌려 발바닥을 지면과 평행하게, 0 = 보정 없음)")]
            [Range(0f, 1f)] public float ankleLevelWeight = 1f;

            [Header("숨쉬기 (정지 중에만. 걷기 이상·가드·공격·스턴이면 줄여서 끔)")]
            [Tooltip("몸통이 위아래로 움직이는 전체 폭(m)")]
            public float breathHeight = 0.03f;
            [Tooltip("한 번 숨쉬는 주기(초)")]
            public float breathPeriod = 2.5f;
            [Tooltip("들숨 때 가슴이 들리는(상체가 살짝 젖혀지는) 각도(°)")]
            public float breathChestAngle = 1.5f;
            [Tooltip("팔이 출렁이는 각도(°)")]
            public float breathArmAngle = 2f;
            [Tooltip("팔이 몸통보다 늦게 따라오는 시간(초)")]
            public float breathArmLag = 0.3f;
            [Tooltip("숨쉬기 때 무릎이 굽었다 펴지는 각도(°). 아주 작게")]
            public float breathKneeAngle = 1.5f;
            [Tooltip("움직이기 시작하면 숨쉬기를 이 시간(초)에 걸쳐 줄여 없앤다 (다시 멈추면 같은 시간에 걸쳐 돌아옴)")]
            public float breathFadeTime = 0.2f;
            [Tooltip("상태 머신이 없는 기체(연습 봇)는 이 속도(m/s) 이상이면 움직이는 것으로 본다")]
            public float breathStopSpeed = 0.5f;

            [Header("상하체 분리 (Pelvis 뼈가 있는 모델만)")]
            [Tooltip("하체가 이동 방향으로 도는 최대 속도(°/초). 작을수록 묵직하다")]
            public float pelvisTurnSpeed = 180f;
            [Tooltip("이 속도(m/s) 이상 움직일 때만 하체가 이동 방향을 따라 돈다")]
            public float pelvisMoveMinSpeed = 0.5f;
            [Tooltip("이동 방향이 상체와 이 각도(°)보다 벌어지면 뒷걸음으로 보고 하체는 반대 방향을 향한다")]
            public float pelvisBackpedalAngle = 110f;
            [Tooltip("상체가 카메라·상대 쪽으로 도는 최대 속도(°/초). 공격 방향이 이 상체 방향을 쓴다")]
            public float upperBodyTurnSpeed = 720f;
            [Tooltip("상체·하체 비틀림 한계(°). 넘으면 하체가 끌려 돈다")]
            public float twistLimit = 100f;
            [Tooltip("머리가 상체 방향에서 더 돌아볼 수 있는 각도(°)")]
            public float headLookLimit = 30f;
            [Tooltip("머리가 볼 방향을 따라가는 지연(초, 시간 상수)")]
            public float headLookLag = 0.2f;
            [Tooltip("한 프레임에 기체가 이 거리(m) 넘게 옮겨지면(라운드 리셋 등) 상하체 방향을 기체 정면으로 바로 맞춘다")]
            public float facingSnapDistance = 3f;

            [Header("무게감 걸음 (Pelvis 뼈가 있는 모델만. 위상은 이동 거리로 진행)")]
            [Tooltip("걷기 한 걸음(한 발 디딤 → 다른 발 디딤) 거리(m)")]
            public float walkStepLength = 1.6f;
            public float runStepLength = 2.4f;
            public float sprintStepLength = 3.0f;
            [Tooltip("이 속도(m/s) 미만이면 멈춘 것으로 보고 걸음을 줄여 대기 자세로")]
            public float gaitMinSpeed = 0.3f;
            [Tooltip("걷기 시작할 때 걸음 동작이 다 나오기까지(초)")]
            public float gaitFadeInTime = 0.15f;
            [Tooltip("멈출 때 걸음 동작이 대기 자세·숨쉬기로 돌아가는 시간(초)")]
            public float stopBlendTime = 0.3f;
            [Tooltip("엉덩이 앞뒤 스윙 최대 각도(°). 실제 각도는 보폭·다리 길이로 정하고 이 값으로 자른다")]
            public float maxHipSwing = 45f;
            [Tooltip("발 드는 동안 무릎이 가장 많이 굽는 시점(0~1). 클수록 천천히 들고 빨리 내려놓는다")]
            [Range(0.3f, 0.95f)] public float swingLiftPeak = 0.7f;
            [Tooltip("발을 들 때 무릎을 굽히는 최대 각도(°)")]
            public float swingKneeAngle = 55f;

            [Header("착지")]
            [Tooltip("착지 순간 골반이 꺼지는 깊이(m). 디딘 다리 무릎을 굽혀 만든다(발은 땅에 붙은 채)")]
            public float landPelvisDrop = 0.05f;
            [Tooltip("꺼졌다 돌아오는 시간(초)")]
            public float landDropTime = 0.18f;
            [Tooltip("상체(Body)가 골반보다 늦게 따라오는 시간(초)")]
            public float landBodyLag = 0.1f;
            [Tooltip("디딘 발 쪽으로 골반이 기우는 각도(°)")]
            public float landPelvisTilt = 4f;
            [Tooltip("발소리 때 내 카메라가 흔들리는 크기(m). 0이면 안 흔들림")]
            public float footstepShake = 0.02f;
            public float footstepShakeTime = 0.12f;

            [Header("팔 스윙·가감속")]
            [Tooltip("팔이 반대쪽 다리 스윙을 따라 흔드는 비율")]
            public float armSwingRatio = 0.3f;
            [Tooltip("걸을 때 팔꿈치가 굽었다 펴지는 각도(°)")]
            public float elbowSwingAngle = 8f;
            [Tooltip("팔꿈치가 위팔보다 늦는 위상(걸음 주기 비율). 0.25 = 반 걸음")]
            public float elbowSwingLag = 0.25f;
            [Tooltip("가속도 1m/s²당 상체 숙임(°). 감속(브레이크)이면 뒤로 젖힌다")]
            public float accelLeanPerMs2 = 0.8f;
            public float maxAccelLean = 12f;
            [Tooltip("가속도를 부드럽게 하는 시간 상수(초)")]
            public float accelSmoothTime = 0.1f;

            [Header("제자리 회전 스텝 (서 있을 때 상체를 하체가 따라감)")]
            [Tooltip("서 있을 때 상체·하체가 이 각도(°) 이상 벌어지면 하체가 스텝으로 돈다")]
            public float turnStepTrigger = 60f;
            [Tooltip("한 걸음에 도는 최대 각도(°). 걸음 수 = 각도 / 이 값을 올림, 2~3걸음으로 자른다")]
            public float turnStepMaxAngle = 45f;
            [Tooltip("한 걸음 시간(초)")]
            public float turnStepTime = 0.25f;
            [Tooltip("드는 발 무릎 각도(°)")]
            public float turnStepKnee = 35f;
            [Tooltip("드는 발 엉덩이를 앞으로 드는 각도(°)")]
            public float turnStepHipLift = 10f;
            [Tooltip("스텝 착지 세기(0~1). 발소리 크기·골반 꺼짐·카메라 흔들림에 곱한다")]
            [Range(0f, 1f)] public float turnStepIntensity = 0.5f;

            [Header("스커트 (허벅지를 늦게 따라가는 스프링)")]
            public float skirtFollow = 0.8f;
            public float skirtSpringFrequency = 4f;
            [Range(0.05f, 1.5f)] public float skirtSpringDamping = 0.35f;

            [Header("접지 보정 (낮은 쪽 발바닥을 물리 콜라이더 바닥에 맞춤)")]
            public bool groundingEnabled = true;
            [Tooltip("비주얼 루트 Y를 목표로 옮기는 시간 상수(초)")]
            public float groundingSmoothTime = 0.05f;
            [Tooltip("보정 최대량(m). 이보다 크게 어긋나면 이 값까지만 맞춘다")]
            public float groundingMaxOffset = 0.5f;
            [Tooltip("보정량을 PlayLogger 이벤트(ground_offset)로 남기는 간격(초). 0이면 안 남김")]
            public float groundLogInterval = 1f;
        }

        [Serializable]
        public class MotionValues
        {
            [Header("전신 모션 공통 (M1, 하체 뼈가 있는 모델만. 포즈 각도는 MechPoseLibrary)")]
            [Tooltip("끄면 예전 포즈 코드(팔만 움직이는 펀치 등)로 돌아간다")]
            public bool enabled = true;
            [Tooltip("모션이 맡은 관절이 스프링을 건너뛰는 비율. 1 = 단계 시간에 정확히 도착, 0 = 예전처럼 스프링으로 늦게 따라감")]
            [Range(0f, 1f)] public float springBypass = 1f;

            [Header("기본 자세 (M2: 대기·걷기·달리기 = LowGuard, 전력질주 = SprintGuard)")]
            [Tooltip("전력질주에 들어가고 나올 때 두 자세를 섞는 시간(초)")]
            public float sprintStanceBlendTime = 0.2f;
            [Tooltip("전력질주 돌격 자세에서 걸음 팔 스윙 비율 (1 = 평소 그대로, 0 = 안 흔듦)")]
            [Range(0f, 1f)] public float sprintArmSwingScale = 0.3f;

            [Header("펀치 (M3: 발동 = Startup.punch, 회복 끝 = Punch.cooldown)")]
            [Tooltip("발동 시간 중 예비 자세(살짝 젖힘)에 도착하는 시점 비율. 0.4면 0.1초 발동에서 0.04초")]
            [Range(0.05f, 0.9f)] public float punchWindupRatio = 0.4f;
            [Tooltip("관절 사슬 지연(초): 뒷발 → 골반 → 허리 → 어깨 순서로 앞 관절보다 이만큼 늦게 도착 (주먹이 판정 순간에 도착)")]
            public float punchChainDelay = 0.018f;
            [Tooltip("타격 뒤 상체가 더 밀고 들어가는 팔로스루 시간(초)")]
            public float punchFollowTime = 0.06f;
            [Tooltip("팔로스루 자세를 유지하는 시간(초). 그 뒤 쿨다운 끝까지 로우 가드로 묵직하게 복귀")]
            public float punchFollowHold = 0.04f;
        }

        [Header("이동")]
        [SerializeField] private MovementValues movement = new MovementValues();
        [Header("부스트")]
        [SerializeField] private BoostValues boost = new BoostValues();
        [Header("히트")]
        [SerializeField] private HeatValues heat = new HeatValues();
        [Header("펀치 / 맞딜")]
        [SerializeField] private PunchValues punch = new PunchValues();
        [Header("공격 발생 시간·입력 버퍼 (버튼 → 판정)")]
        [SerializeField] private AttackStartupValues startup = new AttackStartupValues();
        [Header("가드")]
        [SerializeField] private GuardValues guard = new GuardValues();
        [Header("패리")]
        [SerializeField] private ParryValues parry = new ParryValues();
        [Header("태클 (차징)")]
        [SerializeField] private TackleValues tackle = new TackleValues();
        [Header("대시 어택 (전력질주 1.5초 미만 어깨 태클)")]
        [SerializeField] private DashAttackValues dashAttack = new DashAttackValues();
        [Header("스러스터 런지")]
        [SerializeField] private LungeValues lunge = new LungeValues();
        [Header("스킬: 앵커 클로")]
        [SerializeField] private AnchorClawValues anchorClaw = new AnchorClawValues();
        [Header("스킬: 임팩트 슬램")]
        [SerializeField] private ImpactSlamValues impactSlam = new ImpactSlamValues();
        [Header("락온")]
        [SerializeField] private LockOnValues lockOn = new LockOnValues();
        [Header("히트스톱")]
        [SerializeField] private HitStopValues hitStop = new HitStopValues();
        [Header("외형 애니메이션 (MechVisualRig)")]
        [SerializeField] private VisualValues visual = new VisualValues();
        [Header("전신 공격 모션 (MechVisualRig + MechPoseLibrary, M1~M7)")]
        [SerializeField] private MotionValues motion = new MotionValues();

        public MovementValues Movement => movement;
        public BoostValues Boost => boost;
        public HeatValues Heat => heat;
        public PunchValues Punch => punch;
        public AttackStartupValues Startup => startup;
        public GuardValues Guard => guard;
        public ParryValues Parry => parry;
        public TackleValues Tackle => tackle;
        public DashAttackValues DashAttack => dashAttack;
        public LungeValues Lunge => lunge;
        public AnchorClawValues AnchorClaw => anchorClaw;
        public ImpactSlamValues ImpactSlam => impactSlam;
        public LockOnValues LockOn => lockOn;
        public HitStopValues HitStop => hitStop;
        public VisualValues Visual => visual;
        public MotionValues Motion => motion;

        private static BernardTuning _fallback;

        /// <summary>
        /// 기체에 연결된 에셋이 없을 때 쓸 값: Resources/BernardTuning → 없으면 코드 기본값 인스턴스.
        /// 더미처럼 MechTuning이 없는 기체도 같은 Resources 에셋을 읽게 된다.
        /// </summary>
        public static BernardTuning Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = Resources.Load<BernardTuning>(ResourcesName);
                    if (_fallback == null)
                    {
                        _fallback = CreateInstance<BernardTuning>();
                        _fallback.name = "BernardTuning (코드 기본값)";
                        _fallback.hideFlags = HideFlags.DontSave;
                    }
                }
                return _fallback;
            }
        }
    }
}
