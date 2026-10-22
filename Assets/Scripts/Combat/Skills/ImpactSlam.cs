using UnityEngine;
using MyDongari.Core;

namespace MyDongari.Combat.Skills
{
    public class ImpactSlam : MonoBehaviour
    {
        [Header("임팩트 슬램 설정")]
        [SerializeField] private float slamRange = 8f;
        [SerializeField] private float slamAngle = 90f;
        [SerializeField] private float slamDamage = 60f;
        [SerializeField] private float slamKnockback = 15f;
        [Tooltip("넉백 방향의 위쪽 성분. 클수록 더 띄운다")]
        [SerializeField] private float slamKnockbackLift = 0.3f;
        [SerializeField] private float cooldown = 8f;

        private float _cooldownTimer = 0f;
        private MechIdentity _identity;

        private void Awake()
        {
            _identity = MechIdentity.GetOrAdd(gameObject);
        }

        private void Update()
        {
            _cooldownTimer += Time.deltaTime;
        }

        /// <summary>
        /// 전방 부채꼴 충격파. 쿨다운이면 null, 빗나가면 null, 맞으면 피격자가 판단한 결과.
        /// 피해·넉백은 ReceiveHit을 거쳐 가드(감쇄)·패리(무효, 넉백 없음)·가드 깨짐이 적용된다.
        /// </summary>
        public HitResult? TryActivate()
        {
            if (_cooldownTimer < cooldown) return null;
            _cooldownTimer = 0f;

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null) return null;

            Vector3 dirToTarget = (opponent.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToTarget);
            if (angle > slamAngle * 0.5f) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, slamRange);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                // 살짝 띄워 올리는 넉백 (기존 연출 유지)
                Vector3 knockDir = dirToTarget;
                knockDir.y = slamKnockbackLift;

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Slam,
                    Damage = slamDamage,
                    Attacker = gameObject,
                    KnockbackDirection = knockDir,
                    KnockbackForce = slamKnockback
                };
                // 상대 콜라이더가 여러 개여도 한 번만 맞는다
                return MeleeSystem.DeliverHit(opponent.gameObject, info);
            }
            return null;
        }
    }
}
