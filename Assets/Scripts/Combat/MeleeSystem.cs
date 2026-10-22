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
        // 값은 캐시하지 않고 매번 에셋에서 읽는다 (플레이 중 조정 반영)
        private TuningRef _tuning;
        private BernardTuning.PunchValues Punch => _tuning.Get(this).Punch;
        private BernardTuning.GuardValues Guard => _tuning.Get(this).Guard;
        private BernardTuning.ParryValues Parry => _tuning.Get(this).Parry;
        private BernardTuning.TackleValues Tackle => _tuning.Get(this).Tackle;
        private BernardTuning.DashAttackValues DashAttack => _tuning.Get(this).DashAttack;

        public bool IsGuarding { get; private set; }
        public bool IsParrying { get; private set; }
        public bool IsStunned { get; private set; }
        public float MeleeTimer => _meleeTimer;
        public float MeleeCooldown => Punch.cooldown;
        public bool CanTackle => _tackleTimer >= Tackle.cooldown;
        public float TackleRecoveryStun => Tackle.recoveryStun;

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
            _meleeTimer = Punch.cooldown;
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

        /// <summary>전투 입력 소스를 바꾼다. 연습 봇처럼 런타임에 입력 컴포넌트를 붙이는 기체용.</summary>
        public void SetInputSource(ICombatInputSource input)
        {
            _input = input;
        }

        /// <summary>
        /// 같은 오브젝트의 부품(히트 게이지·체력·입력 소스)을 다시 찾는다.
        /// 연습 봇이 런타임에 HeatGauge 등을 붙였을 때, 이 컴포넌트가 먼저 Awake했어도 같은 규칙(과열 가드 깨짐 등)이 적용되게.
        /// </summary>
        public void RebindComponents()
        {
            _heatGauge = GetComponent<HeatGauge>();
            _health = GetComponent<Health>();
            ICombatInputSource input = GetComponent<ICombatInputSource>();
            if (input != null) _input = input;
        }

        /// <summary>
        /// 가드 입력을 다음 Update까지 기다리지 않고 지금 한 번 처리한다. 규칙은 Update와 같다
        /// (스턴 중 불가, 재사용 대기, 질주 중 불가, 새로 누른 가드에만 패리 창).
        /// 같은 프레임에 들어오는 공격에 반응해야 하는 입력 소스(연습 봇의 패리 읽기)용.
        /// </summary>
        public void ApplyGuardInputNow()
        {
            if (IsStunned) return;
            HandleGuard();
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
                if (_stateMachine != null) _stateMachine.MechController.RequestDamping(Guard.damping, priority: 10);
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
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(Guard.releaseDamping);

            if (_parryGrace)
            {
                _parryGrace = false;
                _reraiseTimer = 0f;
            }
            else if (applyReraiseDelay)
            {
                _reraiseTimer = Guard.reraiseDelay;
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
                if (_parryTimer >= Parry.window)
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
            _meleeTimer = Punch.cooldown;
            _tackleTimer = Tackle.cooldown;
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(Guard.releaseDamping);
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

            float reduction = type == AttackType.Projectile ? Guard.projectileReduction : Guard.meleeReduction;
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
            if (info.Type == AttackType.Punch && _meleeTimer < Punch.cooldown * 0.5f)
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
                ApplyStun(Mathf.Max(Guard.breakStun, info.StunDuration));
            }
            else
            {
                // 5) 정상 가드
                result = HitResult.Guarded;
                damage = ApplyDamage(info.Damage, info.Type);
                if (_heatGauge != null) _heatGauge.AddHeat(Guard.heatPerBlock);
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
            if (attackerHealth != null) attackerHealth.TakeDamage(info.Damage * Parry.reflectRatio);

            Vector3 pushBack = info.Attacker.transform.position - transform.position;
            pushBack.y = 0f;
            float force = info.KnockbackForce > 0f ? info.KnockbackForce : Parry.reflectKnockback;
            ApplyKnockback(info.Attacker.GetComponent<Rigidbody>(), pushBack, force);
        }

        private void ApplyClash(HitInfo info)
        {
            if (info.Attacker == null) return;
            Vector3 knockDir = (transform.position - info.Attacker.transform.position).normalized;
            float force = Punch.clashKnockback;
            ApplyKnockback(_rb, knockDir, force);
            ApplyKnockback(info.Attacker.GetComponent<Rigidbody>(), -knockDir, force);
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
            BernardTuning.TackleValues tackle = Tackle;
            if (_tackleTimer < tackle.cooldown) return null;
            _tackleTimer = 0f;
            OnAttackStarted?.Invoke(type);

            bool charged = type == AttackType.Tackle;
            // 차징 태클은 기존 공식(TackleChargePower 반영), 대시 어택은 고정 수치
            BernardTuning.DashAttackValues dash = DashAttack;
            float finalDamage = charged ? tackle.damage * (1f + chargePower) : dash.damage;
            float finalKnockback = charged ? tackle.knockback * (1f + chargePower) : dash.knockback;
            float finalStun = charged ? tackle.victimStun : dash.stun;

            // 정면 부채꼴 밖이면 헛침 (등 뒤·옆으로 스친 상대는 안 맞는다)
            if (!IsOpponentInTackleArc()) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, tackle.range);
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
            BernardTuning.PunchValues punch = Punch;
            if (_meleeTimer < punch.cooldown) return null;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return null;
            _meleeTimer = 0f;
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(punch.heatPerPunch);
            OnAttackStarted?.Invoke(AttackType.Punch);

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !IsInFrontArc(opponent.transform, punch.halfAngle)) return null;

            // 거리는 콜라이더 표면 기준(OverlapSphere)이라 덩치 큰 기체도 사거리가 자연스럽다
            Collider[] hits = Physics.OverlapSphere(transform.position, punch.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                HitInfo info = new HitInfo
                {
                    Type = AttackType.Punch,
                    Damage = punch.damage,
                    Attacker = gameObject
                };
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                return DeliverHit(opponent.gameObject, info);
            }
            return null;
        }

        /// <summary>상대가 태클 부채꼴(정면 ±Tackle.halfAngle) 안인지. 거리는 따로 본다. 자동 접촉 태클 판정에도 쓴다.</summary>
        public bool IsOpponentInTackleArc()
        {
            MechIdentity opponent = _identity.Opponent;
            return opponent != null && IsInFrontArc(opponent.transform, Tackle.halfAngle);
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
