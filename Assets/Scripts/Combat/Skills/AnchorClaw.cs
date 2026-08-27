using MyDongari.Combat;
using MyDongari.Movement;
using MyDongari.Core;
using UnityEngine;

namespace MyDongari.Combat.Skills
{
    public class AnchorClaw : MonoBehaviour
    {
        [Header("앵커 설정")]
        [SerializeField] private float pullSpeed = 15f;
        [SerializeField] private float pullDuration = 1.5f;
        [SerializeField] private float pileDriverDamage = 50f;
        [SerializeField] private float cooldown = 5f;

        [Header("훅 연출")]
        [Tooltip("그랩이 박히는 순간 확 끌려오는 초기 속도. pullSpeed보다 커야 '훅' 느낌이 남")]
        [SerializeField] private float hookImpactSpeed = 32f;
        [Tooltip("초기 훅 속도에서 pullSpeed로 줄어드는 데 걸리는 시간(초)")]
        [SerializeField] private float hookEaseTime = 0.2f;
        [Tooltip("그랩이 박히는 순간 타겟한테 거는 짧은 경직. 저항 못 하고 훅 당하는 느낌용")]
        [SerializeField] private float hookImpactStun = 0.25f;

        private Transform _target;
        private Rigidbody _targetRb;
        private ExternalControlLock _targetLock;
        private Rigidbody _selfRb;
        private ExternalControlLock _selfLock;
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
            _selfRb = GetComponent<Rigidbody>();
            _selfLock = GetComponent<ExternalControlLock>();
            if (_selfLock == null) _selfLock = gameObject.AddComponent<ExternalControlLock>();
        }

        private void Update()
        {
            _cooldownTimer += Time.deltaTime;

            if (_isPulling)
            {
                _pullTimer += Time.deltaTime;
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

            // 당기는 동안 상대의 이동 로직(플레이어 State머신 / AI)이 동시에
            // 자기 힘을 넣지 못하게 잠근다. 없으면 자동으로 붙여줌.
            _targetLock = _target.GetComponent<ExternalControlLock>();
            if (_targetLock == null) _targetLock = _target.gameObject.AddComponent<ExternalControlLock>();
            _targetLock.Acquire();

            // 그랩이 박히는 순간: 저항 못 하게 짧은 경직을 걸어서 "훅 하고 걸렸다"는 걸 확실히 함
            MeleeSystem targetMelee = _target.GetComponent<MeleeSystem>();
            if (targetMelee != null) targetMelee.ApplyStun(hookImpactStun);

            // 나(공격자) 쪽도 같이 잠근다. 안 그러면 이동 입력을 계속 누르고 있을 때
            // 내가 타겟 쪽으로 걸어 들어가는 게 "내가 당겨지는" 것처럼 보인다.
            // 당기는 동안은 상대만 움직여야 "내가 당긴다"는 느낌이 정확히 산다.
            _selfLock.Acquire();
            if (_selfRb != null)
            {
                _selfRb.linearVelocity = new Vector3(0f, _selfRb.linearVelocity.y, 0f);
            }
        }

        private void FixedUpdate()
        {
            if (!_isPulling) return;

            if (_target == null || _targetRb == null)
            {
                EndPull();
                return;
            }

            Vector3 dirToMe = (transform.position - _target.position).normalized;

            // 박히는 순간엔 확 끌려오다가(hookImpactSpeed) hookEaseTime에 걸쳐 pullSpeed로 잦아듦
            float easeT = hookEaseTime > 0f ? Mathf.Clamp01(_pullTimer / hookEaseTime) : 1f;
            float currentSpeed = Mathf.Lerp(hookImpactSpeed, pullSpeed, easeT);

            _targetRb.linearVelocity = dirToMe * currentSpeed;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist < 3f || _pullTimer >= pullDuration)
            {
                _targetRb.linearVelocity = Vector3.zero;
                EndPull();
            }
        }

        private void EndPull()
        {
            _isPulling = false;
            ReleaseTargetLock();
            _selfLock.Release();
        }

        private void ReleaseTargetLock()
        {
            if (_targetLock != null) _targetLock.Release();
            _targetLock = null;
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
            }

            _target = null;
        }
    }
}