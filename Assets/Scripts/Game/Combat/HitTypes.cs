using UnityEngine;

namespace MyDongari.Game.Combat
{
    /// <summary>공격 종류 (우리 기술 이름). 로그·외형·패리 반사 여부가 이걸로 갈린다.</summary>
    public enum AttackType
    {
        Punch,
        DashAttack,
        Tackle,
        Lunge,
        Slam,
        PileDriver,
        Projectile,
        /// <summary>뛰어 베기 (원본 점프 근접).</summary>
        JumpSlash
    }

    /// <summary>
    /// 피격 판정 종류 (전투 사양서 2.1·2.4·6.3). 반응 단계와 히트스톱이 이걸로 갈린다.
    /// 일반 근접·강타는 때린 쪽도 히트스톱, 근접 계열 2(대시 근접)·3(점프 근접)은 때린 쪽이 멈추지 않는다. 강타는 가드 불능 + 무조건 날아감.
    /// </summary>
    public enum HitKind
    {
        Melee,
        Melee2,
        Melee3,
        Strong,
        /// <summary>사격 (근접 무적·히트스톱과 무관, 가드 불가).</summary>
        Shot
    }

    /// <summary>
    /// 공격 세기 등급 (N1). 화면 흔들림·피격 반응 포즈·이펙트 크기가 이걸로 갈린다 (연출 전용 — 히트스톱은 등급과 무관하게 15f).
    /// 약 = 콤보 원·투, 중 = 쓰리·대시 어택, 강 = 런지·태클·파일 드라이버·슬램·뛰어 베기.
    /// </summary>
    public enum AttackStrength
    {
        Light,
        Medium,
        Heavy
    }

    /// <summary>피격 판단 결과. 로그·연출·후속 처리용.</summary>
    public enum HitResult
    {
        Hit,
        Guarded,
        /// <summary>가드 중에 맞았지만 가드가 깨짐 (강타, 또는 충격치 판정값이 날아감 문턱 이상).</summary>
        GuardBroken,
        Parried,
        Clash,
        /// <summary>무적(근접 무적·다운 중)이라 판정 무시. 피해·반응·이펙트 없음, 로그만</summary>
        Invulnerable
    }

    /// <summary>공격자가 피격자에게 넘기는 한 번의 타격 기록. 틱 끝에 피격자의 기록에 쌓이고 다음 틱 맨 앞에 판단된다.</summary>
    public struct HitInfo
    {
        public AttackType Type;
        public HitKind Kind;
        public float Damage;
        /// <summary>충격치 (전투 사양서 2.1). 피격자의 누적값에 더해진다.</summary>
        public float Impact;
        public GameObject Attacker;
        /// <summary>공격자의 피격 기록 (같은 틱 맞교환 판단용). 전투 허브가 없는 공격자(투사체)는 null.</summary>
        public CombatLedger Source;
        /// <summary>공격 방향 = 피격자가 밀려나는 방향 (수평). 근접은 공격자 → 피격자.</summary>
        public Vector3 Direction;
        /// <summary>펀치 콤보 단계 (1 원, 2 투, 3 쓰리). 펀치가 아니면 0</summary>
        public int ComboStep;
        /// <summary>가드를 무시한다 (피해 감소·가드 경직 없음). 파일 드라이버.</summary>
        public bool IgnoresGuard;
        /// <summary>패리가 안 된다. 파일 드라이버(잡기 후속타).</summary>
        public bool IgnoresParry;
        /// <summary>그대로 맞았을 때 피격자를 미는 힘(Impulse). 0이면 없음</summary>
        public float KnockbackForce;
        /// <summary>그대로 맞았을 때 피격자에게 거는 경직(틱). 0이면 없음</summary>
        public int StunFrames;
        /// <summary>날아가기 전 대기(틱). 파일 드라이버가 외형 어퍼컷이 닿는 순간에 맞추려고 쓴다.</summary>
        public int LaunchDelayFrames;

        /// <summary>이 타격의 세기 등급 (연출용).</summary>
        public AttackStrength Strength => StrengthOf(Type, ComboStep);

        public static AttackStrength StrengthOf(AttackType type, int comboStep)
        {
            switch (type)
            {
                case AttackType.Punch: return comboStep >= 3 ? AttackStrength.Medium : AttackStrength.Light;
                case AttackType.DashAttack: return AttackStrength.Medium;
                case AttackType.Lunge:
                case AttackType.Tackle:
                case AttackType.PileDriver:
                case AttackType.JumpSlash:
                case AttackType.Slam: return AttackStrength.Heavy;
                default: return AttackStrength.Light;
            }
        }

        /// <summary>근접 계열인지 (근접 무적·히트스톱·가드 대상).</summary>
        public bool IsMelee => Kind != HitKind.Shot;
        /// <summary>강타: 가드 불능 + 무조건 날아감.</summary>
        public bool IsStrong => Kind == HitKind.Strong;
        /// <summary>맞히면 때린 쪽도 히트스톱이 걸리는 종류 (일반 근접·강타).</summary>
        public bool StopsAttacker => Kind == HitKind.Melee || Kind == HitKind.Strong;
        /// <summary>같은 틱에 서로 맞으면 맞딜이 되는 묶음: 펀치끼리(1), 대시 공격·태클끼리(2), 그 밖은 0(맞딜 없음).</summary>
        public int ClashFamily => Type == AttackType.Punch ? 1 : (Type == AttackType.DashAttack || Type == AttackType.Tackle ? 2 : 0);
        /// <summary>패리하면 공격자에게 피해·넉백을 되돌리는 종류 (런지·태클·대시 어택).</summary>
        public bool ReflectsOnParry => Type == AttackType.Tackle || Type == AttackType.DashAttack || Type == AttackType.Lunge;
    }

    /// <summary>전투 시간 환산. 전투의 모든 시간은 60Hz 틱 수로 센다 — 초는 외형·표시에만 쓴다.</summary>
    public static class CombatTime
    {
        public const int TickRate = 60;
        /// <summary>틱 → 초 (외형 포즈 길이·HUD 표시용).</summary>
        public static float Seconds(int frames) => frames / (float)TickRate;
        public static float Seconds(float frames) => frames / TickRate;
        /// <summary>초 → 틱 (외형 모션 길이처럼 초로 적힌 값을 전투 틱에 맞출 때).</summary>
        public static int Frames(float seconds) => Mathf.Max(0, Mathf.RoundToInt(seconds * TickRate));
    }
}
