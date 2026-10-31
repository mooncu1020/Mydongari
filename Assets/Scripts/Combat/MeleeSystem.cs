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
        private BernardTuning.AttackStartupValues Startup => _tuning.Get(this).Startup;
        private BernardTuning.ComboValues Combo => _tuning.Get(this).Combo;

        public bool IsGuarding { get; private set; }
        /// <summary>파일 드라이버 넉다운 중(비행·미끄러짐·기상). 공격·가드·이동 입력 무시.</summary>
        public bool IsKnockedDown => _knockdown != null && _knockdown.IsKnockedDown;
        public bool IsParrying { get; private set; }
        public bool IsStunned { get; private set; }
        public float MeleeTimer => _meleeTimer;
        public float MeleeCooldown => Punch.cooldown;
        public bool CanTackle => _tackleTimer >= Tackle.cooldown;
        public float TackleRecoveryStun => Tackle.recoveryStun;

        /// <summary>내가 맞았을 때(피격자 쪽) 판정 결과가 정해지면 발생. 로그·연출은 여기 구독.</summary>
        public event Action<HitInfo, HitResult> OnHitResolved;
        /// <summary>내가 공격 버튼으로 공격을 시작했을 때(발생 시간 전, 맞든 안 맞든). 로그의 "발동" 카운트·예비 동작용.</summary>
        public event Action<AttackType> OnAttackStarted;
        /// <summary>발생 시간 도중 스턴·가드 깨짐 등으로 공격이 취소됐을 때.</summary>
        public event Action<AttackType> OnAttackCanceled;

        /// <summary>펀치 발생 시간 중(버튼은 눌렀고 아직 판정 전)인지.</summary>
        public bool IsPunchPending => _punchElapsed >= 0f;

        /// <summary>마지막으로 시작한 펀치의 콤보 단계 (1 원, 2 투, 3 쓰리). 콤보가 끊겼거나 꺼져 있으면 0. 외형·로그용.</summary>
        public int ComboStep => _comboStep;
        /// <summary>콤보 단계가 정해진 순간(펀치 시작). OnAttackStarted 바로 앞에 온다.</summary>
        public event Action<int> OnComboStep;

        /// <summary>
        /// 콤보에 따른 이동 속도 비율 (N2): 콤보 중(누른 순간 ~ 다음 타 입력 창 끝) moveSpeedScale, 쓰리 회복 동안 thirdRecoveryMoveSpeedScale.
        /// MechController가 이동 힘·최대 속도에 곱한다.
        /// </summary>
        public float MoveSpeedScale
        {
            get
            {
                BernardTuning.ComboValues combo = Combo;
                if (!combo.enabled || _comboStep == 0) return 1f;
                if (_punchElapsed >= 0f) return combo.moveSpeedScale;
                float sinceResolve = Time.time - _lastPunchResolveTime;
                if (_comboStep == 3) return sinceResolve < combo.thirdRecovery ? combo.thirdRecoveryMoveSpeedScale : 1f;
                return sinceResolve <= combo.window ? combo.moveSpeedScale : 1f;
            }
        }

        private float _meleeTimer = 0f;
        // 펀치 발생 시간 경과(초). 음수면 발생 중인 펀치 없음
        private float _punchElapsed = -1f;
        // 마지막으로 시작한 펀치의 콤보 단계 (0 = 콤보 아님)
        private int _comboStep;
        // 마지막으로 펀치 판정을 한 시각 (맞딜 창 판정용)
        private float _lastPunchResolveTime = -999f;
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
        private KnockdownController _knockdown;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _heatGauge = GetComponent<HeatGauge>();
            _health = GetComponent<Health>();
            _rb = GetComponent<Rigidbody>();
            // 입력 소스가 없는 기체(더미 AI)는 가드 입력이 항상 false
            _input = FindInputSource();
            _identity = MechIdentity.GetOrAdd(gameObject);
            // 파일 드라이버 넉다운 (R1). 씬에 안 붙어 있어도 동작하게
            _knockdown = KnockdownController.GetOrAdd(gameObject);
            // 0에서 시작하면 시작 직후 "방금 펀치를 낸 상태"로 취급돼 맞딜이 잘못 판정된다
            _meleeTimer = Punch.cooldown;
        }

        private void Update()
        {
            // 발생 시간이 지난 프레임에 판정. 스턴(가드 깨짐 포함)이면 ApplyStun에서 이미 취소됐다
            if (_punchElapsed >= 0f)
            {
                _punchElapsed += Time.deltaTime;
                if (_punchElapsed >= Startup.punch) ResolvePunch();
            }

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

            // 넉다운 중엔 가드 불가 (올리고 있던 가드도 내림)
            if (IsKnockedDown)
            {
                if (IsGuarding) DropGuard(applyReraiseDelay: false);
                return;
            }

            HandleGuard();
            HandleParry();

            if (InputTrace.ShouldLog(this, "3 ICombatInputSource"))
            {
                string move = _input is InputReader reader ? reader.MoveInput.ToString() : "-";
                InputTrace.Log("3 ICombatInputSource", $"{gameObject.name} source={InputSourceName} move={move} " +
                    $"guardHeld={(_input != null && _input.GuardHeld)}");
            }
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
            ICombatInputSource input = FindInputSource();
            if (input != null) _input = input;
        }

        /// <summary>
        /// 사람 입력(InputReader)이 있으면 그걸, 없으면 다른 입력 소스(BotInput)를 쓴다.
        /// GetComponent&lt;ICombatInputSource&gt;는 컴포넌트 순서대로 첫 번째를 집어서,
        /// 같은 기체에 BotInput이 붙어 있으면 사람 패드 대신 봇 버튼을 읽을 수 있다.
        /// </summary>
        private ICombatInputSource FindInputSource()
        {
            InputReader reader = GetComponent<InputReader>();
            if (reader != null) return reader;
            return GetComponent<ICombatInputSource>();
        }

        /// <summary>지금 읽는 전투 입력 소스 (진단 로그용). 없으면 "없음".</summary>
        public string InputSourceName =>
            _input is Component c && c != null ? $"{_input.GetType().Name}({c.gameObject.name})" : "없음";

        /// <summary>
        /// 가드 입력을 다음 Update까지 기다리지 않고 지금 한 번 처리한다. 규칙은 Update와 같다
        /// (스턴 중 불가, 재사용 대기, 질주 중 불가, 새로 누른 가드에만 패리 창).
        /// 같은 프레임에 들어오는 공격에 반응해야 하는 입력 소스(연습 봇의 패리 읽기)용.
        /// </summary>
        public void ApplyGuardInputNow()
        {
            if (IsStunned || IsKnockedDown) return;
            HandleGuard();
        }

        private void HandleGuard()
        {
            // 대시 중엔 가드 자체를 못 건다 (대시 = 공격적 상태, 방어자세 병행 불가).
            // V5: 공중(점프는 회피 대신 무방비)과 착지 경직 중에도 가드·패리 불가
            bool sprinting = _stateMachine != null &&
                (_stateMachine.IsDashing || _stateMachine.IsAirborne || _stateMachine.IsLanding);
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
            _punchElapsed = -1f;
            _lastPunchResolveTime = -999f;
            _comboStep = 0;
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(Guard.releaseDamping);
        }

        public void ApplyStun(float duration)
        {
            // 발생 시간 중에 스턴·가드 깨짐을 당하면 그 펀치는 나가지 않는다
            if (_punchElapsed >= 0f)
            {
                _punchElapsed = -1f;
                OnAttackCanceled?.Invoke(AttackType.Punch);
            }
            IsStunned = true;
            _stunTimer = -duration;
            // 맞아서 굳으면 콤보는 끊긴다 (다음 펀치는 원부터)
            _comboStep = 0;
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

            // 0) 넉다운 기상 무적: 판정 자체를 무시 (피해·넉백·스턴·히트 없음). 로그엔 남는다
            if (_knockdown != null && _knockdown.IsInvulnerable) return Resolve(info, HitResult.Invulnerable);

            // 1) 맞딜: 양쪽 펀치의 판정 시점이 clashWindow 이내면 둘 다 0 피해 + 밀려남.
            //    내 펀치가 방금 판정됐거나, 아직 발생 중인데 창 안에 판정될 예정이면 맞딜 (그 펀치는 맞딜로 소진)
            if (info.Type == AttackType.Punch && IsInClashWindow())
            {
                _punchElapsed = -1f;
                ApplyClash(info);
                return Resolve(info, HitResult.Clash);
            }

            // 1-2) 대시 어택·태클끼리 맞딜 (V4): 내 대시 공격도 clashWindow 안에 상대를 맞힐 상황이면 둘 다 0 피해 + 밀려남.
            //      내 공격을 먼저 끊고(속도 0) 밀어야 대시 속도에 넉백이 더해지지 않는다
            if ((info.Type == AttackType.DashAttack || info.Type == AttackType.Tackle) &&
                _stateMachine != null && _stateMachine.IsDashing && _stateMachine.ActiveDash.WouldHitWithin(Startup.clashWindow))
            {
                _stateMachine.ActiveDash.EndByClash();
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
            // V8 이동기면 외부 속도로 (MotionBridge), 아니면 리지드바디 충격
            MotionBridge.AddImpulse(rb, direction.normalized * force);
            // V3 이동은 속도를 직접 넣으므로, 잠깐 이동 로직을 멈춰야 넉백이 다음 물리 스텝에 지워지지 않는다
            MechController controller = rb.GetComponent<MechController>();
            if (controller != null) controller.HoldMoveControl();
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

        private bool IsInClashWindow()
        {
            BernardTuning.AttackStartupValues startup = Startup;
            if (Time.time - _lastPunchResolveTime <= startup.clashWindow) return true;
            return _punchElapsed >= 0f && startup.punch - _punchElapsed <= startup.clashWindow;
        }

        /// <summary>대시 어택·차징 태클의 발생 시간(초). 0이면 즉시 판정.</summary>
        public float TackleStartup(AttackType type)
        {
            return type == AttackType.Tackle ? Startup.chargedTackle : Startup.dashAttack;
        }

        /// <summary>
        /// 어깨 태클 시작(버튼을 누른 순간): 쿨다운을 쓰고 OnAttackStarted를 알린다. 쿨다운 중이면 false.
        /// 판정은 발생 시간 뒤 판정 구간 동안 닿으면 ResolveTackle (DashState가 진행).
        /// </summary>
        public bool BeginTackle(AttackType type)
        {
            if (_tackleTimer < Tackle.cooldown) return false;
            _tackleTimer = 0f;
            OnAttackStarted?.Invoke(type);
            return true;
        }

        /// <summary>발생 시간 중이던 태클이 스턴 등으로 취소됐음을 알린다 (외형·로그용).</summary>
        public void CancelTackle(AttackType type)
        {
            OnAttackCanceled?.Invoke(type);
        }

        /// <summary>발생 시간 없이 바로: BeginTackle + ResolveTackle.</summary>
        public HitResult? TryTackle(float chargePower, AttackType type)
        {
            if (!BeginTackle(type)) return null;
            return ResolveTackle(chargePower, type);
        }

        /// <summary>
        /// 어깨 태클 판정(판정하는 순간의 위치·방향 기준). type은 DashAttack(질주 1.5초 미만, 가드에 막힘) 또는 Tackle(차징, 관통 + 스턴).
        /// 범위 안에 상대가 없으면 null. 공격자 경직은 호출 쪽(DashState)이 결과를 보고 건다 — 패리당했으면 경직 없음.
        /// </summary>
        public HitResult? ResolveTackle(float chargePower, AttackType type)
        {
            return ResolveTackle(chargePower, type, MechFacing.AttackForward(transform));
        }

        /// <summary>
        /// 어깨 태클 판정, 부채꼴 기준 방향을 따로 받는다 (V3.1: 대시는 몸 정면과 진행 방향이 달라서 진행 방향으로 잰다).
        /// </summary>
        public HitResult? ResolveTackle(float chargePower, AttackType type, Vector3 forward)
        {
            BernardTuning.TackleValues tackle = Tackle;
            bool charged = type == AttackType.Tackle;
            // 차징 태클은 기존 공식(TackleChargePower 반영), 대시 어택은 고정 수치
            BernardTuning.DashAttackValues dash = DashAttack;
            float finalDamage = charged ? tackle.damage * (1f + chargePower) : dash.damage;
            float finalKnockback = charged ? tackle.knockback * (1f + chargePower) : dash.knockback;
            float finalStun = charged ? tackle.victimStun : dash.stun;

            // 정면 부채꼴 밖이면 헛침 (등 뒤·옆으로 스친 상대는 안 맞는다)
            if (!IsOpponentInTackleArc(forward)) return null;

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
        /// 너클 펀치 시작. 쿨다운·과열·스턴·발생 중이면 안 나가고 false (입력 버퍼가 다시 시도한다).
        /// 시작했으면 true: 발생 시간이 있으면 예비 동작만 시작하고 시간이 지난 프레임에, 0이면 지금 판정한다.
        /// </summary>
        public bool TryMeleeAttack()
        {
            if (_punchElapsed >= 0f) return false;
            // 스턴 중엔 펀치를 시작할 수 없다 (런지·가드와 같은 규칙. 발생 중 스턴이면 취소되는 것과 짝). 넉다운도
            if (IsStunned || IsKnockedDown) return false;
            BernardTuning.PunchValues punch = Punch;
            BernardTuning.ComboValues combo = Combo;
            int step = NextComboStep(combo);
            if (combo.enabled && _comboStep == 3 && Time.time - _lastPunchResolveTime < combo.thirdRecovery) return false;
            // 콤보 다음 타는 짧은 간격, 원(새 콤보·단발)은 예전 쿨다운
            float needed = step >= 2 ? combo.interval : punch.cooldown;
            if (_meleeTimer < needed) return false;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return false;
            _meleeTimer = 0f;
            _comboStep = step;
            if (step > 0) OnComboStep?.Invoke(step);
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(punch.heatPerPunch);
            OnAttackStarted?.Invoke(AttackType.Punch);

            if (Startup.punch <= 0f) ResolvePunch();
            else _punchElapsed = 0f;
            return true;
        }

        /// <summary>
        /// 지금 펀치를 시작하면 몇 번째 타인지 (N2). 원·투를 친 뒤 판정 순간부터 window 안이면 다음 타, 아니면 원.
        /// 쓰리 다음은 원. 콤보가 꺼져 있으면 0(단발).
        /// </summary>
        private int NextComboStep(BernardTuning.ComboValues combo)
        {
            if (!combo.enabled) return 0;
            if ((_comboStep == 1 || _comboStep == 2) && _punchElapsed < 0f && Time.time - _lastPunchResolveTime <= combo.window)
            {
                return _comboStep + 1;
            }
            return 1;
        }

        /// <summary>펀치 판정: 판정하는 순간의 내 정면·상대 위치 기준.</summary>
        private HitResult? ResolvePunch()
        {
            _punchElapsed = -1f;
            _lastPunchResolveTime = Time.time;
            BernardTuning.PunchValues punch = Punch;
            BernardTuning.ComboValues combo = Combo;
            bool third = _comboStep == 3;

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !IsInFrontArc(opponent.transform, punch.halfAngle)) return null;
            // V5: 위아래 차이가 크면(점프한 상대·고지대) 빗나감
            if (Mathf.Abs(opponent.transform.position.y - transform.position.y) > punch.heightTolerance) return null;

            // 거리는 콜라이더 표면 기준(OverlapSphere)이라 덩치 큰 기체도 사거리가 자연스럽다
            Collider[] hits = Physics.OverlapSphere(transform.position, punch.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                // 쓰리는 피해 배율 + 넉백 (원·투는 예전 펀치 그대로)
                HitInfo info = new HitInfo
                {
                    Type = AttackType.Punch,
                    Damage = third ? punch.damage * combo.thirdDamageMultiplier : punch.damage,
                    Attacker = gameObject,
                    KnockbackDirection = third ? opponent.transform.position - transform.position : Vector3.zero,
                    KnockbackForce = third ? combo.thirdKnockback : 0f,
                    ComboStep = _comboStep
                };
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                return DeliverHit(opponent.gameObject, info);
            }
            return null;
        }

        /// <summary>상대가 태클 부채꼴(정면 ±Tackle.halfAngle) 안인지. 거리는 따로 본다. 자동 접촉 태클 판정에도 쓴다.</summary>
        public bool IsOpponentInTackleArc()
        {
            return IsOpponentInTackleArc(MechFacing.AttackForward(transform));
        }

        /// <summary>상대가 forward 기준 ±Tackle.halfAngle 안인지 (대시 진행 방향 기준 판정용).</summary>
        public bool IsOpponentInTackleArc(Vector3 forward)
        {
            MechIdentity opponent = _identity.Opponent;
            return opponent != null && IsInFrontArc(opponent.transform, Tackle.halfAngle, forward);
        }

        private bool IsInFrontArc(Transform target, float halfAngle)
        {
            // 공격 방향 = 상체 방향 (G2. 상하체 분리가 없는 기체는 기체 정면)
            return IsInFrontArc(target, halfAngle, MechFacing.AttackForward(transform));
        }

        private bool IsInFrontArc(Transform target, float halfAngle, Vector3 forward)
        {
            // 위아래 차이는 무시하고 수평 방향만 본다 (지면 밀착 격투)
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return true;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = MechFacing.AttackForward(transform);
            return Vector3.Angle(forward, toTarget) <= halfAngle;
        }
    }
}
