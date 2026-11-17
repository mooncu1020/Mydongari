using UnityEngine;
using MyDongari.Core.VirtualOn;

namespace MyDongari.Game.Combat
{
    /// <summary>피격 반응 단계 (전투 사양서 2.1). 뒤로 갈수록 무겁다 — 같은 틱 여러 발이면 가장 무거운 것이 남는다.</summary>
    public enum Reaction
    {
        /// <summary>반응 없음 (피해만).</summary>
        None = 0,
        /// <summary>반응 없이 무적만 (점프 중·지상 대시 중의 경직급 피격 — 대시는 슈퍼아머).</summary>
        InvulnOnly = 1,
        /// <summary>가드 경직 (막음).</summary>
        GuardStagger = 2,
        /// <summary>경직.</summary>
        Stagger = 3,
        /// <summary>다운.</summary>
        Down = 4,
        /// <summary>공중 다운 (점프 중 다운 문턱 이상).</summary>
        AirDown = 5,
        /// <summary>날아감 (강타, 또는 판정값이 날아감 문턱 이상 — 가드 무시).</summary>
        Blown = 6
    }

    /// <summary>
    /// 충격치 누적값 (전투 사양서 2.1) — 순수 로직. 피격마다 더하고 매 틱 줄어든다. 다운·날아감·공중 다운에 들어가면 0으로,
    /// 경직으로는 지우지 않는다(그래서 경직 중 추가 피격은 다운으로 이어지기 쉽다). 행동 가산치는 근접이 0이라 생략한다.
    /// </summary>
    public class ImpactMeter
    {
        public float Value { get; private set; }

        public void Add(float impact)
        {
            Value += Mathf.Max(0f, impact);
        }

        /// <summary>틱마다: decay만큼 줄어든다 (0 아래로는 안 내려간다).</summary>
        public void Tick(float decay)
        {
            Value = Mathf.Max(0f, Value - decay);
        }

        public void Clear()
        {
            Value = 0f;
        }
    }

    /// <summary>피격 판단에 필요한 피격자 상태 (판단 시점 = 틱 맨 앞, 두 기체 모두 움직이기 전).</summary>
    public struct DefenderPosture
    {
        /// <summary>판정을 통째로 무시 (다운 중: 띄움·누움·기상 대기·기상 모션).</summary>
        public bool Invulnerable;
        /// <summary>근접 무적 중 (근접 계열만 무시).</summary>
        public bool MeleeInvulnerable;
        public bool Parrying;
        public bool Guarding;
        /// <summary>내 정면(Unity Y 회전, °). 가드가 막는 방향(정면 ±guardHalfAngle) 계산용.</summary>
        public float FacingYaw;
        /// <summary>점프 중(상승·하강)·그 밖의 공중.</summary>
        public bool Airborne;
        /// <summary>지상 대시 중 (경직급 피격에 반응하지 않는다).</summary>
        public bool Dashing;
        /// <summary>지금 충격치 누적값.</summary>
        public float Impact;
    }

    /// <summary>판단이 끝난 피격 하나. 적용(피해·반응)은 피격자가 자기 틱의 정산 단계에서 한다.</summary>
    public struct HitDecision
    {
        public HitInfo Info;
        public HitResult Result;
        public float Damage;
        /// <summary>누적값에 더해질 충격치 (가드 중이면 10%, 강타는 20%).</summary>
        public float ImpactAdded;
        public Reaction Reaction;
        /// <summary>이 타격으로 때린 쪽도 히트스톱이 걸리는지.</summary>
        public bool StopsAttacker;
        /// <summary>지상 대시의 슈퍼아머로 버틴 타격 (경직급인데 반응 없이 무적만). 연출·로그용.</summary>
        public bool ArmoredByDash;
    }

