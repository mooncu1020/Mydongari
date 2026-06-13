using MyDongari.Combat;
using MyDongari.Movement;
using UnityEngine;

namespace MyDongari.Combat.Skills
{
    public class AnchorClaw : MonoBehaviour
    {
        [Header("앵커 설정")]
        [SerializeField] private float anchorRange = 20f;
        [SerializeField] private float pullSpeed = 15f;
        [SerializeField] private float pullDuration = 1.5f;
        [SerializeField] private float pileDriverDamage = 50f;
        [SerializeField] private float cooldown = 5f;

        private Transform _target;
        private Rigidbody _targetRb;
        private float _cooldownTimer = 0f;
        private float _pullTimer = 0f;
        private bool _isPulling = false;
        private bool _pileDriverReady = false;
        public bool IsPileDriverReady => _pileDriverReady;

        private LockOnSystem _lockOnSystem;
        private MechStateMachine _stateMachine;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
        }

        private void Update()
        {
            _cooldownTimer += Time.deltaTime;

            if (_isPulling)
            {
                HandlePull();
            }
        }

        public void TryActivate()
        {
            if (_cooldownTimer < cooldown) return;
            if (_lockOnSystem.Target == null) return;

            _target = _lockOnSystem.Target;
            _targetRb = _target.GetComponent<Rigidbody>();
            _isPulling = true;
            _pullTimer = 0f;
            _pileDriverReady = true;
            _cooldownTimer = 0f;

            Debug.Log("앵커 클로 발사");
        }

        private void HandlePull()
        {
            if (_target == null || _targetRb == null)
            {
                _isPulling = false;
                return;
            }

            _pullTimer += Time.deltaTime;

            Vector3 dirToMe = (transform.position - _target.position).normalized;
            _targetRb.linearVelocity = dirToMe * pullSpeed;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist < 3f || _pullTimer >= pullDuration)
            {
                _isPulling = false;
                _targetRb.linearVelocity = Vector3.zero;
                Debug.Log("앵커 클로 도달");
            }
        }

        public void TryPileDriver()
        {
            if (!_pileDriverReady) return;
            if (_target == null) return;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist > 4f) return;

            _pileDriverReady = false;

            Health health = _target.GetComponent<Health>();
            MeleeSystem enemyMelee = _target.GetComponent<MeleeSystem>();

            float damage = pileDriverDamage;
            if (enemyMelee != null)
            {
                damage = enemyMelee.ApplyDamage(pileDriverDamage);
            }

            if (health != null)
            {
                health.TakeDamage(damage);
                Debug.Log("파일드라이버: " + damage);
            }

            _target = null;
        }
    }
}