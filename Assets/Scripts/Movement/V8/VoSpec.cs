using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MyDongari.Movement.V8
{
    /// <summary>단위 환산 (사양서 0장). 거리 u = 기체 키 / 22, 시간 f = 1/60초, 각도 "단위" 65536 = 360°.</summary>
    public static class VoUnits
    {
        /// <summary>각 단위(65536 = 360°) → 도.</summary>
        public static float AngleUnitsToDegrees(float units) => units * (360f / 65536f);
    }

    /// <summary>
    /// 방향별 값 5점: 앞(0°) / 앞대각(45°) / 옆(90°) / 뒤대각(135°) / 뒤(180°). 사이 각도는 선형 보간, 좌우 대칭 (사양서 4.2 "5점 보간").
    /// 템진 원본이 방향마다 속도가 다르다 — 앞이 제일 빠르고 뒤 대시는 크게 느리다.
    /// </summary>
    [Serializable]
    public struct VoDirectional
    {
        public float front;
        public float frontDiagonal;
        public float side;
        public float backDiagonal;
        public float back;

        public VoDirectional(float front, float frontDiagonal, float side, float backDiagonal, float back)
        {
            this.front = front;
            this.frontDiagonal = frontDiagonal;
            this.side = side;
            this.backDiagonal = backDiagonal;
            this.back = back;
        }

        /// <summary>기준 정면에서 잰 각도(°, 부호·범위 무관)의 값.</summary>
        public float Evaluate(float angleDegrees)
        {
            float a = Mathf.Abs(Mathf.DeltaAngle(0f, angleDegrees));
            float segment = a / 45f;
            int i = Mathf.Clamp(Mathf.FloorToInt(segment), 0, 3);
            return Mathf.Lerp(Point(i), Point(i + 1), segment - i);
        }

        /// <summary>L스틱 방향(위 = 앞)의 값. 스틱이 중립이면 앞.</summary>
        public float Evaluate(Vector2 stick)
        {
            if (stick.sqrMagnitude < 0.000001f) return front;
            return Evaluate(Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg);
        }

        private float Point(int index)
        {
            switch (index)
            {
                case 0: return front;
                case 1: return frontDiagonal;
                case 2: return side;
                case 3: return backDiagonal;
                default: return back;
            }
        }

        /// <summary>다섯 값 중 가장 큰 값.</summary>
        public float Max => Mathf.Max(front, Mathf.Max(frontDiagonal, Mathf.Max(side, Mathf.Max(backDiagonal, back))));

        public override string ToString()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0}/{1}/{2}/{3}/{4}", front, frontDiagonal, side, backDiagonal, back);
        }
    }

    /// <summary>
    /// 이동기 수치. **기본값은 docs/REFERENCE_MOVEMENT.md(템진 이동 사양서)의 원본값이고 여기서는 고치지 않는다.**
    /// 원본 단위 그대로: 거리 u(기체 키 / 22), 속도 u/f, 가속 u/f², 시간 f(1/60초), 각도 "단위"(65536 = 360°).
    /// 우리 스타일로 바꾸는 값은 StyleOverrides 에셋이 이 값의 사본 위에 덮어쓴다 — 이동기는 그 결과(유효값)를 IVoHost.Spec으로 받는다.
    /// 주석의 [n.n]은 사양서 절 번호. [미상]은 사양서에 값이 없어 중립값으로 채운 칸이고, [스위치]는 원본과 다른 우리 동작을 켜는 칸(기본 = 원본 동작)이다.
    /// </summary>
    public class VoSpec
    {
        // ---- 단위 [0] ----
        /// <summary>기체 키가 몇 u인지.</summary>
        public float bodyHeightUnits = 22f;
        /// <summary>기체 키를 렌더러로 못 재거나 1~6m 밖이면 쓰는 키(m). 원본 추정 22u = 2.6m.</summary>
        public float fallbackBodyHeight = 2.6f;
        /// <summary>고정 틱(Hz). [1.1]</summary>
        public float tickRate = 60f;

        // ---- 속도 곡선 (u/f, 앞/앞대각/옆/뒤대각/뒤) [8] ----
        public VoDirectional walkSpeed = new VoDirectional(3.10f, 2.75f, 2.70f, 2.50f, 2.80f);
        public VoDirectional dashSpeed = new VoDirectional(3.40f, 3.20f, 3.15f, 3.00f, 2.00f);
        public VoDirectional airDashSpeed = new VoDirectional(3.50f, 3.20f, 3.20f, 3.20f, 2.00f);
        public VoDirectional jumpSpeed = new VoDirectional(1.60f, 1.95f, 2.00f, 1.95f, 1.60f);
        /// <summary>근접 스텝 시작 속력. 원본은 앞 1.5·옆 4.2뿐 [4.3·8]. 대각·뒤는 [미상] — 대각 = 앞과 옆의 중간, 뒤 = 앞으로 채움.</summary>
        public VoDirectional closeStepStartSpeed = new VoDirectional(1.5f, 2.85f, 4.2f, 2.85f, 1.5f);
        /// <summary>근접 스텝 끝 속력. 원본은 앞 2.9·옆 2.2뿐. 대각·뒤는 [미상] — 같은 방식으로 채움.</summary>
        public VoDirectional closeStepEndSpeed = new VoDirectional(2.9f, 2.55f, 2.2f, 2.55f, 2.9f);

        // ---- 거리 (u) ----
        /// <summary>상대가 이 거리 안이면 모든 이동 수평 속력 × closeSlowScale (200u = 23.6m). [1.4]</summary>
        public float closeSlowDistance = 200f;
        public float closeSlowScale = 0.78f;
        /// <summary>근접 구역 경계 (70u = 8.3m). 선회율·근접 스텝이 바뀐다. [8]</summary>
        public float closeZoneDistance = 70f;
        /// <summary>레버 이동 벡터 거리 보정: 이 거리 안은 × leverNearScale, leverFarDistance부터 1.0 (사이는 선형). [2.1]</summary>
        public float leverNearDistance = 70f;
        public float leverFarDistance = 100f;
        public float leverNearScale = 0.7f;

        // ---- 대시 방향 잠금 [2.1] ----
        /// <summary>터보를 누른 뒤 이 프레임 안에 레버가 들어오면 그때 방향을 잠근다 (서기·걷기). 근접 구역 서기면 근접 스텝.</summary>
        public int dashLockWindow = 15;
        /// <summary>[스위치] 레버 중립에서 터보를 누르면 앞 대시. 원본은 방향 잠금이 없어 대시가 안 나간다.</summary>
        public bool neutralTurboDashesForward = false;
        /// <summary>[스위치] 근접 구역 서기의 중립 터보로 시작한 앞 대시가, 이 프레임 안에 레버가 들어오면 근접 스텝으로 바뀐다. 0 = 없음(원본).</summary>
        public int closeStepFromDashFrames = 0;

        // ---- 수동 선회 (각 단위/f) [2.1·2.2] ----
        /// <summary>선회 입력 |s| 이 이하면 0. 넘으면 부호 유지 제곱이 선회 계수.</summary>
        public float turnDeadzone = 0.05f;
        /// <summary>상대가 정면 ±이 각도(°) 안이면 '정면 안' 선회율.</summary>
        public float frontHalfAngle = 33.75f;
        /// <summary>[스위치] 정면 안 → 밖 선회율이 frontHalfAngle에서 이 각도(°)까지 선형으로 올라간다. frontHalfAngle 이하면 경계에서 바로 바뀜(원본).</summary>
        public float turnBlendEndAngle = 33.75f;
        /// <summary>[스위치] 빠른 '정면 밖' 선회율을 상대 쪽으로 도는 입력에만 쓴다. 원본은 회전 방향을 가리지 않는다.</summary>
        public bool fastTurnOnlyTowardOpponent = false;
        /// <summary>원거리, 상대가 정면 밖 (2304 = 759°/s).</summary>
        public float turnOutside = 2304f;
        /// <summary>원거리, 상대가 정면 안 (400 = 132°/s).</summary>
        public float turnInsideFar = 400f;
        /// <summary>근접 구역, 상대가 정면 안 (512).</summary>
        public float turnInsideNear = 512f;
        /// <summary>근접 구역, 상대가 정면 ±22.5° 안 (549).</summary>
        public float turnInsideNearCenter = 549f;
        public float nearCenterHalfAngle = 22.5f;
        /// <summary>지상 대시 시작·지속 중, 상대가 정면 밖 / 안.</summary>
        public float turnDashOutside = 384f;
        public float turnDashInside = 320f;
        /// <summary>점프 중 고정 선회율 (64 = 21°/s), 터보를 누르고 있으면 turnJumpTurbo.</summary>
        public float turnJump = 64f;
        public float turnJumpTurbo = 384f;
        /// <summary>공중 대시 중 (몸과 진로를 같이 돌림, 104 = 34°/s).</summary>
        public float turnAirDash = 104f;

        // ---- 자동 선회 (각 단위/f) [2.3] ----
        /// <summary>점프 이륙 "점프 조준": 몸을 상대 쪽으로.</summary>
        public float jumpAimRate = 2304f;
        /// <summary>대시 중 공격 "지속 조준".</summary>
        public float dashAttackAimRate = 2304f;
        /// <summary>"사격 조준" 유지 프레임 (지나거나 근접 구역에 들어오면 끝). 선회율은 무기별이라 호스트가 공격을 알릴 때 준다.</summary>
        public int attackAimFrames = 15;
        /// <summary>사격 조준이 최대 선회율에 걸린 틱의 수평 속력 배율.</summary>
        public float attackAimSlowScale = 0.1f;
        /// <summary>공중 대시 복귀: 몸을 카메라 방위 쪽으로.</summary>
        public float airDashReturnRate = 2560f;
        /// <summary>점프 조준·공중 대시 중 카메라 방위가 몸을 쫓는 최대율 (4096 = 22.5°/f).</summary>
        public float cameraYawFollowRate = 4096f;
        /// <summary>정렬로 보는 각도 차(°). [미상] 원본은 "정렬되면"만 있다 — 구현 허용 오차.</summary>
        public float alignEpsilon = 0.5f;

        // ---- 대시 게이지 [1.2·4.4] ----
        public float gaugeDecay = 200f;
        public float gaugeGain = 206f;
        public float gaugeMax = 500f;
        /// <summary>게이지가 이 값 미만이어야 대시 허가.</summary>
        public float gaugeAllow = 24f;

        // ---- 지상 대시 (f) [4.4·4.5] ----
        public int dashStartFrames = 16;
        /// <summary>뒤 계열(뒤·뒤대각) 시작 모션.</summary>
        public int dashStartFramesBack = 12;
        public int dashEndFrames = 16;
        /// <summary>종료 단계 감속: v × (1 − t / 이 값).</summary>
        public float dashEndDivisor = 17f;
        /// <summary>종료 단계에서 이 프레임째부터 무기를 받는다. (대시 정지에도 같은 값을 쓰는 것은 우리 규칙)</summary>
        public int dashEndAttackFrame = 3;
        /// <summary>대시 정지 길이 (종료 모션).</summary>
        public int dashStopFrames = 16;
        /// <summary>지상 대시 중 공중(발판 끝)일 때 중력·최저 vy (u/f², u/f).</summary>
        public float dashAirGravity = 0.081f;
        public float dashAirMinVy = -1.801f;

        // ---- 근접 스텝 (f) [4.3] ----
        public int closeStepStartFrames = 6;
        /// <summary>전체 길이 (6f 시작 + 22f).</summary>
        public int closeStepFrames = 28;

        // ---- 점프 (u/f, u/f², f) [4.6·7.1] ----
        public int takeoffFrames = 2;
        public float jumpVy = 3.25f;
        public float gravity = 0.074f;
        /// <summary>상승 중 vy가 이 값 이하가 되면 정점 → 다음 틱부터 하강.</summary>
        public float apexVy = 0.001f;
        /// <summary>하강 vy 하한. 하강은 보조·본 처리에서 두 번 더해 실효 2배.</summary>
        public float fallMinVy = -0.526f;
        /// <summary>하강 2중 적분 (7.1 확정).</summary>
        public bool fallDoubleIntegrate = true;
        /// <summary>발판에서 떨어질 때 시작 vy. [6.1]</summary>
        public float ledgeFallVy = -0.5f;
        /// <summary>상승 중 레버 벌리기 재입력: vy를 jumpVy로 한 번 다시. 원본에 있다 — 끄는 것이 우리 스타일.</summary>
        public bool risingRejump = true;
        /// <summary>하강 중 레버 벌리기를 누른 지 hoverStartFrame ~ hoverEndFrame 사이: vy += (경과 − 시작) × hoverGain, 상한 hoverMaxVy.</summary>
        public int hoverStartFrame = 7;
        public int hoverEndFrame = 14;
        public float hoverGain = 0.1f;
        public float hoverMaxVy = -0.01f;

        // ---- 공중 대시 [4.7] ----
        /// <summary>상승 중(vy > 0) 중력 배율.</summary>
        public float airDashRisingGravityScale = 0.5f;
        /// <summary>게이지가 차서 끝나면 이 vy로 점프 하강.</summary>
        public float airDashEndVy = -0.001f;

        // ---- 급강하 (u/f) [4.8] ----
        public float diveStartVy = -1.75f;
        public float diveAccel = 0.25f;
        /// <summary>vy가 이 값보다 크면(덜 빠르면) 매 틱 diveAccel만큼 더 빨라진다.</summary>
        public float diveAccelUntilVy = -2.751f;
        /// <summary>[스위치] 하강 중에도 급강하 입력을 받는다. 원본은 상승 중에만.</summary>
        public bool diveAllowedWhileFalling = false;

        // ---- 지면·벽 (u) [6.1] ----
        /// <summary>이 높이 이하 단차는 따라 오르내리고, 넘으면 아래는 낙하·위는 벽 (5u).</summary>
        public float stepHeight = 5f;

        // ---- 카메라 (u, 각 단위) [5.2·5.3] ----
        /// <summary>기체 뒤 거리 (78u).</summary>
        public float cameraDistance = 78f;
        /// <summary>시선 피치, 아래로 (1536 = 8.4°).</summary>
        public float cameraPitch = 1536f;
        /// <summary>시선 표적 = 기준 높이(발 + 키 × cameraBaseHeightRatio) + 이 값.</summary>
        public float cameraTargetUp = 6f;
        /// <summary>[미상] 기준 높이 비율. 원본 계산식을 확인하지 못해 기체 가운데(0.5)로 둠.</summary>
        public float cameraBaseHeightRatio = 0.5f;
        /// <summary>시선 표적 오프셋이 매 틱 목표값과의 차이를 이 비율만큼 따라감 (12%).</summary>
        public float cameraTargetOffsetFollow = 0.12f;
        /// <summary>기준점이 매 틱 기체 위치와의 차이를 이 비율만큼 따라감. 원본은 지연 없음(1) — 눈·표적 = 기체 위치 + 국부 오프셋 [5.1].</summary>
        public float cameraPositionFollow = 1f;
        /// <summary>기체가 화면 가로 중앙에서 벗어날 수 있는 최대 거리(화면 폭 대비). 원본엔 없다 — 0.5(화면 끝)면 사실상 꺼짐.</summary>
        public float cameraMaxScreenOffset = 0.5f;
        /// <summary>선회 리드 최대 / 틱당 변화 (1536 = 8.4°, 256 = 1.41°).</summary>
        public float cameraLeadMax = 1536f;
        public float cameraLeadStep = 256f;

        /// <summary>원본값 그대로의 새 사본.</summary>
        public static VoSpec Original() => new VoSpec();

        public VoSpec Clone() => (VoSpec)MemberwiseClone();

        /// <summary>모든 칸을 "이름=값" 한 줄씩으로 (스모크 테스트 로그·비교용).</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (FieldInfo field in typeof(VoSpec).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                sb.Append(field.Name).Append('=').Append(Format(field.GetValue(this))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>원본과 다른 칸만 "이름: 원본 → 지금" 으로.</summary>
        public string DescribeDifferences()
        {
            VoSpec original = Original();
            var sb = new StringBuilder();
            foreach (FieldInfo field in typeof(VoSpec).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string a = Format(field.GetValue(original));
                string b = Format(field.GetValue(this));
                if (a != b) sb.Append(field.Name).Append(": ").Append(a).Append(" → ").Append(b).Append('\n');
            }
            return sb.ToString();
        }

        private static string Format(object value)
        {
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
