using UnityEngine;
using MyDongari.Core;

namespace MyDongari.Combat
{
    public class Projectile : MonoBehaviour
    {
        [Header("투사체 설정")]
        [SerializeField] private float speed = 30f;
        [SerializeField] private float homingStrength = 5f;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float damage = 10f;

        [Header("소프트 호밍")]
        [Tooltip("이 각도(발사자 카메라 정면 기준) 밖이면 각도 점수 0")]
        [SerializeField] private float homingMaxAngle = 30f;
        [Tooltip("이 거리 밖이면 거리 점수 0")]
        [SerializeField] private float homingMaxDistance = 50f;

        private Transform _target;
        private Vector3 _direction;
        private float _lifeTimer = 0f;
        private Rigidbody _rb;
        private Transform _aimCamera;
        private MechIdentity _owner;

        /// <param name="aimCamera">발사자의 카메라. 호밍 각도 점수를 이 카메라 정면 기준으로 잰다</param>
        /// <param name="owner">발사자. 발사자의 상대만 맞고 발사자 자신은 절대 안 맞는다</param>
        public void Init(Vector3 direction, Transform target, Transform aimCamera, MechIdentity owner)
        {
            _direction = direction.normalized;
            _target = target;
            _aimCamera = aimCamera;
            _owner = owner;
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
                Vector3 dirToTarget = (_target.position - transform.position).normalized;

                float dot = Vector3.Dot(_direction.normalized, dirToTarget);
                if (dot < 0f)
                {
                    _passed = true;
                }
                else
                {
                    Vector3 aimForward = _aimCamera != null ? _aimCamera.forward : _direction;
                    float angle = Vector3.Angle(aimForward, dirToTarget);
                    float angleFactor = Mathf.Clamp01(1f - (angle / homingMaxAngle));

                    float distance = Vector3.Distance(transform.position, _target.position);
                    float distanceFactor = Mathf.Clamp01(1f - (distance / homingMaxDistance));

                    float currentHoming = homingStrength * (angleFactor + distanceFactor) * 0.5f;
                    _direction = Vector3.Lerp(_direction, dirToTarget, currentHoming * Time.fixedDeltaTime);
                }
            }

            _rb.linearVelocity = _direction * speed;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_owner != null && _owner.IsOpponentCollider(other))
            {
                GameObject victim = _owner.Opponent.gameObject;
                HitInfo info = new HitInfo
                {
                    Type = AttackType.Projectile,
                    Damage = damage,
                    Attacker = _owner.gameObject
                };
                MeleeSystem.DeliverHit(victim, info);
                Destroy(gameObject);
            }
        }
    }
}