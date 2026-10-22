using UnityEngine;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Input;
using MyDongari.Core;

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
        private ICombatInputSource _input;
        private MechIdentity _identity;

        private void Awake()
        {
            _boostGauge = GetComponent<BoostGauge>();
            _stateMachine = GetComponent<MechStateMachine>();
            // 입력 소스가 없는 기체(더미 AI)는 가드 입력이 항상 false
            _input = GetComponent<ICombatInputSource>();
            _identity = MechIdentity.GetOrAdd(gameObject);
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

            // 전력질주 중엔 가드 자체를 못 건다 (질주=공격적 상태, 방어자세 병행 불가)
            bool sprinting = _stateMachine != null && _stateMachine.IsSprinting;

            bool guardHeld = _input != null && _input.GuardHeld;
            if (guardHeld && !sprinting)
            {
                if (_boostGauge.IsOverheated)
                {
                    IsGuarding = false;
                    return;
                }
                IsGuarding = true;
                _boostGauge.TryConsume(guardBoostDrain * Time.deltaTime);

                // priority 10: Slide/SideStep(우선순위 5)보다 높음 —
                // 방어 자세는 플레이어가 의도적으로 유지 중인 상태라 대시 감속 램프보다 우선한다.
                _stateMachine.MechController.RequestDamping(8f, priority: 10);
            }
            else
            {
                if (IsGuarding)
                {
                    _stateMachine.MechController.SetBaselineDamping(2f);
                }
                IsGuarding = false;
            }
        }

        private void HandleParry()
        {
            if (IsGuarding && _input != null && _input.GuardPressedThisFrame)
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
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;

                MeleeSystem enemyMelee = target.GetComponent<MeleeSystem>();
                if (enemyMelee != null && enemyMelee.IsParrying)
                {
                    ApplyStun(attackerStunDuration);
                    return;
                }

                Health health = target.GetComponent<Health>();
                Rigidbody rb = target.GetComponent<Rigidbody>();

                float damage = finalDamage;
                if (enemyMelee != null && enemyMelee.IsGuarding)
                {
                    damage = enemyMelee.ApplyDamage(finalDamage);
                }

                if (health != null) health.TakeDamage(damage);
                if (rb != null)
                {
                    Vector3 knockDir = (target.transform.position - transform.position).normalized;
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
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;

                MeleeSystem enemyMelee = target.GetComponent<MeleeSystem>();
                Health health = target.GetComponent<Health>();

                if (enemyMelee != null && enemyMelee.MeleeTimer < enemyMelee.MeleeCooldown * 0.5f)
                {
                    Rigidbody myRb = GetComponent<Rigidbody>();
                    Rigidbody enemyRb = target.GetComponent<Rigidbody>();

                    Vector3 knockDir = (target.transform.position - transform.position).normalized;
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
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                return;
            }
        }
    }
}