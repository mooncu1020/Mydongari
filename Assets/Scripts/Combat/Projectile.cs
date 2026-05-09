using UnityEngine;

namespace MyDongari.Combat
{
    public class Projectile : MonoBehaviour
    {
        [Header("투사체 설정")]
        [SerializeField] private float speed = 30f;
        [SerializeField] private float homingStrength = 5f;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float damage = 10f;

        private Transform _target;
        private Vector3 _direction;
        private float _lifeTimer = 0f;
        private Rigidbody _rb;

        public void Init(Vector3 direction, Transform target)
        {
            _direction = direction.normalized;
            _target = target;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = false;
            }
        }

        private void Update()
        {
            _lifeTimer += Time.deltaTime;
            if (_lifeTimer >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
        }

        private bool _passed = false;

        private void FixedUpdate()
        {
            if (_target != null && !_passed)
            {
                Transform cam = GameObject.FindWithTag("MainCamera").transform;
                Vector3 dirToTarget = (_target.position - transform.position).normalized;

                float dot = Vector3.Dot(_direction.normalized, dirToTarget);
                if (dot < 0f)
                {
                    _passed = true;
                }
                else
                {
                    float angle = Vector3.Angle(cam.forward, dirToTarget);
                    float maxAngle = 30f;
                    float angleFactor = Mathf.Clamp01(1f - (angle / maxAngle));

                    float distance = Vector3.Distance(transform.position, _target.position);
                    float maxDistance = 50f;
                    float distanceFactor = Mathf.Clamp01(1f - (distance / maxDistance));

                    float currentHoming = homingStrength * (angleFactor + distanceFactor) * 0.5f;
                    _direction = Vector3.Lerp(_direction, dirToTarget, currentHoming * Time.fixedDeltaTime);
                }
            }

            _rb.linearVelocity = _direction * speed;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Enemy"))
            {
                Health health = other.GetComponent<Health>();
                if (health != null)
                {
                    health.TakeDamage(damage);
                }
                Destroy(gameObject);
            }
        }
    }
}