using MyDongari.Core;
using UnityEngine;

namespace MyDongari.Combat
{
    public class MeleeDummy : MonoBehaviour
    {
        [SerializeField] private float detectionRange = 5f;
        [SerializeField] private float attackRange = 3f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private float moveSpeed = 5f;

        private Transform _player;
        private MechIdentity _identity;
        private MeleeSystem _meleeSystem;
        private Health _health;
        private float _attackTimer = 0f;
        private Rigidbody _rb;
        private ExternalControlLock _controlLock;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _rb = GetComponent<Rigidbody>();
            _controlLock = GetComponent<ExternalControlLock>();
            _identity = MechIdentity.GetOrAdd(gameObject);
        }

        private void Update()
        {
            // 상대는 GameManager가 Awake 이후에 연결할 수 있으니 매 프레임 참조를 갱신
            _player = _identity.Opponent != null ? _identity.Opponent.transform : null;

            if (_health != null && _health.IsDead) return;
            if (_player == null) return;

            _attackTimer += Time.deltaTime;

            float dist = Vector3.Distance(transform.position, _player.position);

            if (dist <= attackRange)
            {
                TryAttack();
            }
        }

        private void FixedUpdate()
        {
            _player = _identity.Opponent != null ? _identity.Opponent.transform : null;
            if (_health != null && _health.IsDead) return;
            if (_player == null) return;
            if (_controlLock != null && _controlLock.IsLocked) return; // 앵커클로 등에 잡혀있으면 양보

            float dist = Vector3.Distance(transform.position, _player.position);

            if (dist <= detectionRange && dist > attackRange)
            {
                Vector3 dir = (_player.position - transform.position).normalized;
                _rb.AddForce(dir * moveSpeed, ForceMode.Acceleration);

                Vector3 flat = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                if (flat.magnitude > moveSpeed)
                {
                    flat = flat.normalized * moveSpeed;
                    _rb.linearVelocity = new Vector3(flat.x, _rb.linearVelocity.y, flat.z);
                }
            }
        }

        private void TryAttack()
        {
            if (_attackTimer < attackCooldown) return;
            _attackTimer = 0f;

            if (_meleeSystem != null)
            {
                _meleeSystem.TryMeleeAttack();
            }
        }
    }
}