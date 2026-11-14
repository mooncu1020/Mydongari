using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 근접 공격 한 변형의 구동 수치 (전투 사양서 1.2·1.3). 단계: ① 파고들기 → ② 공격 → ③ 후딜.
    /// 판정 창은 ② 단계 안의 틱 번호(0부터), 끝 번호는 "그 번호 직전까지".
    /// </summary>
    [Serializable]
    public struct VoMeleeMove
    {
        /// <summary>기준 속력(u/f). ①은 0에서 여기까지 선형, ②는 이 값, ③은 0.</summary>
        public float baseSpeed;
        public int approachFrames;
        public int attackFrames;
        public int recoveryFrames;
        /// <summary>판정 창 시작(② 단계 틱 번호). 이 틱부터 ② 끝까지 점프·대시 취소가 잠긴다.</summary>
        public int hitStart;
        /// <summary>판정 창 끝(이 번호 직전까지 켜짐).</summary>
        public int hitEnd;
        /// <summary>추적 최대 회전율(각 단위/f).</summary>
        public float trackRate;
        /// <summary>정지 거리(u): 상대가 이 거리 이하면 속력 0, 추적 종료.</summary>
        public float stopDistance;

        public VoMeleeMove(float baseSpeed, int approachFrames, int attackFrames, int recoveryFrames, int hitStart, int hitEnd, float trackRate, float stopDistance)
        {
            this.baseSpeed = baseSpeed;
            this.approachFrames = approachFrames;
            this.attackFrames = attackFrames;
            this.recoveryFrames = recoveryFrames;
            this.hitStart = hitStart;
            this.hitEnd = hitEnd;
            this.trackRate = trackRate;
            this.stopDistance = stopDistance;
        }

        public int TotalFrames => approachFrames + attackFrames + recoveryFrames;

        public override string ToString()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0}u/f {1}/{2}/{3}f 판정 {4}~{5} 추적 {6} 정지 {7}u",
                baseSpeed, approachFrames, attackFrames, recoveryFrames, hitStart, hitEnd, trackRate, stopDistance);
        }
    }

    /// <summary>
    /// 전투 뼈대 수치. **기본값은 docs/REFERENCE_COMBAT.md(템진 전투 사양서)의 원본값이고 여기서는 고치지 않는다** (VoSpec과 같은 규칙).
    /// 원본 단위 그대로: 거리 u(기체 키 / 22), 속도 u/f, 시간 f(1/60초), 각도 "단위"(65536 = 360°) 또는 도.
    /// 우리 값은 StyleOverrides가 이 값의 사본 위에 덮어쓴다 — 게임은 그 결과(유효값)를 IVoHost.CombatSpec / StyleOverrides.EffectiveCombat으로 받는다.
    /// 주석의 [n.n]은 전투 사양서 절 번호. [미상]은 사양서에 값이 없어 임시로 채운 칸(StyleOverrides에서 우리 값으로 정한다).
    /// </summary>
    public class VoCombatSpec
    {
        // ---- 근접 구역 A (버튼 A) [1.1] ----
        /// <summary>바깥 거리(u). 70u = 6.14m.</summary>
        public float zoneOuter = 70f;
        /// <summary>안쪽 원 반지름(u). 이 안이면 각도와 무관하게 구역이 켜진다("밀착"). 12.5u = 1.10m.</summary>
        public float zoneInner = 12.5f;
        /// <summary>원뿔 반각(°). 꼭짓점은 내 위치에서 정면 반대쪽으로 2 × zoneInner × sin(반각)만큼 물린 점.</summary>
        public float zoneHalfAngle = 42.2f;
        /// <summary>높이차(나 − 상대, u) 창. 밖이면 구역이 꺼진다. −24u(상대가 2.11m 위) ~ +13u(1.14m 아래).</summary>
        public float zoneHeightMin = -24f;
        public float zoneHeightMax = 13f;

        // ---- 파고들기 추적·속력 보정 [1.3] ----
        /// <summary>근접 시작 틱에 상대가 이 거리(u) 미만일 때만 추적 카운터가 시작된다.</summary>
        public float trackStartDistance = 100f;
        /// <summary>추적 회전은 카운터가 이 값일 때까지만.</summary>
        public int trackTurnFrames = 15;
        /// <summary>카운터가 이 값을 넘으면 속력 0, 추적 종료.</summary>
        public int trackMaxFrames = 32;
        /// <summary>필요 회전이 최대율을 넘은 틱의 속력 배율.</summary>
        public float trackSaturatedScale = 0.1f;
        /// <summary>상대 거리 > offAxisDistance(u)이고 상대가 정면 ±offAxisHalfAngle(°) 밖이면 속력 × offAxisScale.</summary>
        public float offAxisDistance = 20f;
        public float offAxisHalfAngle = 22.5f;
        public float offAxisScale = 0.5f;

        // ---- 근접 변형 [1.2] ----
        /// <summary>A1 1타.</summary>
        public VoMeleeMove a1First = new VoMeleeMove(7.251f, 16, 10, 22, 3, 9, 848f, 12.5f);
        /// <summary>A1 2타: 기준 속력 = 1타 × 0.75. 단계 길이·판정 창은 [미상](원본은 같은 모션의 다른 구간) — 1타와 같게 채움.</summary>
        public VoMeleeMove a1Second = new VoMeleeMove(7.251f * 0.75f, 16, 10, 22, 3, 9, 848f, 12.5f);
        /// <summary>C1.</summary>
        public VoMeleeMove c1 = new VoMeleeMove(6.7f, 22, 14, 34, 6, 8, 512f, 15f);

        // ---- 점프 근접 [1.7] ----
        /// <summary>점프 입력 뒤 이 틱 안에 근접 조건이 성립하면 점프 근접.</summary>
        public int jumpMeleeInputFrames = 8;
        public float jumpMeleeVy = 3.80f;
        /// <summary>
        /// 점프 근접 구동. 원본 확정은 추적 2560 단위/f뿐. 기준 속력·단계 길이·판정 창·정지 거리는 [미상] — StyleOverrides가 우리 값으로 채운다.
        /// </summary>
        public VoMeleeMove jumpMelee = new VoMeleeMove(0f, 0, 0, 0, 0, 0, 2560f, 15f);
        /// <summary>[미상] 점프 근접 중 중력(u/f²)·낙하 상한(u/f). 원본 미확인 — 점프 중력으로 채움.</summary>
        public float jumpMeleeGravity = 0.074f;
        public float jumpMeleeFallMaxVy = 0.526f;
        /// <summary>대시 근접 정지 거리(u).</summary>
        public float dashMeleeStopDistance = 15f;

        // ---- 충격치 [2.1·6.3] ----
        /// <summary>충격치 누적값이 매 틱 줄어드는 양.</summary>
        public float impactDecay = 1f;
        public float staggerThreshold = 200f;
        public float downThreshold = 600f;
        public float blownThreshold = 1200f;
        /// <summary>반응 뒤 무적(점프 중·대시 중 경직급 피격, 경직 진입, 가드 경직 뒤).</summary>
        public int reactInvulnFrames = 16;
        public float impactA1First = 500f;
        public float impactA1Second = 1100f;
        public float impactC1 = 1100f;
        public float impactDashMelee = 1100f;
        public float impactJumpMelee = 1100f;
        /// <summary>강타(A2·B2) 충격치.</summary>
        public float impactStrong = 1100f;

        // ---- 경직 [2.2] ----
        public int staggerFrames = 28;
        /// <summary>공격 방향으로 밀리는 속력(u/f). 히트스톱이 아닌 틱마다, 근접 감속 × 0.78과 함께.</summary>
        public float staggerSpeed = 1.301f;

        // ---- 다운 (근접 다운) [2.2] ----
        public float downLaunchVy = 1.4f;
        public float downGravity = 0.451f;
        public float downFallMaxVy = 4.001f;
        public float downSpeed = 1.6f;
        /// <summary>띄움 단계 최소 길이 (착지 + 이 틱 이상).</summary>
        public int downLaunchMinFrames = 24;
        public int lyingFrames = 40;
        /// <summary>누움 진입 시 작은 튐.</summary>
        public float lyingEntryVy = 0.2f;
        /// <summary>누움 수평 속력 = 기준 × (lyingDecelFrames − 경과) / lyingDecelFrames.</summary>
        public float lyingDecelFrames = 120f;
        public float lyingGravityScale = 0.5f;
        /// <summary>누움 중 착지할 때 vy × −이 값으로 튄다.</summary>
        public float lyingBounce = 0.667f;
        /// <summary>[미상] 튐이 이 속력(u/f)보다 작아지면 멈춘다 ("작아지면 멈춤"의 문턱).</summary>
        public float lyingBounceStopVy = 0.1f;

        // ---- 날아감 [2.2] ----
        public float blownSpeed = 2.401f;
        public int blownLaunchMinFrames = 32;

        // ---- 공중 다운 [2.2] ----
        public float airDownVy = 1.901f;
        public float airDownSpeed = 2.0f;
        public int airDownMaxFrames = 200;
        public int airDownLyingFrames = 70;

        // ---- 기상 [2.3] ----
        public int wakeWaitFrames = 56;
        /// <summary>대기 타이머가 이 값 미만일 때만 공격 선입력을 받는다.</summary>
        public int wakeAttackWindow = 12;
        public int wakeRiseFrames = 30;
        /// <summary>기상 모션이 끝난 뒤 무적.</summary>
        public int wakeInvulnFrames = 64;
        /// <summary>선입력 공격으로 바로 일어날 때 무적.</summary>
        public int wakeActionInvulnFrames = 16;

        // ---- 가드 [1.5·6.3] ----
        /// <summary>가드 중 들어가는 피해·충격치 비율.</summary>
        public float guardDamageScale = 0.06f;
        public float guardImpactScale = 0.10f;
        public float guardStrongImpactScale = 0.20f;
        /// <summary>상대 방위 오차가 ±이 각도(°) 안이어야 막는다.</summary>
        public float guardHalfAngle = 112.5f;
        public int guardStaggerFrames = 16;
        public float guardStaggerSpeed = 1.4f;

        // ---- 히트스톱 [2.4·9] ----
        public int hitStopFrames = 15;
        /// <summary>근접 피격 때 피격자가 받는 근접 무적.</summary>
        public int hitInvulnFrames = 4;

        // ---- 선입력 [8] ----
        /// <summary>근접 변환 창 (사실상 근접 선입력).</summary>
        public int meleeBufferFrames = 4;

        /// <summary>원본값 그대로의 새 사본.</summary>
        public static VoCombatSpec Original() => new VoCombatSpec();

        public VoCombatSpec Clone() => (VoCombatSpec)MemberwiseClone();

        /// <summary>모든 칸을 "이름=값" 한 줄씩으로 (스모크 테스트 로그·비교용).</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (FieldInfo field in typeof(VoCombatSpec).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                sb.Append(field.Name).Append('=').Append(Format(field.GetValue(this))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>원본과 다른 칸만 "이름: 원본 → 지금" 으로.</summary>
        public string DescribeDifferences()
        {
            VoCombatSpec original = Original();
            var sb = new StringBuilder();
            foreach (FieldInfo field in typeof(VoCombatSpec).GetFields(BindingFlags.Public | BindingFlags.Instance))
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

    /// <summary>
    /// 근접 구역 판정 (전투 사양서 1.1) — 순수 계산. 거리만 보지 않는다: 높이차 창 → 안쪽 원 → 원뿔 순서.
    /// </summary>
    public static class VoMeleeZone
    {
        /// <summary>
        /// distanceU = 두 기체 중심 수평 거리(u), angleOffDegrees = 내 정면에서 상대까지 각(°, 부호 무관),
        /// heightDiffU = 내 높이 − 상대 높이(u). ignoreHeight = 높이차 창을 보지 않는다(점프 이륙·점프 근접 예외).
        /// inner = 안쪽 원 안(밀착)인지.
        /// </summary>
        public static bool Evaluate(VoCombatSpec c, float distanceU, float angleOffDegrees, float heightDiffU, bool ignoreHeight, out bool inner)
        {
            inner = false;
            // 1) 높이차 창
            if (!ignoreHeight && (heightDiffU < c.zoneHeightMin || heightDiffU > c.zoneHeightMax)) return false;
            // 2) 안쪽 원: 각도와 무관
            if (distanceU <= c.zoneInner)
            {
                inner = true;
                return true;
            }
            // 3) 원뿔: 꼭짓점을 정면 반대쪽으로 물려서, 가까울수록 허용 각이 넓다
            if (distanceU >= c.zoneOuter) return false;
            float half = c.zoneHalfAngle * Mathf.Deg2Rad;
            float back = 2f * c.zoneInner * Mathf.Sin(half);
            float a = Mathf.Abs(Mathf.DeltaAngle(0f, angleOffDegrees)) * Mathf.Deg2Rad;
            float forward = distanceU * Mathf.Cos(a) + back;
            float side = distanceU * Mathf.Sin(a);
            return Mathf.Atan2(side, forward) < half;
        }
    }
}
