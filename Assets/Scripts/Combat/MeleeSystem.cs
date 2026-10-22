using System;
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
        [Tooltip("펀치 한 방(발동 기준)마다 오르는 히트")]
        [SerializeField] private float heatPerPunch = 6f;

        [Header("가드 설정")]
        [Tooltip("가드로 받은 근접 계열(펀치·대시 어택·슬램·파일드라이버) 피해 감쇄율. 0.5 = 절반만 들어감")]
        [Range(0f, 1f)]
        [SerializeField] private float guardMeleeReduction = 0.5f;
        [Tooltip("가드로 받은 투사체 피해 감쇄율 (사격 기체용)")]
        [Range(0f, 1f)]
        [SerializeField] private float guardProjectileReduction = 0.7f;
        [Tooltip("가드로 공격을 받을 때마다 오르는 히트")]
        [SerializeField] private float guardHeatPerBlock = 10f;
        [Tooltip("과열 상태에서 가드로 받으면 가드가 깨지고 이 시간(초) 동안 스턴")]
        [SerializeField] private float guardBreakStun = 0.9f;
        [Tooltip("가드를 내린 뒤 다시 올릴 수 있을 때까지 대기(초). 패리 성공 직후엔 면제")]
        [SerializeField] private float guardReraiseDelay = 0.4f;
        [Tooltip("가드를 올린 순간부터 이 시간(초) 안에 맞으면 패리")]
        [SerializeField] private float parryWindow = 0.15f;
        [Tooltip("가드 중 리지드바디 감속. 방어 자세로 버티는 느낌")]
        [SerializeField] private float guardDamping = 8f;
        [Tooltip("가드를 내렸을 때 돌아갈 일반 이동 감속")]
        [SerializeField] private float guardReleaseDamping = 2f;

        [Header("맞딜")]
        [Tooltip("양쪽 펀치가 거의 동시에 나갔을 때 서로 밀려나는 힘")]
        [SerializeField] private float clashKnockback = 10f;

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

        /// <summary>내가 맞았을 때(피격자 쪽) 판정 결과가 정해지면 발생. 로그·연출은 여기 구독.</summary>
        public event Action<HitInfo, HitResult> OnHitResolved;

        private float _meleeTimer = 0f;
        private float _parryTimer = 0f;
        private float _tackleTimer = 999f;
        private float _stunTimer = 0f;
        private bool _parryActive = false;
        private float _reraiseTimer = 0f;
        // 패리에 성공한 뒤 가드를 내리면 재사용 대기를 건너뛴다 (후속타도 패리할 수 있게)
        private bool _parryGrace = false;

        private MechStateMachine _stateMachine;
        private HeatGauge _heatGauge;
        private Health _health;
        private Rigidbody _rb;
        private ICombatInputSource _input;
        private MechIdentity _identity;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _heatGauge = GetComponent<HeatGauge>();
            _health = GetComponent<Health>();
            _rb = GetComponent<Rigidbody>();
            // 입력 소스가 없는 기체(더미 AI)는 가드 입력이 항상 false
            _input = GetComponent<ICombatInputSource>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            // 0에서 시작하면 시작 직후 "방금 펀치를 낸 상태"로 취급돼 맞딜이 잘못 판정된다
            _meleeTimer = meleeCooldown;
        }

        private void Update()
        {
            _meleeTimer += Time.deltaTime;
            _tackleTimer += Time.deltaTime;
            if (_reraiseTimer > 0f) _reraiseTimer -= Time.deltaTime;

            if (IsStunned)
            {
                // 스턴 중엔 가드 불가. 스턴 자체가 벌이라 재사용 대기는 걸지 않는다
                if (IsGuarding) DropGuard(applyReraiseDelay: false);

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
            // 전력질주 중엔 가드 자체를 못 건다 (질주=공격적 상태, 방어자세 병행 불가)
            bool sprinting = _stateMachine != null && _stateMachine.IsSprinting;
            bool guardHeld = _input != null && _input.GuardHeld;

            if (guardHeld && !sprinting && (IsGuarding || _reraiseTimer <= 0f))
            {
                // 가드는 부스트를 쓰지 않는다. 탈진이어도 가드 가능.
                if (!IsGuarding)
                {
                    IsGuarding = true;
                    // 패리 창은 "버튼을 새로 누른 순간" 올라간 가드에만 연다.
                    // 재사용 대기 중 누르고 버티다 자동으로 올라간 가드까지 패리를 주면 대기가 의미 없어진다.
                    if (_input.GuardPressedThisFrame) StartParryWindow();
                }

                // priority 10: Slide/SideStep(우선순위 5)보다 높음 —
                // 방어 자세는 플레이어가 의도적으로 유지 중인 상태라 대시 감속 램프보다 우선한다.
                if (_stateMachine != null) _stateMachine.MechController.RequestDamping(guardDamping, priority: 10);
            }
            else if (IsGuarding)
            {
                DropGuard(applyReraiseDelay: true);
            }
        }

        private void DropGuard(bool applyReraiseDelay)
        {
            IsGuarding = false;
            EndParryWindow();
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(guardReleaseDamping);

            if (_parryGrace)
            {
                _parryGrace = false;
                _reraiseTimer = 0f;
            }
            else if (applyReraiseDelay)
            {
                _reraiseTimer = guardReraiseDelay;
            }
        }

        private void StartParryWindow()
        {
            _parryActive = true;
            _parryTimer = 0f;
            IsParrying = true;
        }

        private void EndParryWindow()
        {
            _parryActive = false;
            IsParrying = false;
        }

        private void HandleParry()
        {
            if (_parryActive)
            {
                _parryTimer += Time.deltaTime;
                if (_parryTimer >= parryWindow)
                {
                    EndParryWindow();
                }
            }
        }

        public void ApplyStun(float duration)
        {
            IsStunned = true;
            _stunTimer = -duration;
        }

        /// <summary>
        /// 가드 감쇄만 계산한다 (부작용 없음). 가드 중이 아니거나 관통 공격이면 그대로.
        /// 실제 피격 처리는 ReceiveHit을 쓴다.
        /// </summary>
        public float ApplyDamage(float incomingDamage, AttackType type)
        {
            if (!IsGuarding) return incomingDamage;
            if (type == AttackType.Lunge || type == AttackType.Tackle) return incomingDamage;

            float reduction = type == AttackType.Projectile ? guardProjectileReduction : guardMeleeReduction;
            return incomingDamage * (1f - reduction);
        }

        /// <summary>
        /// 모든 공격의 피격 판정 입구. 맞딜 → 패리 → 관통 → 가드(깨짐) → 피격 순으로 판단하고
        /// 피해·넉백·스턴·히트 증가까지 여기서 적용한다. 공격자는 결과만 보고 후속 처리를 한다.
        /// </summary>
        public HitResult ReceiveHit(HitInfo info)
        {
            HitResult result;

            // 1) 맞딜: 내가 방금 펀치를 냈는데(쿨다운 절반 이내) 상대 펀치가 들어오면 둘 다 0 피해 + 밀려남
            if (info.Type == AttackType.Punch && _meleeTimer < meleeCooldown * 0.5f)
            {
                ApplyClash(info);
                return Resolve(info, HitResult.Clash);
            }

            // 2) 패리: 피해 0, 히트 증가 없음
            if (IsParrying && !info.IgnoresParry)
            {
                _parryGrace = true;
                return Resolve(info, HitResult.Parried);
            }

            float damage;
            if (!IsGuarding || info.PiercesGuard)
            {
                // 3) 가드 안 함 / 관통: 감쇄 없음, 가드 히트 증가 없음
                result = HitResult.Hit;
                damage = info.Damage;
            }
            else if (_heatGauge != null && _heatGauge.IsOverheated)
            {
                // 4) 과열 상태로 막으면 가드가 깨진다: 감쇄 없이 전부 + 무방비 스턴
                result = HitResult.GuardBroken;
                damage = info.Damage;
                DropGuard(applyReraiseDelay: false);
                ApplyStun(Mathf.Max(guardBreakStun, info.StunDuration));
            }
            else
            {
                // 5) 정상 가드
                result = HitResult.Guarded;
                damage = ApplyDamage(info.Damage, info.Type);
                if (_heatGauge != null) _heatGauge.AddHeat(guardHeatPerBlock);
            }

            if (_health != null) _health.TakeDamage(damage);
            ApplyKnockback(_rb, info.KnockbackDirection, info.KnockbackForce);
            if (result == HitResult.Hit && info.StunDuration > 0f) ApplyStun(info.StunDuration);

            return Resolve(info, result);
        }

        private HitResult Resolve(HitInfo info, HitResult result)
        {
            OnHitResolved?.Invoke(info, result);
            return result;
        }

        private void ApplyClash(HitInfo info)
        {
            if (info.Attacker == null) return;
            Vector3 knockDir = (transform.position - info.Attacker.transform.position).normalized;
            ApplyKnockback(_rb, knockDir, clashKnockback);
            ApplyKnockback(info.Attacker.GetComponent<Rigidbody>(), -knockDir, clashKnockback);
        }

        private static void ApplyKnockback(Rigidbody rb, Vector3 direction, float force)
        {
            if (rb == null || force <= 0f || direction == Vector3.zero) return;
            rb.AddForce(direction.normalized * force, ForceMode.Impulse);
        }

        /// <summary>
        /// 대상에게 타격을 전달한다. MeleeSystem이 있으면 ReceiveHit으로 판정을 맡기고,
        /// 없으면(체력만 있는 오브젝트) 그대로 맞은 것으로 처리한다.
        /// </summary>
        public static HitResult DeliverHit(GameObject target, HitInfo info)
        {
            MeleeSystem defender = target.GetComponent<MeleeSystem>();
            if (defender != null) return defender.ReceiveHit(info);

            Health health = target.GetComponent<Health>();
            if (health != null) health.TakeDamage(info.Damage);
            ApplyKnockback(target.GetComponent<Rigidbody>(), info.KnockbackDirection, info.KnockbackForce);
            return HitResult.Hit;
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

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Tackle,
                    Damage = finalDamage,
                    Attacker = gameObject,
                    KnockbackDirection = target.transform.position - transform.position,
                    KnockbackForce = finalKnockback,
                    StunDuration = victimStunDuration
                };

                HitResult result = DeliverHit(target, info);
                if (result == HitResult.Parried)
                {
                    ApplyStun(attackerStunDuration);
                }
                return;
            }

            ApplyStun(attackerStunDuration);
        }

        public void TryMeleeAttack()
        {
            if (_meleeTimer < meleeCooldown) return;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return;
            _meleeTimer = 0f;
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(heatPerPunch);

            Collider[] hits = Physics.OverlapSphere(transform.position, meleeRange);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Punch,
                    Damage = meleeDamage,
                    Attacker = gameObject
                };
                DeliverHit(target, info);
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                return;
            }
        }
    }
}
