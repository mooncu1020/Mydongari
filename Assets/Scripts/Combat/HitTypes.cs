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

    /// <summary>MeleeSystem.ReceiveHit이 판단한 피격 결과. 로그·히트스톱·후속 처리용.</summary>
    public enum HitResult
    {
        Hit,
        Guarded,
        GuardBroken,
        Parried,
        Clash
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

        /// <summary>런지·차징 태클은 가드를 뚫는다 (감쇄 없음, 가드 히트 증가 없음)</summary>
        public bool PiercesGuard => Type == AttackType.Lunge || Type == AttackType.Tackle;

        /// <summary>앵커 클로(잡기)로 이어지는 파일드라이버는 패리가 안 된다</summary>
        public bool IgnoresParry => Type == AttackType.PileDriver;
    }
}
