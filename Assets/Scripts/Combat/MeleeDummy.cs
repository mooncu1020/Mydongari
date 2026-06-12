using UnityEngine;

namespace MyDongari.Combat
{
    public class MeleeDummy : MonoBehaviour
    {
        [SerializeField] private float detectionRange = 5f;
        [SerializeField] private float attackRange = 3f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private float attackDamage = 15f;
        [SerializeField] private float moveSpeed = 5f;

        private Transform _player;
        private MeleeSystem _meleeSystem;
        private Health _health;
        private float _attackTimer = 0f;
        private Rigidbody _rb;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _rb = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            _player = GameObject.FindWithTag("Player").transform;
            Debug.Log("플레이어 찾음: " + (_player != null ? _player.name : "없음"));
        }

        private void Update()
        {
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
            if (_health != null && _health.IsDead) return;
            if (_player == null) return;

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