    /// <summary>
    /// 피격 판단 규칙 (전투 사양서 2.1 판정 순서 + 1.5 가드 + 6.3 보정) — 순수 계산.
    /// 순서: 무적이면 무시 → (맞딜) → (패리) → 피해·누적 → 강타면 날아감 → 점프 중(≥600 공중 다운, 200~600 반응 없이 무적) → &lt;200 반응 없음
    /// → 200~600: 지상 대시 중이면 반응 없이 무적(슈퍼아머), 아니면 경직 → 600~1200: 다운 → ≥1200 날아감.
    /// 가드(정면 ±112.5° 안의 근접): 피해 6%·충격치 10%(강타 20%)만 들어가고, 강타이거나 판정값 ≥1200이면 가드가 깨져 날아가고, 그 밖엔 가드 경직.
    /// 맞딜·패리는 원본에 없는 우리 규칙이다.
    /// </summary>
    public static class HitRules
    {
        /// <summary>공격 방향(공격자 → 피격자)으로 온 타격을 정면이 facingYaw인 가드가 막을 수 있는지: 상대 방위 오차 ±guardHalfAngle 안.</summary>
        public static bool GuardCovers(VoCombatSpec c, float facingYaw, Vector3 attackDirection)
        {
            attackDirection.y = 0f;
            if (attackDirection.sqrMagnitude < 0.000001f) return true;
            // 공격자는 공격 방향의 반대쪽에 있다
            float attackerBearing = Mathf.Atan2(-attackDirection.x, -attackDirection.z) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(facingYaw, attackerBearing)) <= c.guardHalfAngle;
        }

        /// <summary>
        /// 타격 하나를 판단한다. impactBefore = 이 타격 전의 누적값(같은 틱 앞선 타격 포함), clash = 같은 틱 맞교환인지.
        /// </summary>
        public static HitDecision Decide(VoCombatSpec c, HitInfo info, DefenderPosture p, float impactBefore, bool clash)
        {
            var d = new HitDecision { Info = info, Reaction = Reaction.None };
            // 1) 무적: 통째로 무시 (피해·충격치 0)
            if (p.Invulnerable || (info.IsMelee && p.MeleeInvulnerable))
            {
                d.Result = HitResult.Invulnerable;
                return d;
            }
            // 우리 규칙: 같은 틱 맞교환 = 맞딜, 패리 = 무효
            if (clash)
            {
                d.Result = HitResult.Clash;
                return d;
            }
            if (p.Parrying && !info.IgnoresParry)
            {
                d.Result = HitResult.Parried;
                return d;
            }

            // 4) 피해·충격치 (가드 중이면 줄어든다 — 강타도 피해는 6%만)
            bool guarded = p.Guarding && info.IsMelee && !info.IgnoresGuard && GuardCovers(c, p.FacingYaw, info.Direction);
            d.Damage = guarded ? info.Damage * c.guardDamageScale : info.Damage;
            d.ImpactAdded = guarded ? info.Impact * (info.IsStrong ? c.guardStrongImpactScale : c.guardImpactScale) : info.Impact;
            float total = impactBefore + d.ImpactAdded;
            d.Result = HitResult.Hit;
            d.StopsAttacker = info.StopsAttacker;

            // 5) 강타: 무조건 날아감 (가드 불능)
            if (info.IsStrong)
            {
                d.Reaction = Reaction.Blown;
                if (guarded) d.Result = HitResult.GuardBroken;
                return d;
            }
            // 가드: 판정값이 날아감 문턱 이상이면 깨지고, 아니면 가드 경직 (반응 단계 무시). 가드됨이면 때린 쪽도 멈춘다
            if (guarded)
            {
                if (total >= c.blownThreshold)
                {
                    d.Reaction = Reaction.Blown;
                    d.Result = HitResult.GuardBroken;
                }
                else
                {
                    d.Reaction = Reaction.GuardStagger;
                    d.Result = HitResult.Guarded;
                    d.StopsAttacker = true;
                }
                return d;
            }
            // 6) 점프 중: 다운 문턱 이상이면 공중 다운, 경직급이면 반응 없이 무적
            if (p.Airborne)
            {
                if (total >= c.downThreshold) d.Reaction = Reaction.AirDown;
                else if (total >= c.staggerThreshold) d.Reaction = Reaction.InvulnOnly;
                return d;
            }
            // 7) 경직 문턱 미만: 반응 없음
            if (total < c.staggerThreshold) return d;
            // 8) 경직급: 지상 대시 중이면 슈퍼아머
            if (total < c.downThreshold)
            {
                d.Reaction = p.Dashing ? Reaction.InvulnOnly : Reaction.Stagger;
                d.ArmoredByDash = p.Dashing;
                return d;
            }
            // 9) 다운 / 10) 날아감
            d.Reaction = total < c.blownThreshold ? Reaction.Down : Reaction.Blown;
            return d;
        }
    }
}
