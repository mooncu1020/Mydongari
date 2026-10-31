using System;
using UnityEngine;

namespace MyDongari.Mech
{
    /// <summary>이동기 선택 (V8). 문제가 생기면 Legacy로 돌리면 V6.1 이동이 그대로 돈다.</summary>
    public enum MovementModel
    {
        /// <summary>V6.1까지의 이동 (다이내믹 리지드바디, Idle/Run/Dash/Air 상태).</summary>
        Legacy,
        /// <summary>V8 버추얼온식 이동기 (60Hz 고정 틱, 위치 += 속도, 키네마틱 리지드바디).</summary>
        VirtualOn
    }

    public partial class BernardTuning
    {
        /// <summary>
        /// V8 이동기 수치. docs/REFERENCE_MOVEMENT.md의 원본 단위 그대로 둔다:
        /// 거리 u(기체 키 / 22), 속도 u/f, 가속 u/f², 시간 f(1/60초), 각도 "단위"(65536 = 360°).
        /// 기체 키를 렌더러로 재서 실행할 때 미터·초로 바꾼다. 키 2.6m면 u/f × 7.09 = m/s (V6 표와 같은 값).
        /// </summary>
        [Serializable]
        public class V8Values
        {
            [Header("단위")]
            [Tooltip("기체 키가 몇 u인지 (사양서: 22u)")]
            public float bodyHeightUnits = 22f;
            [Tooltip("렌더러로 키를 못 재거나 1~6m 밖이면 이 키(m)를 쓴다")]
            public float fallbackBodyHeight = 2.6f;
            [Tooltip("고정 틱(Hz). 사양서 60")]
            public float tickRate = 60f;

            [Header("속도 곡선 (u/f, 앞/앞대각/옆/뒤대각/뒤)")]
            public DirectionalValues walkSpeed = new DirectionalValues(3.10f, 2.75f, 2.70f, 2.50f, 2.80f);
            public DirectionalValues dashSpeed = new DirectionalValues(3.40f, 3.20f, 3.15f, 3.00f, 2.00f);
            public DirectionalValues airDashSpeed = new DirectionalValues(3.50f, 3.20f, 3.20f, 3.20f, 2.00f);
            public DirectionalValues jumpSpeed = new DirectionalValues(1.60f, 1.95f, 2.00f, 1.95f, 1.60f);
            [Tooltip("근접 스텝 시작 속력. 사양서는 앞 1.5·옆 4.2만 있다 — 대각은 사이 값, 뒤는 앞과 같게 둠")]
            public DirectionalValues closeStepStartSpeed = new DirectionalValues(1.5f, 2.85f, 4.2f, 2.85f, 1.5f);
            [Tooltip("근접 스텝 끝 속력. 사양서는 앞 2.9·옆 2.2만 있다 — 대각은 사이 값, 뒤는 앞과 같게 둠")]
            public DirectionalValues closeStepEndSpeed = new DirectionalValues(2.9f, 2.55f, 2.2f, 2.55f, 2.9f);
            [Tooltip("걷기·점프 속력에 레버 이동 벡터 크기(0.8 ~ 1.2)를 곱한다 (사양서 4.2, 확신도 중간)")]
            public bool scaleByLeverMagnitude = true;

            [Header("거리 (u)")]
            [Tooltip("상대가 이 거리 안이면 모든 이동 수평 속력 × closeSlowScale (사양서 200u = 23.6m)")]
            public float closeSlowDistance = 200f;
            public float closeSlowScale = 0.78f;
            [Tooltip("근접 구역 경계 (사양서 70u = 8.3m). 레버 표·선회율·근접 스텝이 바뀐다")]
            public float closeZoneDistance = 70f;
            [Tooltip("레버 이동 벡터 거리 보정: 이 거리 안은 × leverNearScale")]
            public float leverNearDistance = 70f;
            [Tooltip("레버 이동 벡터 거리 보정: 이 거리부터 1.0 (사이는 선형)")]
            public float leverFarDistance = 100f;
            public float leverNearScale = 0.7f;

            [Header("레버 누적값 (방향 0 앞, 1 앞좌, 2 좌, 3 뒤좌, 4 뒤, 5 뒤우, 6 우, 7 앞우)")]
            [Tooltip("L스틱이 이 기울기 이상이면 그 8방향으로 레버를 민 것으로 본다")]
            [Range(0f, 1f)] public float stickThreshold = 0.3f;
            public int[] riseFar = { 100, 98, 98, 48, 64, 48, 98, 98 };
            public int[] riseNear = { 100, 96, 40, 36, 26, 36, 40, 96 };
            public int[] decay = { 90, 90, 200, 90, 90, 90, 200, 90 };
            public int[] oppositeDecayFar = { 96, 104, 128, 104, 96, 104, 128, 104 };
            public int[] oppositeDecayNear = { 88, 88, 78, 48, 88, 48, 78, 88 };
            public int thresholdFar = 64;
            public int thresholdNear = 48;

