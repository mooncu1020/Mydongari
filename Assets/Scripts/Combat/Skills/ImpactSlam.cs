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

        public void TryActivate()
        {
            if (_cooldownTimer < cooldown) return;
            _cooldownTimer = 0f;
            Collider[] hits = Physics.OverlapSphere(transform.position, slamRange);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                GameObject target = _identity.Opponent.gameObject;

                Vector3 dirToTarget = (target.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, dirToTarget);

                if (angle > slamAngle * 0.5f) continue;

                Health health = target.GetComponent<Health>();
                Rigidbody rb = target.GetComponent<Rigidbody>();

                if (health != null)
                {
                    health.TakeDamage(slamDamage);
                }

                if (rb != null)
                {
                    Vector3 knockDir = (target.transform.position - transform.position).normalized;
                    knockDir.y = 0.3f;
                    rb.AddForce(knockDir.normalized * slamKnockback, ForceMode.Impulse);
                }
                // 상대 콜라이더가 여러 개여도 한 번만 맞는다
                return;
            }
        }
    }
}
