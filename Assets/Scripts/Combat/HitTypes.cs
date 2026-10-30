using UnityEngine;

namespace MyDongari.Combat
{
    /// <summary>공격 종류. 가드 감쇄율·관통·패리 반사 여부가 이걸로 갈린다.</summary>
    public enum AttackType
    {
        Punch,
        DashAttack,
        Tackle,
        Lunge,
        Slam,
        PileDriver,
        Projectile
    }

    /// <summary>
    /// 공격 세기 등급 (N1). 히트스톱·화면 흔들림·피격 반응·이펙트 크기가 이걸로 갈린다.
    /// 약 = 콤보 원·투, 중 = 쓰리·대시 어택, 강 = 런지·태클·파일 드라이버·슬램.
    /// </summary>
    public enum AttackStrength
    {
        Light,
        Medium,
        Heavy
    }

    /// <summary>MeleeSystem.ReceiveHit이 판단한 피격 결과. 로그·히트스톱·후속 처리용.</summary>
    public enum HitResult
    {
        Hit,
        Guarded,
        GuardBroken,
        Parried,
        Clash,
        /// <summary>넉다운 기상 무적 중이라 판정 무시 (R1). 피해·넉백·히트스톱·이펙트 없음, 로그만</summary>
        Invulnerable
    }

    /// <summary>공격자가 피격자에게 넘기는 한 번의 타격 정보.</summary>
    public struct HitInfo
    {
        public AttackType Type;
        public float Damage;
        public GameObject Attacker;
        /// <summary>피격자를 밀어낼 방향 (정규화 안 해도 됨)</summary>
        public Vector3 KnockbackDirection;
        /// <summary>Impulse 크기. 0이면 넉백 없음</summary>
        public float KnockbackForce;
        /// <summary>그대로 맞았을 때(Hit) 피격자에게 거는 스턴. 0이면 없음</summary>
        public float StunDuration;
        /// <summary>펀치 콤보 단계 (1 원, 2 투, 3 쓰리). 펀치가 아니거나 콤보 정보가 없으면 0</summary>
        public int ComboStep;

        /// <summary>이 타격의 세기 등급.</summary>
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
                case AttackType.Slam: return AttackStrength.Heavy;
                default: return AttackStrength.Light;
            }
        }

        /// <summary>런지·차징 태클·파일드라이버(잡기 후속타)는 가드를 뚫는다 (감쇄 없음, 가드 히트 증가 없음)</summary>
        public bool PiercesGuard => PiercesGuardType(Type);

        public static bool PiercesGuardType(AttackType type)
        {
            return type == AttackType.Lunge || type == AttackType.Tackle || type == AttackType.PileDriver;
        }

        /// <summary>앵커 클로(잡기)로 이어지는 파일드라이버는 패리가 안 된다</summary>
        public bool IgnoresParry => Type == AttackType.PileDriver;
    }
}