            [Header("선회 (각 단위/f, 65536 = 360°)")]
            [Tooltip("R스틱 좌우 |값| 이 이하면 선회 0. 넘으면 부호 유지 제곱이 선회 계수")]
            [Range(0f, 1f)] public float turnDeadzone = 0.05f;
            [Tooltip("상대가 정면 ±이 각도(°) 안이면 '정면 안' 선회율")]
            public float frontHalfAngle = 33.75f;
            [Tooltip("원거리, 상대가 정면 밖 (2304 = 759°/s)")]
            public float turnOutside = 2304f;
            [Tooltip("원거리, 상대가 정면 안 (400 = 132°/s)")]
            public float turnInsideFar = 400f;
            [Tooltip("근접 구역, 상대가 정면 안 (512)")]
            public float turnInsideNear = 512f;
            [Tooltip("근접 구역, 상대가 정면 ±22.5° 안 (549)")]
            public float turnInsideNearCenter = 549f;
            public float nearCenterHalfAngle = 22.5f;
            [Tooltip("지상 대시 시작·지속 중, 상대가 정면 밖 / 안")]
            public float turnDashOutside = 384f;
            public float turnDashInside = 320f;
            [Tooltip("점프 중 고정 선회율 (64 = 21°/s), ○를 누르고 있으면 turnJumpTurbo")]
            public float turnJump = 64f;
            public float turnJumpTurbo = 384f;
            [Tooltip("공중 대시 중 (몸과 진로를 같이 돌림, 104 = 34°/s)")]
            public float turnAirDash = 104f;

            [Header("자동 선회 (각 단위/f)")]
            [Tooltip("점프 이륙 '점프 조준': 몸을 상대 쪽으로 (2304)")]
            public float jumpAimRate = 2304f;
            [Tooltip("대시 중 공격 '지속 조준' (2304)")]
            public float dashAttackAimRate = 2304f;
            [Tooltip("공격 사용 '사격 조준': 몸을 상대 쪽으로 (사양서는 무기별 80~2560, 우리 공격 하나의 값)")]
            public float attackAimRate = 2560f;
            [Tooltip("사격 조준 유지 프레임 (15f 지나거나 근접 구역에 들어오면 끝)")]
            public int attackAimFrames = 15;
            [Tooltip("사격 조준이 최대 선회율에 걸린 틱의 수평 속력 배율")]
            public float attackAimSlowScale = 0.1f;
            [Tooltip("공중 대시 복귀: 몸을 카메라 방위 쪽으로 (2560)")]
            public float airDashReturnRate = 2560f;
            [Tooltip("점프 조준·공중 대시 중 카메라 방위가 몸을 쫓는 최대율 (4096 = 22.5°/f)")]
            public float cameraYawFollowRate = 4096f;
            [Tooltip("정렬로 보는 각도 차(°)")]
            public float alignEpsilon = 0.5f;

            [Header("대시 게이지 (사양서 1.2·4.4)")]
            public float gaugeDecay = 200f;
            public float gaugeGain = 206f;
            public float gaugeMax = 500f;
            [Tooltip("게이지가 이 값 미만이어야 대시 허가")]
            public float gaugeAllow = 24f;

            [Header("지상 대시 (f)")]
            public int dashStartFrames = 16;
            [Tooltip("뒤 계열(뒤·뒤대각) 시작 모션")]
            public int dashStartFramesBack = 12;
            public int dashEndFrames = 16;
            [Tooltip("종료 단계 감속: v × (1 − t / 이 값)")]
            public float dashEndDivisor = 17f;
            [Tooltip("종료 단계·대시 정지에서 이 프레임째부터 공격을 받는다 (V6.1 0.05초 = 3f)")]
            public int dashEndAttackFrame = 3;
            [Tooltip("대시 정지 길이 (종료 모션)")]
            public int dashStopFrames = 16;
            [Tooltip("○를 누른 뒤 이 프레임 안에 스틱이 들어오면 그때 방향을 잠근다 (서기·걷기). 근접 구역 서기면 근접 스텝")]
            public int dashLockWindow = 15;
            [Tooltip("지상 대시 중 공중(발판 끝)일 때 중력·최저 vy (u/f², u/f)")]
            public float dashAirGravity = 0.081f;
            public float dashAirMinVy = -1.801f;

