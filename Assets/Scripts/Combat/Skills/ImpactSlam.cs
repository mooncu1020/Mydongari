using UnityEngine;

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

        private void Update()
        {
            _cooldownTimer += Time.deltaTime;
        }

        public void TryActivate()
        {
            if (_cooldownTimer < cooldown) return;
            _cooldownTimer = 0f;

            Debug.Log("임팩트 슬램 발동");

            Collider[] hits = Physics.OverlapSphere(transform.position, slamRange);
            foreach (Collider hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;

                Vector3 dirToTarget = (hit.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, dirToTarget);

                if (angle > slamAngle * 0.5f) continue;

                Health health = hit.GetComponent<Health>();
                Rigidbody rb = hit.GetComponent<Rigidbody>();

                if (health != null)
                {
                    health.TakeDamage(slamDamage);
                    Debug.Log("슬램 히트: " + slamDamage);
                }

                if (rb != null)
                {
                    Vector3 knockDir = (hit.transform.position - transform.position).normalized;
                    knockDir.y = 0.3f;
                    rb.AddForce(knockDir.normalized * slamKnockback, ForceMode.Impulse);
                }
            }
        }
    }
}
