using UnityEngine;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Combat.Skills
{
    public class ImpactSlam : MonoBehaviour
    {
        private TuningRef _tuning;
        private BernardTuning.ImpactSlamValues T => _tuning.Get(this).ImpactSlam;

        /// <summary>슬램을 발동한 순간(맞든 빗나가든). 외형·로그용.</summary>
        public event System.Action OnSlamStarted;

        private float _cooldownTimer = 0f;
        private MechIdentity _identity;

        private void Awake()
        {
            _identity = MechIdentity.GetOrAdd(gameObject);
        }

        /// <summary>라운드 시작: 쿨타임을 비워 바로 쓸 수 있게.</summary>
        public void ResetForRound()
        {
            _cooldownTimer = T.cooldown;
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
            BernardTuning.ImpactSlamValues t = T;
            if (_cooldownTimer < t.cooldown) return null;
            _cooldownTimer = 0f;
            // 맞든 빗나가든 내리찍는 순간 (외형·로그용)
            OnSlamStarted?.Invoke();

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null) return null;

            Vector3 dirToTarget = (opponent.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToTarget);
            if (angle > t.angle * 0.5f) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, t.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                // 살짝 띄워 올리는 넉백 (기존 연출 유지)
                Vector3 knockDir = dirToTarget;
                knockDir.y = t.knockbackLift;

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Slam,
                    Damage = t.damage,
                    Attacker = gameObject,
                    KnockbackDirection = knockDir,
                    KnockbackForce = t.knockback
                };
                // 상대 콜라이더가 여러 개여도 한 번만 맞는다
                return MeleeSystem.DeliverHit(opponent.gameObject, info);
            }
            return null;
        }
    }
}