            [Header("근접 스텝 (f)")]
            public int closeStepStartFrames = 6;
            public int closeStepFrames = 28;

            [Header("점프 (u/f, u/f², f)")]
            public int takeoffFrames = 2;
            public float jumpVy = 3.25f;
            public float gravity = 0.074f;
            [Tooltip("상승 중 vy가 이 값 이하가 되면 정점 → 다음 틱부터 하강")]
            public float apexVy = 0.001f;
            [Tooltip("하강 vy 하한. 하강은 보조·본 처리에서 두 번 더해 실효 2배 (7.46m/s)")]
            public float fallMinVy = -0.526f;
            [Tooltip("하강 2중 적분 (사양서 7.1 확정). 끄면 1회")]
            public bool fallDoubleIntegrate = true;
            [Tooltip("발판에서 떨어질 때 시작 vy")]
            public float ledgeFallVy = -0.5f;
            [Tooltip("하강 중 △를 누른 지 이 프레임 ~ hoverEndFrame 사이: vy += (경과 − 시작) × hoverGain, 상한 hoverMaxVy")]
            public int hoverStartFrame = 7;
            public int hoverEndFrame = 14;
            public float hoverGain = 0.1f;
            public float hoverMaxVy = -0.01f;
            [Tooltip("하강 중 급강하 입력 후 이 프레임 안: vy −= (경과 + 1) × gatherFallGain")]
            public int gatherFallFrames = 15;
            public float gatherFallGain = 0.2f;

            [Header("공중 대시")]
            [Tooltip("상승 중(vy > 0) 중력 배율")]
            public float airDashRisingGravityScale = 0.5f;
            [Tooltip("게이지가 차서 끝나면 이 vy로 점프 하강")]
            public float airDashEndVy = -0.001f;

            [Header("급강하 (u/f)")]
            [Tooltip("공중에서 L스틱을 이 값 이하로(아래로) 당기고 ○ = 급강하 입력 (임시)")]
            [Range(-1f, 0f)] public float diveStickY = -0.7f;
            public float diveStartVy = -1.75f;
            public float diveAccel = 0.25f;
            [Tooltip("vy가 이 값보다 크면(덜 빠르면) 매 틱 diveAccel만큼 더 빨라진다")]
            public float diveAccelUntilVy = -2.751f;

            [Header("지면·벽 (u)")]
            [Tooltip("이 높이 이하 단차는 따라 오르내리고, 넘으면 아래는 낙하·위는 벽 (5u = 0.59m)")]
            public float stepHeight = 5f;

            [Header("카메라 (u, 각 단위)")]
            [Tooltip("기체 뒤 거리 (78u = 키 × 3.54)")]
            public float cameraDistance = 78f;
            [Tooltip("시선 피치, 아래로 (1536 = 8.4°)")]
            public float cameraPitch = 1536f;
            [Tooltip("시선 표적 = 기준 높이(발 + 키 × cameraBaseHeightRatio) + 이 값")]
            public float cameraTargetUp = 6f;
            [Range(0f, 1f)] public float cameraBaseHeightRatio = 0.5f;
            [Tooltip("기준점이 매 틱 기체 위치와의 차이를 이 비율만큼 따라감 (12%)")]
            [Range(0f, 1f)] public float cameraFollow = 0.12f;
            [Tooltip("세로 FOV(°)")]
            public float cameraFov = 35f;
            [Tooltip("선회 리드 최대 / 틱당 변화 (1536 = 8.4°, 256 = 1.41°)")]
            public float cameraLeadMax = 1536f;
            public float cameraLeadStep = 256f;
        }

        [Header("이동기 선택 (V8)")]
        [Tooltip("VirtualOn = V8 버추얼온식 이동기, Legacy = V6.1 이동. 플레이 중 바꿔도 다음 틱에 전환된다")]
        [SerializeField] private MovementModel movementModel = MovementModel.VirtualOn;
        [Header("V8 이동기 (원본 단위: u·u/f·f·각 단위)")]
        [SerializeField] private V8Values v8 = new V8Values();

        public MovementModel Model => movementModel;
        public V8Values V8 => v8;

        /// <summary>각 단위(65536 = 360°) → 도.</summary>
        public static float AngleUnitsToDegrees(float units) => units * (360f / 65536f);
    }
}
