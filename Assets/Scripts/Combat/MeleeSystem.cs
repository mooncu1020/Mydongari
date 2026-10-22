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
        [Tooltip("펀치 사거리(m)")]
        [SerializeField] private float meleeRange = 3f;
        [Tooltip("펀치 판정 부채꼴 반각(°). 기체 정면 기준 ±이 각도 안의 상대만 맞는다")]
        [SerializeField] private float punchHalfAngle = 45f;
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

        [Header("패리 반사")]
        [Tooltip("런지·태클을 패리하면 공격자가 그 공격 피해의 이 비율만큼 받는다")]
        [SerializeField] private float parryReflectRatio = 1f;
        [Tooltip("반사 넉백 힘. 공격 자체에 넉백 값이 있으면 그 값을 쓰고, 없을 때만 이 값")]
        [SerializeField] private float parryReflectKnockback = 8f;
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
        [Tooltip("태클 판정 부채꼴 반각(°). 기체 정면 기준 ±이 각도 안의 상대만 맞는다 (대시 어택·차징 태클·자동 접촉 공통)")]
        [SerializeField] private float tackleHalfAngle = 45f;
        [SerializeField] private float tackleCooldown = 1f;
        [SerializeField] private float victimStunDuration = 1.7f;
        [Tooltip("태클을 부딪친 뒤 공격자 자신의 경직(초). 패리당했을 땐 적용 안 함")]
        [SerializeField] private float tackleRecoveryStun = 0.5f;

        [Header("대시 어택 (전력질주 1.5초 미만 어깨 태클)")]
        [Tooltip("가드에 막힌다(가드 규칙 적용). 차징 태클과 달리 관통·스턴 없음")]
        [SerializeField] private float dashAttackDamage = 20f;
        [SerializeField] private float dashAttackKnockback = 6f;
        [Tooltip("그대로 맞았을 때 상대 스턴(초). 0이면 없음")]
        [SerializeField] private float dashAttackStun = 0f;

        public bool IsGuarding { get; private set; }
        public bool IsParrying { get; private set; }
        public bool IsStunned { get; private set; }
        public float MeleeTimer => _meleeTimer;
        public float MeleeCooldown => meleeCooldown;
        public bool CanTackle => _tackleTimer >= tackleCooldown;
        public float TackleRecoveryStun => tackleRecoveryStun;

        /// <summary>내가 맞았을 때(피격자 쪽) 판정 결과가 정해지면 발생. 로그·연출은 여기 구독.</summary>
        public event Action<HitInfo, HitResult> OnHitResolved;
        /// <summary>내가 공격을 발동했을 때(맞든 안 맞든). 로그의 "발동" 카운트용.</summary>
        public event Action<AttackType> OnAttackStarted;

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

        /// <summary>라운드 시작: 스턴·가드·패리·각종 타이머 초기화. 펀치·태클은 바로 쓸 수 있는 상태로.</summary>
        public void ResetForRound()
        {
            IsStunned = false;
            _stunTimer = 0f;
            IsGuarding = false;
            EndParryWindow();
            _parryGrace = false;
            _reraiseTimer = 0f;
            _meleeTimer = meleeCooldown;
            _tackleTimer = tackleCooldown;
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(guardReleaseDamping);
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
            if (HitInfo.PiercesGuardType(type)) return incomingDamage;

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

            // 2) 패리: 피해 0, 히트 증가 없음. 런지·태클은 공격자에게 되돌려 준다 (공격자 스턴은 없음)
            if (IsParrying && !info.IgnoresParry)
            {
                _parryGrace = true;
                if (info.Type == AttackType.Tackle || info.Type == AttackType.DashAttack || info.Type == AttackType.Lunge)
                {
                    ReflectToAttacker(info);
                }
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

        private void ReflectToAttacker(HitInfo info)
        {
            if (info.Attacker == null) return;

            // 공격자는 돌진 중이라 가드가 없다 → 감쇄 계산 없이 체력에 바로
            Health attackerHealth = info.Attacker.GetComponent<Health>();
            if (attackerHealth != null) attackerHealth.TakeDamage(info.Damage * parryReflectRatio);

            Vector3 pushBack = info.Attacker.transform.position - transform.position;
            pushBack.y = 0f;
            float force = info.KnockbackForce > 0f ? info.KnockbackForce : parryReflectKnockback;
            ApplyKnockback(info.Attacker.GetComponent<Rigidbody>(), pushBack, force);
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

        /// <summary>
        /// 어깨 태클 판정. type은 DashAttack(질주 1.5초 미만, 가드에 막힘) 또는 Tackle(차징, 관통 + 스턴).
        /// 쿨다운 중이거나 범위 안에 상대가 없으면 null.
        /// 공격자 경직은 호출 쪽(SprintState)이 결과를 보고 건다 — 패리당했으면 경직 없음.
        /// </summary>
        public HitResult? TryTackle(float chargePower, AttackType type)
        {
            if (_tackleTimer < tackleCooldown) return null;
            _tackleTimer = 0f;
            OnAttackStarted?.Invoke(type);

            bool charged = type == AttackType.Tackle;
            // 차징 태클은 기존 공식(TackleChargePower 반영), 대시 어택은 고정 수치
            float finalDamage = charged ? tackleDamage * (1f + chargePower) : dashAttackDamage;
            float finalKnockback = charged ? tackleKnockback * (1f + chargePower) : dashAttackKnockback;
            float finalStun = charged ? victimStunDuration : dashAttackStun;

            // 정면 부채꼴 밖이면 헛침 (등 뒤·옆으로 스친 상대는 안 맞는다)
            if (!IsOpponentInTackleArc()) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, tackleRange);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;

                HitInfo info = new HitInfo
                {
                    Type = charged ? AttackType.Tackle : AttackType.DashAttack,
                    Damage = finalDamage,
                    Attacker = gameObject,
                    KnockbackDirection = target.transform.position - transform.position,
                    KnockbackForce = finalKnockback,
                    StunDuration = finalStun
                };

                return DeliverHit(target, info);
            }

            return null;
        }

        /// <summary>
        /// 너클 펀치. 쿨다운·과열로 안 나가면 null, 휘둘렀는데 빗나가면 null,
        /// 맞았으면 피격자가 판단한 결과(Hit/Guarded/GuardBroken/Parried/Clash).
        /// </summary>
        public HitResult? TryMeleeAttack()
        {
            if (_meleeTimer < meleeCooldown) return null;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return null;
            _meleeTimer = 0f;
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(heatPerPunch);
            OnAttackStarted?.Invoke(AttackType.Punch);

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !IsInFrontArc(opponent.transform, punchHalfAngle)) return null;

            // 거리는 콜라이더 표면 기준(OverlapSphere)이라 덩치 큰 기체도 사거리가 자연스럽다
            Collider[] hits = Physics.OverlapSphere(transform.position, meleeRange);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Punch,
                    Damage = meleeDamage,
                    Attacker = gameObject
                };
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                return DeliverHit(opponent.gameObject, info);
            }
            return null;
        }

        /// <summary>상대가 태클 부채꼴(정면 ±tackleHalfAngle) 안인지. 거리는 따로 본다. 자동 접촉 태클 판정에도 쓴다.</summary>
        public bool IsOpponentInTackleArc()
        {
            MechIdentity opponent = _identity.Opponent;
            return opponent != null && IsInFrontArc(opponent.transform, tackleHalfAngle);
        }

        private bool IsInFrontArc(Transform target, float halfAngle)
        {
            // 위아래 차이는 무시하고 수평 방향만 본다 (지면 밀착 격투)
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return true;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, toTarget) <= halfAngle;
        }
    }
}
