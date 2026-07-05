using UnityEngine;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.Combat
{
    public class MeleeSystem : MonoBehaviour
    {
        [Header("근접 설정")]
        [SerializeField] private float meleeRange = 3f;
        [SerializeField] private float meleeDamage = 25f;
        [SerializeField] private float meleeCooldown = 0.5f;

        [Header("가드 설정")]
        [SerializeField] private float guardBoostDrain = 15f;
        [SerializeField] private float guardDamageReduction = 0.7f;
        [SerializeField] private float parryWindow = 0.15f;

        [Header("태클 설정")]
        [SerializeField] private float tackleDamage = 30f;
        [SerializeField] private float tackleKnockback = 8f;
        [SerializeField] private float tackleRange = 5f;
        [SerializeField] private float tackleCooldown = 1f;
        [SerializeField] private float attackerStunDuration = 0.5f;
        [SerializeField] private float victimStunDuration = 1.7f;

        public bool IsGuarding { get; private set; }
        public bool IsParrying { get; private set; }
        public bool IsStunned { get; private set; }
        public float MeleeTimer => _meleeTimer;
        public float MeleeCooldown => meleeCooldown;

        private float _meleeTimer = 0f;
        private float _parryTimer = 0f;
        private float _tackleTimer = 999f;
        private float _stunTimer = 0f;
        private bool _parryActive = false;

        private BoostGauge _boostGauge;
        private MechStateMachine _stateMachine;

        private void Awake()
        {
            _boostGauge = GetComponent<BoostGauge>();
            _stateMachine = GetComponent<MechStateMachine>();
        }

        private void Update()
        {
            _meleeTimer += Time.deltaTime;
            _tackleTimer += Time.deltaTime;

            if (IsStunned)
            {
                _stunTimer += Time.deltaTime;
                if (_stunTimer >= 0f)
                {
                    IsStunned = false;
                }
                return;
            }

            HandleGuard();
            HandleParry();
        }

        private void HandleGuard()
        {
            if (_boostGauge == null) return;

            if (UnityEngine.InputSystem.Mouse.current.rightButton.isPressed)
            {
                if (_boostGauge.IsOverheated)
                {
                    IsGuarding = false;
                    return;
                }
                IsGuarding = true;
                _boostGauge.TryConsume(guardBoostDrain * Time.deltaTime);

                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null) rb.linearDamping = 8f;
            }
            else
            {
                if (IsGuarding)
                {
                    Rigidbody rb = GetComponent<Rigidbody>();
                    if (rb != null) rb.linearDamping = 2f;
                }
                IsGuarding = false;
            }
        }

        private void HandleParry()
        {
            if (IsGuarding && UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame)
            {
                _parryActive = true;
                _parryTimer = 0f;
                IsParrying = true;
            }

            if (_parryActive)
            {
                _parryTimer += Time.deltaTime;
                if (_parryTimer >= parryWindow)
                {
                    _parryActive = false;
                    IsParrying = false;
                }
            }
        }

        public void ApplyStun(float duration)
        {
            IsStunned = true;
            _stunTimer = -duration;
        }

        public float ApplyDamage(float incomingDamage)
        {
            if (IsParrying)
            {
                return 0f;
            }
            if (IsGuarding)
            {
                return incomingDamage * (1f - guardDamageReduction);
            }
            return incomingDamage;
        }

        public void TryTackle(float chargePower)
        {
            if (_tackleTimer < tackleCooldown) return;
            _tackleTimer = 0f;

            float finalDamage = tackleDamage * (1f + chargePower);
            float finalKnockback = tackleKnockback * (1f + chargePower);

            Collider[] hits = Physics.OverlapSphere(transform.position, tackleRange);
            foreach (Collider hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;

                MeleeSystem enemyMelee = hit.GetComponent<MeleeSystem>();
                if (enemyMelee != null && enemyMelee.IsParrying)
                {
                    ApplyStun(attackerStunDuration);
                    return;
                }

                Health health = hit.GetComponent<Health>();
                Rigidbody rb = hit.GetComponent<Rigidbody>();

                float damage = finalDamage;
                if (enemyMelee != null && enemyMelee.IsGuarding)
                {
                    damage = enemyMelee.ApplyDamage(finalDamage);
                }

                if (health != null) health.TakeDamage(damage);
                if (rb != null)
                {
                    Vector3 knockDir = (hit.transform.position - transform.position).normalized;
                    rb.AddForce(knockDir * finalKnockback, ForceMode.Impulse);
                }

                if (enemyMelee != null) enemyMelee.ApplyStun(victimStunDuration);
                return;
            }

            ApplyStun(attackerStunDuration);
        }

        public void TryMeleeAttack()
        {
            if (_meleeTimer < meleeCooldown) return;
            _meleeTimer = 0f;

            Collider[] hits = Physics.OverlapSphere(transform.position, meleeRange);
            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject) continue;
                if (!hit.CompareTag("Enemy") && !hit.CompareTag("Player")) continue;

                MeleeSystem enemyMelee = hit.GetComponent<MeleeSystem>();
                Health health = hit.GetComponent<Health>();

                if (enemyMelee != null && enemyMelee.MeleeTimer < enemyMelee.MeleeCooldown * 0.5f)
                {
                    Rigidbody myRb = GetComponent<Rigidbody>();
                    Rigidbody enemyRb = hit.GetComponent<Rigidbody>();

                    Vector3 knockDir = (hit.transform.position - transform.position).normalized;
                    if (myRb != null) myRb.AddForce(-knockDir * 10f, ForceMode.Impulse);
                    if (enemyRb != null) enemyRb.AddForce(knockDir * 10f, ForceMode.Impulse);
                    return;
                }

                float damage = meleeDamage;
                if (enemyMelee != null)
                {
                    damage = enemyMelee.ApplyDamage(meleeDamage);
                }

                if (health != null)
                {
                    health.TakeDamage(damage);
                }
            }
        }
    }
}