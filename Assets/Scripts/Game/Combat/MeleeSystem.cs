using System;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Bot;
using MyDongari.Game.Combat.Skills;
using MyDongari.Game.Heat;
using MyDongari.Game.Input;
using MyDongari.Game.Knockdown;
using MyDongari.Game.Tuning;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 기체 한 대의 전투 허브. 전투(공격 진행·판정·피격 정산·반응·가드·패리)는 전부 60Hz 전투 틱에서 돌고, 시간은 틱 수로 센다
    /// (Time.time·deltaTime을 쓰지 않는다 — 30fps에서도 같은 결과).
    /// 한 틱 (전투 사양서 4.1·4.2, VoTickDriver가 순서를 정한다):
    ///   ① DecideHits — 틱 맨 앞, 아무도 움직이기 전에 지난 틱 기록을 판단(막음·패리·맞딜). 두 기체가 같은 시점 상태로 본다.
    ///   ② CombatTick — 내 갱신의 "피격 정산" 자리(이동기의 자동 선회 뒤, 상태 본 처리 전): 판단을 적용 → 가드·패리 → 스킬·공격 입력 → 공격 진행.
    ///   ③ EndTick — 이동 적분 뒤.
    ///   ④ CollectHits — 모든 기체가 움직인 뒤 내 공격 판정을 하고 상대 기록에 쌓는다(반응은 다음 틱).
    /// 이동기가 켜진 기체는 ②③을 이동기가(VoHostAdapter를 거쳐) 부르고, 이동기가 없는 연습 봇은 TickStandalone이 같은 순서로 부른다.
    /// </summary>
    public class MeleeSystem : MonoBehaviour, IVoTickUnit
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
        /// <summary>넉다운 중(비행·미끄러짐·기상). 공격·가드·이동 입력 무시.</summary>
        public bool IsKnockedDown => _knockdown != null && _knockdown.IsKnockedDown;
        public bool IsParrying { get; private set; }
        public bool IsStunned => _stunFrames > 0;
        public bool CanTackle => _tackleTimer >= Tackle.cooldownFrames;
        public int TackleRecoveryFrames => Tackle.recoveryFrames;
        /// <summary>히트스톱 중 (모션·수평 이동 정지).</summary>
        public bool IsHitStopped => false;

        /// <summary>내가 맞았을 때(피격자 쪽) 판정 결과가 적용되면 발생. 로그·연출은 여기 구독.</summary>
        public event Action<HitInfo, HitResult> OnHitResolved;
        /// <summary>내가 때린 공격의 결과가 피격자 쪽에서 적용됐을 때 (공격자 쪽 후속 처리·로그용).</summary>
        public event Action<HitInfo, HitResult> OnAttackResolved;
        /// <summary>내가 공격 버튼으로 공격을 시작했을 때(발생 시간 전, 맞든 안 맞든). 로그의 "발동" 카운트·예비 동작용.</summary>
        public event Action<AttackType> OnAttackStarted;
        /// <summary>발생 시간 도중 스턴·가드 깨짐 등으로 공격이 취소됐을 때.</summary>
        public event Action<AttackType> OnAttackCanceled;

        /// <summary>펀치 발생 시간 중(버튼은 눌렀고 아직 판정 전)인지.</summary>
        public bool IsPunchPending => _punchElapsed >= 0;

        /// <summary>마지막으로 시작한 펀치의 콤보 단계 (1 원, 2 투, 3 쓰리). 콤보가 끊겼거나 꺼져 있으면 0. 외형·로그용.</summary>
        public int ComboStep => _comboStep;
        /// <summary>콤보 단계가 정해진 순간(펀치 시작). OnAttackStarted 바로 앞에 온다.</summary>
        public event Action<int> OnComboStep;

        /// <summary>이 기체의 피격 기록.</summary>
        public CombatLedger Ledger { get; } = new CombatLedger();

        /// <summary>
        /// 콤보에 따른 이동 속도 비율 (N2): 콤보 중(누른 순간 ~ 다음 타 입력 창 끝) moveSpeedScale, 쓰리 회복 동안 thirdRecoveryMoveSpeedScale.
        /// </summary>
        public float MoveSpeedScale
        {
            get
            {
                BernardTuning.ComboValues combo = Combo;
                if (!combo.enabled || _comboStep == 0) return 1f;
                if (_punchElapsed >= 0) return combo.moveSpeedScale;
                if (_comboStep == 3) return _sincePunchResolve < CombatTime.Frames(combo.thirdRecovery) ? combo.thirdRecoveryMoveSpeedScale : 1f;
                return _sincePunchResolve <= CombatTime.Frames(combo.window) ? combo.moveSpeedScale : 1f;
            }
        }

        // ---- 틱 카운터 (전부 틱 수) ----
        private const int Long = 100000;
        private int _meleeTimer = Long;
        // 펀치 발생 시간 경과. 음수면 발생 중인 펀치 없음
        private int _punchElapsed = -1;
        // 이번 틱 끝(CollectHits)에 펀치 판정을 할지
        private bool _punchStrike;
        // 마지막으로 시작한 펀치의 콤보 단계 (0 = 콤보 아님)
        private int _comboStep;
        // 마지막 펀치 판정 뒤 지난 틱
        private int _sincePunchResolve = Long;
        private int _parryFrames;
        private int _tackleTimer = Long;
        private int _stunFrames;
        private int _reraiseFrames;
        // 패리에 성공한 뒤 가드를 내리면 재사용 대기를 건너뛴다 (후속타도 패리할 수 있게)
        private bool _parryGrace;
        private int _lastGuardPressCount;

        private MechStateMachine _stateMachine;
        private HeatGauge _heatGauge;
        private Health _health;
        private Rigidbody _rb;
        private ICombatInputSource _input;
        private MechIdentity _identity;
        private KnockdownController _knockdown;
        private WeaponSystem _weapon;
        private AnchorClaw _claw;
        private ImpactSlam _slam;
        private ThrusterLunge _lunge;
        private MeleeDummy _dummy;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _rb = GetComponent<Rigidbody>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            // 넉다운 (R1). 씬에 안 붙어 있어도 동작하게
            _knockdown = KnockdownController.GetOrAdd(gameObject);
            RebindComponents();
        }

        private void OnEnable()
        {
            VoTickDriver.Register(this, StyleOverrides.Current.Effective.tickRate);
        }

        private void OnDisable()
        {
            VoTickDriver.Unregister(this);
        }

        // ---------------- IVoTickUnit ----------------

        public int PlayerIndex => _identity != null ? _identity.PlayerIndex : 0;
        public bool TickEnabled => isActiveAndEnabled;
        public VoMotor Motor => VoMotor.Of(_rb);

        public void DecideHits()
        {
            if (Ledger.IncomingCount == 0) return;
            Ledger.Decide(new DefenderPosture
            {
                Invulnerable = _knockdown != null && _knockdown.IsInvulnerable,
                Parrying = IsParrying,
                Guarding = IsGuarding,
                Overheated = _heatGauge != null && _heatGauge.IsOverheated,
                GuardedDamageScale = 1f - Guard.meleeReduction
            });
        }

        public void TickStandalone()
        {
            CombatTick();
            EndTick();
        }

        public void CollectHits()
        {
            if (_punchStrike)
            {
                _punchStrike = false;
                ResolvePunch();
            }
            if (_lunge != null) _lunge.CollectHits();
            if (_slam != null) _slam.CollectHits();
            if (_claw != null) _claw.CollectHits();
            if (_stateMachine != null && _stateMachine.DashAttack != null) _stateMachine.DashAttack.CollectHits();
        }

        // ---------------- 한 틱 ----------------

        /// <summary>
        /// 내 갱신의 "피격 정산" 자리 (전투 사양서 4.2-②-4). 이동기 틱 안(자동 선회 뒤, 상태 본 처리 전) 또는 TickStandalone에서 한 번.
        /// </summary>
        public void CombatTick()
        {
            // 판단이 끝났으니 지난 틱 기록은 비운다 (모든 기체의 DecideHits는 어느 기체의 틱보다 먼저 끝난다)
            Ledger.EndDecide();

            if (_meleeTimer < Long) _meleeTimer++;
            if (_tackleTimer < Long) _tackleTimer++;
            if (_sincePunchResolve < Long) _sincePunchResolve++;
            if (_reraiseFrames > 0) _reraiseFrames--;
            if (_stunFrames > 0) _stunFrames--;

            ApplyDecisions();

            if (_knockdown != null) _knockdown.CombatTick();
            if (_stateMachine != null) _stateMachine.SyncKnockdownState();

            bool roundActive = Flow.GameManager.Instance == null || Flow.GameManager.Instance.IsRoundActive;
            if (IsStunned || IsKnockedDown)
            {
                // 스턴·넉다운 중엔 가드 불가 (올리고 있던 가드도 내림). 스턴 자체가 벌이라 재사용 대기는 걸지 않는다
                if (IsGuarding) DropGuard(applyReraiseDelay: false);
                ReadGuardPress();
            }
            else
            {
                HandleGuard();
                HandleParry();
            }

            // 스킬·공격: 쿨다운은 계속 돌고, 입력은 각 컴포넌트가 상황을 보고 받는다
            if (_claw != null) _claw.CombatTick();
            if (_slam != null) _slam.CombatTick();
            if (_lunge != null) _lunge.CombatTick();
            if (_weapon != null) _weapon.CombatTick(roundActive);
            if (_dummy != null) _dummy.CombatTick();

            // 펀치 발생 시간: 다 지난 틱의 끝(모든 기체가 움직인 뒤)에 판정한다
            if (_punchElapsed >= 0)
            {
                _punchElapsed++;
                if (_punchElapsed >= CombatTime.Frames(Startup.punch)) _punchStrike = true;
            }

            if (InputTrace.ShouldLog(this, "3 ICombatInputSource"))
            {
                string move = _input is InputReader reader ? reader.MoveInput.ToString() : "-";
                InputTrace.Log("3 ICombatInputSource", $"{gameObject.name} source={InputSourceName} move={move} " +
                    $"guardHeld={(_input != null && _input.GuardHeld)}");
            }
        }

        /// <summary>틱 끝 (이동 적분 뒤).</summary>
        public void EndTick()
        {
        }

        // ---------------- 피격 적용 ----------------

        private void ApplyDecisions()
        {
            var decisions = Ledger.Decisions;
            for (int i = 0; i < decisions.Count; i++) ApplyDecision(decisions[i]);
            var reflects = Ledger.Reflects;
            for (int i = 0; i < reflects.Count; i++) ApplyReflect(reflects[i]);
            Ledger.ClearApplied();
        }

        private void ApplyDecision(HitDecision decision)
        {
            HitInfo info = decision.Info;
            switch (decision.Result)
            {
                case HitResult.Clash:
                    // 맞딜: 둘 다 피해 0 + 밀려남 (각자 자기 틱에 자기 몫만). 진행 중인 내 공격은 맞딜로 소진
                    if (_punchElapsed >= 0) _punchElapsed = -1;
                    _punchStrike = false;
                    if (_stateMachine != null && _stateMachine.IsDashing && _stateMachine.ActiveDash.IsAttacking) _stateMachine.ActiveDash.EndByClash();
                    ApplyKnockback(info.Direction, Punch.clashKnockback);
                    break;
                case HitResult.Parried:
                    _parryGrace = true;
                    break;
                case HitResult.Hit:
                    if (_health != null) _health.TakeDamage(decision.Damage);
                    ApplyKnockback(info.Direction, info.KnockbackForce);
                    if (info.StunFrames > 0) ApplyStun(info.StunFrames);
                    if (info.Type == AttackType.PileDriver && _knockdown != null) _knockdown.Begin(info.Direction, CombatTime.Seconds(info.LaunchDelayFrames));
                    break;
                case HitResult.GuardBroken:
                    if (_health != null) _health.TakeDamage(decision.Damage);
                    ApplyKnockback(info.Direction, info.KnockbackForce);
                    DropGuard(applyReraiseDelay: false);
                    ApplyStun(Mathf.Max(CombatTime.Frames(Guard.breakStun), info.StunFrames));
                    break;
                case HitResult.Guarded:
                    if (_health != null) _health.TakeDamage(decision.Damage);
                    ApplyKnockback(info.Direction, info.KnockbackForce);
                    if (_heatGauge != null) _heatGauge.AddHeat(Guard.heatPerBlock);
                    break;
            }
            OnHitResolved?.Invoke(info, decision.Result);
            MeleeSystem attacker = info.Attacker != null ? info.Attacker.GetComponent<MeleeSystem>() : null;
            if (attacker != null) attacker.NotifyAttackResolved(info, decision.Result);
        }

        /// <summary>내가 때린 공격을 상대가 패리해 되돌아온 것: 돌진 중이라 가드가 없다 → 감쇄 없이 체력에 바로 + 밀려남.</summary>
        private void ApplyReflect(ReflectRecord reflect)
        {
            if (_health != null) _health.TakeDamage(reflect.Damage * Parry.reflectRatio);
            float force = reflect.Info.KnockbackForce > 0f ? reflect.Info.KnockbackForce : Parry.reflectKnockback;
            ApplyKnockback(reflect.Direction, force);
        }

        /// <summary>피격자가 내 공격의 결과를 알려 준다 (피격자의 정산 단계에서).</summary>
        private void NotifyAttackResolved(HitInfo info, HitResult result)
        {
            // 패리·맞딜당한 대시 공격은 공격자 경직이 없다 (되돌아온 넉백이 벌이다)
            if ((result == HitResult.Parried || result == HitResult.Clash) && info.ClashFamily == 2) _stunFrames = 0;
            OnAttackResolved?.Invoke(info, result);
        }

        private void ApplyKnockback(Vector3 direction, float force)
        {
            direction.y = 0f;
            if (_rb == null || force <= 0f || direction.sqrMagnitude < 0.0001f) return;
            // 이동기면 외부 속도로 (MotionBridge), 아니면 리지드바디 충격
            MotionBridge.AddImpulse(_rb, direction.normalized * force);
            // 이동 상태는 속도를 직접 넣으므로, 잠깐 이동 로직을 멈춰야 넉백이 다음 틱에 지워지지 않는다
            MechController controller = _stateMachine != null ? _stateMachine.MechController : null;
            if (controller != null) controller.HoldMoveControl();
        }

        /// <summary>
        /// 대상의 피격 기록에 타격을 쌓는다 (판단·반응은 다음 틱). 전투 허브가 없는 대상(체력만 있는 오브젝트)은 그대로 맞은 것으로 처리.
        /// </summary>
        public static void QueueHit(GameObject target, HitInfo info)
        {
            if (target == null) return;
            MeleeSystem defender = target.GetComponent<MeleeSystem>();
            if (defender != null && defender.isActiveAndEnabled)
            {
                defender.Ledger.Add(info);
                return;
            }
            Health health = target.GetComponent<Health>();
            if (health != null) health.TakeDamage(info.Damage);
        }

        /// <summary>내 공격 기록을 만들 때 공통으로 채우는 칸 (공격자·맞교환 판단용 기록·공격 방향).</summary>
        public HitInfo NewHit(AttackType type, HitKind kind, float damage, GameObject target)
        {
            Vector3 direction = target != null ? target.transform.position - transform.position : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            return new HitInfo
            {
                Type = type,
                Kind = kind,
                Damage = damage,
                Attacker = gameObject,
                Source = Ledger,
                Direction = direction.normalized
            };
        }

        // ---------------- 근접 구역 ----------------

        private static float UnitMeters(VoMotor motor) =>
            motor != null && motor.IsActive ? motor.U : StyleOverrides.Current.Effective.fallbackBodyHeight / StyleOverrides.Current.Effective.bodyHeightUnits;

        /// <summary>이 기체의 1u(m). 이동기가 있으면 잰 기체 키 기준, 없으면(연습 봇) 기준 키 1.93m / 22.</summary>
        public float U => UnitMeters(Motor);

        /// <summary>
        /// 상대가 근접 구역 A 안인지 (전투 사양서 1.1: 바깥 70u, 안쪽 원 12.5u, 원뿔 반각 42.2°, 높이차 −24 ~ +13u).
        /// 공격 버튼이 펀치가 되는 조건. ignoreHeight = 높이차 창을 보지 않는다(뛰어 베기).
        /// 이동기가 있으면 이동기가 이번 틱 위치로 계산한 값, 없으면(연습 봇) 같은 식으로 직접 잰다.
        /// </summary>
        public bool IsOpponentInMeleeZone(bool ignoreHeight = false)
        {
            VoMotor motor = Motor;
            if (motor != null && motor.IsActive) return ignoreHeight ? motor.InMeleeZoneAnyHeight : motor.InMeleeZone;
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Vector3 offset = VoMotor.OpponentPosition(opponent.transform) - transform.position;
            float u = U;
            float height = -offset.y / u;
            offset.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            float angle = offset.sqrMagnitude > 0.000001f ? Vector3.Angle(forward, offset) : 0f;
            return VoMeleeZone.Evaluate(StyleOverrides.Current.EffectiveCombat, offset.magnitude / u, angle, height, ignoreHeight, out _);
        }

        // ---------------- 입력 소스 ----------------

        /// <summary>전투 입력 소스를 바꾼다. 연습 봇처럼 런타임에 입력 컴포넌트를 붙이는 기체용.</summary>
        public void SetInputSource(ICombatInputSource input)
        {
            _input = input;
            _lastGuardPressCount = input != null ? input.GuardPressCount : 0;
        }

        /// <summary>
        /// 같은 오브젝트의 부품(히트 게이지·체력·입력 소스·스킬)을 다시 찾는다.
        /// 연습 봇이 런타임에 HeatGauge 등을 붙였을 때, 이 컴포넌트가 먼저 Awake했어도 같은 규칙이 적용되게.
        /// </summary>
        public void RebindComponents()
        {
            _heatGauge = GetComponent<HeatGauge>();
            _health = GetComponent<Health>();
            _weapon = GetComponent<WeaponSystem>();
            _claw = GetComponent<AnchorClaw>();
            _slam = GetComponent<ImpactSlam>();
            _lunge = GetComponent<ThrusterLunge>();
            _dummy = GetComponent<MeleeDummy>();
            ICombatInputSource input = FindInputSource();
            if (input != null) SetInputSource(input);
        }

        private void Start()
        {
            // WeaponSystem이 Awake에서 런지를 붙였을 수 있다
            RebindComponents();
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

        // ---------------- 가드·패리 ----------------

        /// <summary>
        /// 가드 입력을 다음 틱까지 기다리지 않고 지금 한 번 처리한다. 규칙은 틱과 같다
        /// (스턴 중 불가, 재사용 대기, 대시 중 불가, 새로 누른 가드에만 패리 창).
        /// 곧 들어올 공격에 반응해야 하는 입력 소스(연습 봇의 패리 읽기)용.
        /// </summary>
        public void ApplyGuardInputNow()
        {
            if (IsStunned || IsKnockedDown) return;
            HandleGuard();
        }

        /// <summary>가드 버튼을 지난 틱 뒤에 새로 눌렀는지 (누름 횟수로 — 틱과 화면 프레임이 어긋나도 놓치지 않는다).</summary>
        private bool ReadGuardPress()
        {
            if (_input == null) return false;
            int count = _input.GuardPressCount;
            bool pressed = count != _lastGuardPressCount;
            _lastGuardPressCount = count;
            return pressed;
        }

        private void HandleGuard()
        {
            // 대시 중엔 가드 자체를 못 건다 (대시 = 공격적 상태, 방어자세 병행 불가).
            // V5: 공중(점프는 회피 대신 무방비)과 착지 경직 중에도 가드·패리 불가
            bool blocked = _stateMachine != null &&
                (_stateMachine.IsDashing || _stateMachine.IsAirborne || _stateMachine.IsLanding);
            bool pressed = ReadGuardPress();
            bool guardHeld = _input != null && (_input.GuardHeld || pressed);

            if (guardHeld && !blocked && (IsGuarding || _reraiseFrames <= 0))
            {
                // 가드는 자원을 쓰지 않는다 (대가는 막을 때 쌓이는 히트).
                if (!IsGuarding)
                {
                    IsGuarding = true;
                    // V9a: 가드는 ✕. 공중에서 ✕를 누른 채 착지하면 여기서 일반 가드로 이어지지만, 새로 누른 것이 아니라 패리는 안 열린다.
                    // 패리 창은 "버튼을 새로 누른 순간" 올라간 가드에만 연다.
                    // 재사용 대기 중 누르고 버티다 자동으로 올라간 가드까지 패리를 주면 대기가 의미 없어진다.
                    if (pressed) StartParryWindow();
                }

                // priority 10: 방어 자세는 플레이어가 의도적으로 유지 중인 상태라 다른 감속 요청보다 우선한다.
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
                _reraiseFrames = 0;
            }
            else if (applyReraiseDelay)
            {
                _reraiseFrames = Guard.reraiseDelayFrames;
            }
        }

        private void StartParryWindow()
        {
            _parryFrames = Parry.windowFrames;
            IsParrying = true;
        }

        private void EndParryWindow()
        {
            _parryFrames = 0;
            IsParrying = false;
        }

        private void HandleParry()
        {
            if (!IsParrying) return;
            // 창 길이만큼의 틱 동안 열려 있다 (연 틱 포함)
            if (_parryFrames <= 0) EndParryWindow();
            else _parryFrames--;
        }

        /// <summary>라운드 시작: 스턴·가드·패리·각종 타이머 초기화. 펀치·태클은 바로 쓸 수 있는 상태로.</summary>
        public void ResetForRound()
        {
            _stunFrames = 0;
            IsGuarding = false;
            EndParryWindow();
            _parryGrace = false;
            _reraiseFrames = 0;
            _meleeTimer = Long;
            _tackleTimer = Long;
            _punchElapsed = -1;
            _punchStrike = false;
            _sincePunchResolve = Long;
            _comboStep = 0;
            Ledger.Reset();
            if (_input != null) _lastGuardPressCount = _input.GuardPressCount;
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(Guard.releaseDamping);
        }

        /// <summary>frames틱 동안 행동 불가 (공격자 경직·훅 경직·가드 깨짐). 더 긴 쪽이 남는다.</summary>
        public void ApplyStun(int frames)
        {
            if (frames <= 0) return;
            // 발생 시간 중에 스턴·가드 깨짐을 당하면 그 펀치는 나가지 않는다
            if (_punchElapsed >= 0)
            {
                _punchElapsed = -1;
                _punchStrike = false;
                OnAttackCanceled?.Invoke(AttackType.Punch);
            }
            _stunFrames = Mathf.Max(_stunFrames, frames);
            // 맞아서 굳으면 콤보는 끊긴다 (다음 펀치는 원부터)
            _comboStep = 0;
        }

        // ---------------- 대시 어택·차징 태클 ----------------

        /// <summary>대시 어택·차징 태클의 발생 시간(틱). 0이면 즉시 판정.</summary>
        public int TackleStartupFrames(AttackType type)
        {
            return type == AttackType.Tackle ? Startup.chargedTackleFrames : Startup.dashAttackFrames;
        }

        /// <summary>
        /// 어깨 태클 시작(버튼을 누른 순간): 쿨다운을 쓰고 OnAttackStarted를 알린다. 쿨다운 중이면 false.
        /// 판정은 발생 시간 뒤 판정 구간 동안 닿으면 QueueTackleHit (DashAttackRunner가 진행).
        /// </summary>
        public bool BeginTackle(AttackType type)
        {
            if (!CanTackle) return false;
            _tackleTimer = 0;
            OnAttackStarted?.Invoke(type);
            return true;
        }

        /// <summary>발생 시간 중이던 태클이 스턴 등으로 취소됐음을 알린다 (외형·로그용).</summary>
        public void CancelTackle(AttackType type)
        {
            OnAttackCanceled?.Invoke(type);
        }

        /// <summary>
        /// 어깨 태클 판정 (틱 끝, 판정하는 순간의 위치·방향 기준). type은 DashAttack(가드에 막힘) 또는 Tackle(차징, 관통 + 스턴).
        /// forward = 부채꼴 기준 방향 (대시는 몸 정면과 진행 방향이 달라서 진행 방향으로 잰다). 상대 기록에 쌓았으면 true.
        /// </summary>
        public bool QueueTackleHit(float chargePower, AttackType type, Vector3 forward)
        {
            BernardTuning.TackleValues tackle = Tackle;
            bool charged = type == AttackType.Tackle;
            // 차징 태클은 기존 공식(차징 반영), 대시 어택은 고정 수치
            BernardTuning.DashAttackValues dash = DashAttack;

            // 정면 부채꼴 밖이면 헛침 (등 뒤·옆으로 스친 상대는 안 맞는다)
            if (!IsOpponentInTackleArc(forward)) return false;

            Collider[] hits = Physics.OverlapSphere(transform.position, tackle.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;
                HitInfo info = NewHit(type, charged ? HitKind.Strong : HitKind.Melee2,
                    charged ? tackle.damage * (1f + chargePower) : dash.damage, target);
                info.KnockbackForce = charged ? tackle.knockback * (1f + chargePower) : dash.knockback;
                info.StunFrames = CombatTime.Frames(charged ? tackle.victimStun : dash.stun);
                QueueHit(target, info);
                return true;
            }
            return false;
        }

        // ---------------- 뛰어 베기 ----------------

        /// <summary>뛰어 베기 (원본 점프 근접): 점프 입력 후 8f 이내 + 근접 구역(높이차 창 무시)이면 발동.</summary>
        public bool TryJumpSlash()
        {
            return false;
        }

        // ---------------- 펀치 ----------------

        /// <summary>
        /// 너클 펀치 시작. 쿨다운·과열·스턴·발생 중이면 안 나가고 false (입력 버퍼가 다시 시도한다).
        /// 시작했으면 true: 발생 시간이 지난 틱의 끝에 판정한다.
        /// </summary>
        public bool TryMeleeAttack()
        {
            if (_punchElapsed >= 0) return false;
            // 스턴 중엔 펀치를 시작할 수 없다 (런지·가드와 같은 규칙. 발생 중 스턴이면 취소되는 것과 짝). 넉다운도
            if (IsStunned || IsKnockedDown) return false;
            BernardTuning.PunchValues punch = Punch;
            BernardTuning.ComboValues combo = Combo;
            int step = NextComboStep(combo);
            if (combo.enabled && _comboStep == 3 && _sincePunchResolve < CombatTime.Frames(combo.thirdRecovery)) return false;
            // 콤보 다음 타는 짧은 간격, 원(새 콤보·단발)은 예전 쿨다운
            int needed = CombatTime.Frames(step >= 2 ? combo.interval : punch.cooldown);
            if (_meleeTimer < needed) return false;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return false;
            _meleeTimer = 0;
            _comboStep = step;
            if (step > 0) OnComboStep?.Invoke(step);
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(punch.heatPerPunch);
            OnAttackStarted?.Invoke(AttackType.Punch);
            _punchElapsed = 0;
            return true;
        }

        /// <summary>
        /// 지금 펀치를 시작하면 몇 번째 타인지 (N2). 원·투를 친 뒤 판정 순간부터 window 안이면 다음 타, 아니면 원.
        /// 쓰리 다음은 원. 콤보가 꺼져 있으면 0(단발).
        /// </summary>
        private int NextComboStep(BernardTuning.ComboValues combo)
        {
            if (!combo.enabled) return 0;
            if ((_comboStep == 1 || _comboStep == 2) && _punchElapsed < 0 && _sincePunchResolve <= CombatTime.Frames(combo.window))
            {
                return _comboStep + 1;
            }
            return 1;
        }

        /// <summary>펀치 판정 (틱 끝): 판정하는 순간의 내 정면·상대 위치 기준. 맞았으면 상대 기록에 쌓는다.</summary>
        private void ResolvePunch()
        {
            _punchElapsed = -1;
            _sincePunchResolve = 0;
            BernardTuning.PunchValues punch = Punch;
            BernardTuning.ComboValues combo = Combo;
            bool third = _comboStep == 3;

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !IsInFrontArc(opponent.transform, punch.halfAngle)) return;
            // V5: 위아래 차이가 크면(점프한 상대·고지대) 빗나감
            if (Mathf.Abs(opponent.transform.position.y - transform.position.y) > punch.heightTolerance) return;

            // 거리는 콜라이더 표면 기준(OverlapSphere)이라 덩치 큰 기체도 사거리가 자연스럽다
            Collider[] hits = Physics.OverlapSphere(transform.position, punch.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                // 쓰리는 피해 배율 + 넉백 (원·투는 예전 펀치 그대로)
                HitInfo info = NewHit(AttackType.Punch, HitKind.Melee,
                    third ? punch.damage * combo.thirdDamageMultiplier : punch.damage, opponent.gameObject);
                info.KnockbackForce = third ? combo.thirdKnockback : 0f;
                info.ComboStep = _comboStep;
                // 콜라이더가 여러 개여도 한 방은 한 번만 맞는다
                QueueHit(opponent.gameObject, info);
                return;
            }
        }

        /// <summary>상대가 forward 기준 ±Tackle.halfAngle 안인지 (대시 진행 방향 기준 판정용). 거리는 따로 본다.</summary>
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
