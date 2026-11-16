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
        private static VoCombatSpec C => StyleOverrides.Current.EffectiveCombat;

        public bool IsGuarding { get; private set; }
        /// <summary>넉다운 중(비행·미끄러짐·기상). 공격·가드·이동 입력 무시.</summary>
        public bool IsKnockedDown => _knockdown != null && _knockdown.IsKnockedDown;
        public bool IsParrying { get; private set; }
        /// <summary>행동 불가: 경직(피격 반응) 또는 우리 규칙의 경직(공격자 경직·훅 경직).</summary>
        public bool IsStunned => _stunFrames > 0 || IsStaggered;
        /// <summary>경직(피격 반응, 28f) 중. 입력을 전혀 처리하지 않고 버퍼에도 남기지 않는다.</summary>
        public bool IsStaggered => _react.IsStaggered;
        /// <summary>가드 경직(16f) 중. 끝나면 가드 유지로 돌아간다.</summary>
        public bool IsGuardStaggered => _react.IsGuardStaggered;
        /// <summary>경직·가드 경직으로 밀려나는 중 (이동기는 그동안 외부 속도로 움직인다).</summary>
        public bool IsPushed => _react.IsPushed;
        /// <summary>이동·공격·스킬 입력을 받지 않는 상태 (경직·가드 경직·공격자 경직).</summary>
        public bool IsActionLocked => IsStunned || IsGuardStaggered;
        /// <summary>근접 무적 중 (근접 계열 피격을 통째로 무시).</summary>
        public bool IsMeleeInvulnerable => _react.IsMeleeInvulnerable;
        /// <summary>충격치 누적값 (전투 사양서 2.1).</summary>
        public float Impact => _impact.Value;
        public bool CanTackle => _tackleTimer >= Tackle.cooldownFrames;
        public int TackleRecoveryFrames => Tackle.recoveryFrames;
        /// <summary>
        /// 히트스톱 중 (전투 사양서 2.4·9장): 기체별 15f. 그동안 모션(공격 단계·경직 시간)·수평 이동·다운계 중력이 멈추고 외형도 멈춘다.
        /// 무적·쿨다운·입력 카운터는 계속 돈다.
        /// </summary>
        public bool IsHitStopped => _react.IsHitStopped;

        /// <summary>내가 맞았을 때(피격자 쪽) 판정 결과가 적용되면 발생. 로그·연출은 여기 구독.</summary>
        public event Action<HitInfo, HitResult> OnHitResolved;
        /// <summary>내가 때린 공격의 결과가 피격자 쪽에서 적용됐을 때 (공격자 쪽 후속 처리·로그용).</summary>
        public event Action<HitInfo, HitResult> OnAttackResolved;
        /// <summary>내가 공격 버튼으로 공격을 시작했을 때(발생 시간 전, 맞든 안 맞든). 로그의 "발동" 카운트·예비 동작용.</summary>
        public event Action<AttackType> OnAttackStarted;
        /// <summary>발생 시간 도중 스턴·가드 깨짐 등으로 공격이 취소됐을 때.</summary>
        public event Action<AttackType> OnAttackCanceled;
        /// <summary>이 기체에 히트스톱이 걸렸을 때 (true = 때린 쪽으로, false = 맞은 쪽으로). 로그용.</summary>
        public event Action<bool> OnHitStopStarted;
        /// <summary>다운에서 일어나는 방식이 정해졌을 때 (그냥 / 공격 / 대시 / 점프). 로그용.</summary>
        public event Action<KnockTickResult> OnWokeUp;
        /// <summary>내 대시 공격·차징 태클이 끝났을 때 (landed = 상대에게 닿아 기록에 쌓았는지 — false면 빗나감). 로그용.</summary>
        public event Action<AttackType, bool> OnTackleEnded;

        /// <summary>
        /// 공격 버튼 한 번의 진단 기록이 끝났을 때 (펀치 휘두르기마다 한 번 + 펀치가 안 나간 누름). 로그·스모크용.
        /// </summary>
        public event Action<PunchTrace> OnPunchTraced;

        /// <summary>대시 공격 진행기(DashAttackRunner)가 공격을 끝냈다고 알린다.</summary>
        public void NotifyTackleEnded(AttackType type, bool landed) => OnTackleEnded?.Invoke(type, landed);

        private void BeginHitStop(bool asAttacker)
        {
            _react.BeginHitStop(C.hitStopFrames);
            OnHitStopStarted?.Invoke(asAttacker);
        }

        /// <summary>펀치(파고들기 → 공격 → 후딜) 중인지.</summary>
        public bool IsMeleeActive => ActiveDrive != null;

        /// <summary>마지막으로 시작한 펀치의 콤보 단계 (1 원, 2 투, 3 쓰리). 콤보가 끊겼거나 꺼져 있으면 0. 외형·로그용.</summary>
        public int ComboStep => _comboStep;
        /// <summary>콤보 단계가 정해진 순간(펀치 시작). OnAttackStarted 바로 앞에 온다.</summary>
        public event Action<int> OnComboStep;

        /// <summary>이 기체의 피격 기록.</summary>
        public CombatLedger Ledger { get; } = new CombatLedger();

        // ---- 틱 카운터 (전부 틱 수) ----
        private const int Long = 100000;
        // 지금 휘두르기의 콤보 단계 (1 원, 2 투, 3 쓰리. 0 = 휘두르는 중 아님)
        private int _comboStep;
        // 지금 휘두르기가 이미 맞았는지 (한 번 휘두를 때 한 상대에게 한 번만)
        private bool _swingHit;
        // 상태 머신이 없는 기체(연습 봇)의 근접 구동. 이동기가 있는 기체는 코어의 근접 상태(VoMeleeState)가 구동한다
        private readonly VoMeleeDrive _ownDrive = new VoMeleeDrive();
        private int _parryFrames;
        // 충격치 누적값·근접 무적
        private readonly ImpactMeter _impact = new ImpactMeter();
        // 히트스톱·근접 무적·경직(가드 경직) 카운터 (순수 로직)
        private readonly ReactionTimers _react = new ReactionTimers();
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
            Ledger.Decide(C, new DefenderPosture
            {
                Invulnerable = _knockdown != null && _knockdown.IsInvulnerable,
                MeleeInvulnerable = IsMeleeInvulnerable,
                Parrying = IsParrying,
                Guarding = IsGuarding,
                FacingYaw = FacingYaw,
                Airborne = _stateMachine != null && _stateMachine.IsAirborne,
                Dashing = _stateMachine != null && _stateMachine.IsDashing && !_stateMachine.IsAirborne,
                Impact = _impact.Value
            });
        }

        /// <summary>내 정면(Unity Y 회전, °).</summary>
        private float FacingYaw
        {
            get
            {
                VoMotor motor = Motor;
                return motor != null && motor.IsActive ? motor.Yaw : transform.eulerAngles.y;
            }
        }

        public void TickStandalone()
        {
            CombatTick();
            EndTick();
        }

        public void CollectHits()
        {
            // 히트스톱 중에는 공격도 멈춰 있다 (판정 없음)
            if (IsHitStopped) return;
            CollectPunchHit();
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

            if (_tackleTimer < Long) _tackleTimer++;
            if (_reraiseFrames > 0) _reraiseFrames--;
            if (_stunFrames > 0) _stunFrames--;
            // 히트 게이지 냉각도 전투 틱으로 (히트스톱 중에도 식는다)
            if (_heatGauge != null) _heatGauge.CombatTick();
            // 충격치는 매 틱 줄고, 무적 타이머도 매 틱 준다 (히트스톱 중에도)
            _impact.Tick(C.impactDecay);
            _react.TickInvuln();

            ApplyDecisions();
            TickPush();
            TickKnockdown();
            if (_stateMachine != null) _stateMachine.SyncKnockdownState();

            bool roundActive = Flow.GameManager.Instance == null || Flow.GameManager.Instance.IsRoundActive;
            if (IsHitStopped)
            {
                // 히트스톱: 쿨다운·입력 카운터만 돈다. 공격 진행·가드 전환·이동은 멈춘다 (이동기가 있으면 이동기도 이 틱을 건너뛴다)
                ReadGuardPress();
                if (_claw != null) _claw.CombatTick();
                if (_slam != null) _slam.CombatTick();
                if (_lunge != null) _lunge.CombatTick(frozen: true);
                if (_weapon != null) _weapon.CombatTickFrozen();
                // 봇 두뇌는 히트스톱 중에도 본다·누른다 (사람도 멈춘 동안 버튼을 누를 수 있다)
                if (_dummy != null) _dummy.CombatTick();
                if (_stateMachine == null && _rb != null && !IsKnockedDown && !IsPushed) MotionBridge.SetHorizontalVelocity(_rb, Vector3.zero);
                return;
            }
            if (IsGuardStaggered)
            {
                // 가드 경직: 가드는 그대로 유지, 입력은 받지 않는다 (끝나면 가드 유지로 돌아간다)
                ReadGuardPress();
            }
            else if (IsStunned || IsKnockedDown)
            {
                // 경직·넉다운 중엔 가드 불가 (올리고 있던 가드도 내림). 경직 자체가 벌이라 재사용 대기는 걸지 않는다
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
            if (_lunge != null) _lunge.CombatTick(frozen: false);
            if (_weapon != null) _weapon.CombatTick(roundActive);
            if (_dummy != null) _dummy.CombatTick();

            // 상태 머신이 없는 기체는 근접 구동을 여기서 직접 돌린다
            if (_stateMachine == null) TickOwnDrive();

            if (InputTrace.ShouldLog(this, "3 ICombatInputSource"))
            {
                string move = _input is InputReader reader ? reader.MoveInput.ToString() : "-";
                InputTrace.Log("3 ICombatInputSource", $"{gameObject.name} source={InputSourceName} move={move} " +
                    $"guardHeld={(_input != null && _input.GuardHeld)}");
            }
        }

        /// <summary>틱 끝 (이동 적분 뒤). 근접 구동이 이번 틱에 다음 타로 넘어갔거나 끝났는지 본다.</summary>
        public void EndTick()
        {
            // 히트스톱 카운트 (전투 사양서 4.2-②-9)
            if (_react.EndTick()) return;
            VoMeleeDrive drive = ActiveDrive;
            if (drive == null)
            {
                // 휘두르기가 끝났다(후딜 끝·점프/대시 취소)
                _comboStep = 0;
                CloseTrace("move");
                return;
            }
            if (drive.Step != _comboStep) BeginSwing(drive.Step);
        }

        // ---------------- 피격 적용 ----------------

        private void ApplyDecisions()
        {
            // 내가 때린 공격이 맞았다(또는 막혔다): 때린 쪽 히트스톱. 맞은 쪽이 이미 근접 무적이었으면 걸리지 않는다(판단 때 걸러짐)
            if (Ledger.AttackerStopPending) BeginHitStop(asAttacker: true);
            var decisions = Ledger.Decisions;
            Reaction heaviest = Reaction.None;
            HitInfo heaviestInfo = default;
            for (int i = 0; i < decisions.Count; i++)
            {
                HitDecision decision = decisions[i];
                ApplyDecision(decision);
                if (decision.Reaction > heaviest)
                {
                    heaviest = decision.Reaction;
                    heaviestInfo = decision.Info;
                }
            }
            // 같은 틱 여러 발이면 가장 무거운 반응 하나로 들어간다
            if (heaviest != Reaction.None) EnterReaction(heaviest, heaviestInfo);
            var reflects = Ledger.Reflects;
            for (int i = 0; i < reflects.Count; i++) ApplyReflect(reflects[i]);
            Ledger.ClearApplied();
        }

        /// <summary>판단된 피격 하나: 피해·충격치 누적, 알림. 반응 상태로 들어가는 것은 EnterReaction이 한다.</summary>
        private void ApplyDecision(HitDecision decision)
        {
            HitInfo info = decision.Info;
            info.Reaction = decision.Reaction;
            info.ArmoredByDash = decision.ArmoredByDash;
            switch (decision.Result)
            {
                case HitResult.Clash:
                    // 맞딜: 둘 다 피해 0 + 밀려남 (각자 자기 틱에 자기 몫만). 진행 중인 내 공격은 맞딜로 소진
                    CancelMelee(notify: false, reason: "clash");
                    if (_stateMachine != null && _stateMachine.IsDashing && _stateMachine.ActiveDash.IsAttacking) _stateMachine.ActiveDash.EndByClash();
                    ApplyKnockback(info.Direction, Punch.clashKnockback);
                    break;
                case HitResult.Parried:
                    _parryGrace = true;
                    break;
                case HitResult.Hit:
                case HitResult.GuardBroken:
                case HitResult.Guarded:
                    if (_health != null) _health.TakeDamage(decision.Damage);
                    _impact.Add(decision.ImpactAdded);
                    // 근접 계열 피격이면 맞은 쪽은 항상 히트스톱 15f + 근접 무적 4f (반응이 없는 약한 피격도)
                    if (info.IsMelee)
                    {
                        BeginHitStop(asAttacker: false);
                        GrantMeleeInvuln(C.hitInvulnFrames);
                    }
                    // 막을 때마다 히트가 오른다 (가드의 대가)
                    if (decision.Result == HitResult.Guarded && _heatGauge != null) _heatGauge.AddHeat(Guard.heatPerBlock);
                    break;
            }
            OnHitResolved?.Invoke(info, decision.Result);
            MeleeSystem attacker = info.Attacker != null ? info.Attacker.GetComponent<MeleeSystem>() : null;
            if (attacker != null) attacker.NotifyAttackResolved(info, decision.Result);
        }

        /// <summary>반응 상태로 들어간다 (전투 사양서 2.2). 정산한 틱에 바로 — 그 틱의 이동 적분부터 밀려남이 반영된다.</summary>
        private void EnterReaction(Reaction reaction, HitInfo info)
        {
            VoCombatSpec c = C;
            switch (reaction)
            {
                case Reaction.InvulnOnly:
                    // 점프 중·지상 대시 중의 경직급 피격: 반응 없이 무적만
                    GrantMeleeInvuln(c.reactInvulnFrames);
                    break;

                case Reaction.GuardStagger:
                    // 가드 경직: 공격 방향으로 밀리고, 근접 무적 16f. 끝나면 가드 유지로
                    BeginPush(c.guardStaggerFrames, c.guardStaggerSpeed, info.Direction, guard: true);
                    GrantMeleeInvuln(c.reactInvulnFrames);
                    break;

                case Reaction.Stagger:
                    InterruptActions();
                    BeginPush(c.staggerFrames, c.staggerSpeed, info.Direction, guard: false);
                    // 근접으로 경직했으면 근접 무적 16f
                    if (info.IsMelee) GrantMeleeInvuln(c.reactInvulnFrames);
                    break;

                case Reaction.Down:
                case Reaction.AirDown:
                case Reaction.Blown:
                    InterruptActions();
                    _react.PushFrames = 0;
                    // 다운·날아감·공중 다운에 들어가면 충격치 누적을 지운다 (경직으로는 안 지운다)
                    _impact.Clear();
                    if (_knockdown != null)
                    {
                        DownKind kind = info.Type == AttackType.PileDriver ? DownKind.PileDriver
                            : reaction == Reaction.Down ? DownKind.Down
                            : reaction == Reaction.AirDown ? DownKind.AirDown : DownKind.Blown;
                        _knockdown.Begin(kind, info.Direction, info.LaunchDelayFrames);
                    }
                    break;
            }
        }

        /// <summary>반응으로 끊기는 것: 가드, 펀치, 대시, 런지.</summary>
        private void InterruptActions()
        {
            if (IsGuarding) DropGuard(applyReraiseDelay: false);
            CancelMelee(notify: true, reason: "hit");
            if (_stateMachine != null && _stateMachine.IsDashing) _stateMachine.ActiveDash.EndByKnockback();
        }

        /// <summary>근접 무적을 frames틱 준다 (더 긴 쪽이 남는다).</summary>
        public void GrantMeleeInvuln(int frames)
        {
            _react.GrantInvuln(frames);
        }

        /// <summary>상대가 200u 안이면 0.78 (이동 사양서의 근접 감속 — 피격 밀림에도 걸린다), 아니면 1.</summary>
        public float CloseSlowScale
        {
            get
            {
                VoSpec v = StyleOverrides.Current.Effective;
                return TryGetOpponentRelation(out float distance, out _) && distance < v.closeSlowDistance ? v.closeSlowScale : 1f;
            }
        }

        private void BeginPush(int frames, float speed, Vector3 direction, bool guard)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
            _react.BeginPush(frames, speed, direction, guard);
        }

        /// <summary>경직·가드 경직 한 틱: 공격 방향으로 밀린다 (히트스톱이 아닌 틱마다, 근접 감속과 함께). 끝나면 멈춘다.</summary>
        private void TickPush()
        {
            if (!_react.IsPushed || _rb == null) return;
            if (IsKnockedDown)
            {
                _react.PushFrames = 0;
                return;
            }
            float speed = _react.TickPush(CloseSlowScale) * U * CombatTime.TickRate;
            MotionBridge.SetHorizontalVelocity(_rb, _react.PushDirection * speed, undamped: true);
        }

        /// <summary>넉다운 한 틱 + 기상 입력 (전투 사양서 2.3).</summary>
        private void TickKnockdown()
        {
            if (_knockdown == null || !_knockdown.IsKnockedDown) return;
            VoMotor motor = Motor;
            bool live = motor != null && motor.IsActive;
            bool dashHeld = false;
            if (_knockdown.IsWaitingWake)
            {
                bool attack = _weapon != null && _weapon.PeekAttackPressed();
                bool guard = _input != null && _input.GuardPressCount != _lastGuardPressCount;
                bool jump = live && motor.JumpNew;
                bool lever = live && motor.Lever.Direction >= 0 && motor.Lever.WasNeutral;
                bool mash = attack || guard || jump || lever || (live && motor.TurboNew);
                _knockdown.WakeInput(attack, jump, mash);
                dashHeld = live && motor.TurboHeld && motor.Lever.Direction >= 0 && motor.DashAllowed && _stateMachine != null;
            }
            KnockTickResult result = _knockdown.CombatTick(IsHitStopped, dashHeld);
            if (result == KnockTickResult.Recovered || result == KnockTickResult.WakeAttack ||
                result == KnockTickResult.WakeDash || result == KnockTickResult.WakeJump) OnWokeUp?.Invoke(result);
            VoCombatSpec c = C;
            switch (result)
            {
                case KnockTickResult.Recovered:
                    // 그냥 일어나면 무적이 길다
                    GrantMeleeInvuln(c.wakeInvulnFrames);
                    break;
                case KnockTickResult.WakeAttack:
                    // 바로 공격으로 일어나면 무적이 짧다. 공격은 이 틱의 공격 선택(구역이면 펀치, 아니면 런지)으로 나간다
                    GrantMeleeInvuln(c.wakeActionInvulnFrames);
                    if (_stateMachine != null) _stateMachine.ToStand();
                    if (_weapon != null) _weapon.BufferAttackPress();
                    break;
                case KnockTickResult.WakeDash:
                    if (_stateMachine != null)
                    {
                        _stateMachine.ToStand();
                        // 일반 대시와 달리 상대 방위 기준
                        _stateMachine.V8GroundDashFrom(motor.Lever.Direction, motor.HasOpponent ? motor.OpponentBearing : motor.Yaw);
                    }
                    break;
                case KnockTickResult.WakeJump:
                    if (_stateMachine != null)
                    {
                        _stateMachine.ToStand();
                        _stateMachine.TryStartJumpV8();
                    }
                    break;
            }
        }

        /// <summary>내가 때린 공격을 상대가 패리해 되돌아온 것: 돌진 중이라 가드가 없다 → 감쇄 없이 체력에 바로 + 밀려남.</summary>
        private void ApplyReflect(ReflectRecord reflect)
        {
            if (_health != null) _health.TakeDamage(reflect.Damage * Parry.reflectRatio);
            ApplyKnockback(reflect.Direction, Parry.reflectKnockback);
        }

        /// <summary>피격자가 내 공격의 결과를 알려 준다 (피격자의 정산 단계에서).</summary>
        private void NotifyAttackResolved(HitInfo info, HitResult result)
        {
            // 패리·맞딜당한 대시 공격은 공격자 경직이 없다 (되돌아온 넉백이 벌이다)
            if ((result == HitResult.Parried || result == HitResult.Clash) && info.ClashFamily == 2) _stunFrames = 0;
            if (info.Type == AttackType.Punch && _hasPendingTrace)
            {
                _hasPendingTrace = false;
                _pendingTrace.Result = PunchTrace.ResultOf(info, result);
                OnPunchTraced?.Invoke(_pendingTrace);
            }
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
        /// 입력(InputReader — 사람 패드든 연습 봇의 가상 패드든)이 있으면 그걸, 없으면 다른 입력 소스를 쓴다.
        /// GetComponent&lt;ICombatInputSource&gt;는 컴포넌트 순서대로 첫 번째를 집어서,
        /// 같은 기체에 다른 입력 소스가 붙어 있으면 InputReader 대신 그걸 읽을 수 있다.
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
            // 펀치 중에는 ① 파고들기에서만 가드로 갈 수 있다 (② 공격·③ 후딜은 불가)
            VoMeleeDrive drive = ActiveDrive;
            if (drive != null && drive.Phase != VoMeleeDrive.DrivePhase.Approach) blocked = true;
            bool pressed = ReadGuardPress();
            bool guardHeld = _input != null && (_input.GuardHeld || pressed);

            if (guardHeld && !blocked && (IsGuarding || _reraiseFrames <= 0))
            {
                // 가드는 자원을 쓰지 않는다 (대가는 막을 때 쌓이는 히트).
                if (!IsGuarding)
                {
                    IsGuarding = true;
                    // 파고들기(①) 중에 올린 가드는 펀치를 접고 가드로 넘어간다 (전투 사양서 1.3)
                    CancelMelee(notify: true, reason: "guard");
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
            _tackleTimer = Long;
            CancelMelee(notify: false, reason: "reset");
            _impact.Clear();
            _react.Reset();
            Ledger.Reset();
            if (_input != null) _lastGuardPressCount = _input.GuardPressCount;
            if (_stateMachine != null) _stateMachine.MechController.SetBaselineDamping(Guard.releaseDamping);
        }

        /// <summary>frames틱 동안 행동 불가 (우리 규칙: 공격자 경직·훅 경직). 더 긴 쪽이 남는다. 피격 반응의 경직은 EnterReaction이 건다.</summary>
        public void ApplyStun(int frames)
        {
            if (frames <= 0) return;
            // 휘두르는 중에 굳으면 그 펀치는 끊긴다 (다음 펀치는 원부터)
            CancelMelee(notify: true, reason: "stun");
            _stunFrames = Mathf.Max(_stunFrames, frames);
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

            // 정면 부채꼴 밖이면 헛침 (등 뒤·옆으로 스친 상대는 안 맞는다)
            if (!IsOpponentInTackleArc(forward)) return false;

            Collider[] hits = Physics.OverlapSphere(transform.position, tackle.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 상대 기체의 자식 콜라이더에 맞아도 컴포넌트는 루트(MechIdentity가 있는 곳)에서 찾는다
                GameObject target = _identity.Opponent.gameObject;
                // 차징 태클 = 강타(가드 불능, 날아감), 대시 공격 = 근접 계열 2(가드 가능, 때린 쪽이 멈추지 않는다)
                // 피해: 차징 태클 = 원본 C2(차징이 길수록 최대 maxChargeDamageScale배 — 우리 규칙), 대시 공격 = 원본 대시 근접 A
                VoCombatSpec c = C;
                HitInfo info = NewHit(type, charged ? HitKind.Strong : HitKind.Melee2,
                    charged ? c.Damage(c.damageC2) * Mathf.Lerp(1f, tackle.maxChargeDamageScale, chargePower) : c.Damage(c.damageDashMelee), target);
                info.Impact = charged ? c.impactStrong : c.impactDashMelee;
                QueueHit(target, info);
                return true;
            }
            return false;
        }

        // ---------------- 뛰어 베기 ----------------

        /// <summary>뛰어 베기 중인지.</summary>
        public bool IsJumpSlashing => _stateMachine != null && _stateMachine.IsJumpMelee;

        /// <summary>지금 뛰어 베기 입력 창 안인지: 점프 입력 후 8f 이내이고 점프 중(이륙·상승·하강)이거나 이번 틱에 점프를 누른 서기·걷기.</summary>
        public bool InJumpSlashWindow
        {
            get
            {
                VoMotor motor = Motor;
                if (_stateMachine == null || motor == null || !motor.IsActive) return false;
                if (motor.JumpPressFrames > C.jumpMeleeInputFrames) return false;
                MechState state = _stateMachine.CurrentState;
                if (state is Core.VirtualOn.States.VoJumpState) return true;
                return motor.JumpNew && (state is Core.VirtualOn.States.VoStandState || state is Core.VirtualOn.States.VoWalkState);
            }
        }

        /// <summary>
        /// 뛰어 베기 (원본 점프 근접, 전투 사양서 1.7): 점프 입력 후 8f 이내 + 근접 구역(높이차 창 무시)이면 발동.
        /// 이동은 코어의 점프 근접 상태가(vy 3.80, 진로 정면, 추적 2560), 판정은 여기서(근접 계열 3 — 때린 쪽이 멈추지 않는다).
        /// </summary>
        public bool TryJumpSlash()
        {
            if (IsActionLocked || IsKnockedDown) return false;
            if (!InJumpSlashWindow || !IsOpponentInMeleeZone(ignoreHeight: true)) return false;
            if (IsGuarding) DropGuard(applyReraiseDelay: false);
            _stateMachine.V8JumpMelee();
            // 펀치 콤보 단계와 같은 칸을 쓴다 (1 = 휘두르는 중) — 히트·콤보 알림은 펀치 전용이라 여기서 직접 시작한다
            _comboStep = 1;
            _swingHit = false;
            _hasLastCapsule = false;
            OnAttackStarted?.Invoke(AttackType.JumpSlash);
            return true;
        }

        // ---------------- 펀치 (파고들기 근접: 원 = 템진 A1 1타, 투 = A1 2타, 쓰리 = C1) ----------------

        /// <summary>지금 도는 근접 구동 (없으면 null). 이동기가 있는 기체는 코어 근접 상태의 것, 없는 기체는 내 것.</summary>
        private VoMeleeDrive ActiveDrive
        {
            get
            {
                if (_stateMachine != null)
                {
                    if (_stateMachine.IsMelee) return _stateMachine.VoMelee.Drive;
                    return _stateMachine.IsJumpMelee ? _stateMachine.VoJumpMelee.Drive : null;
                }
                return _ownDrive.IsActive ? _ownDrive : null;
            }
        }

        /// <summary>콤보 단계의 구동 수치 (전투 사양서 1.2).</summary>
        public static VoMeleeMove PunchMove(int step)
        {
            VoCombatSpec c = C;
            return step >= 3 ? c.c1 : (step == 2 ? c.a1Second : c.a1First);
        }

        /// <summary>버튼을 누른 뒤 판정 창이 열릴 때까지(초). 외형 예비 동작 길이·봇 반응용.</summary>
        public static float PunchWindupSeconds(int step)
        {
            VoMeleeMove m = PunchMove(Mathf.Max(1, step));
            return CombatTime.Seconds(m.approachFrames + m.hitStart);
        }

        /// <summary>판정 창이 열린 뒤 휘두르기가 끝날 때까지(초, 후딜 포함). 외형 회복 동작 길이용.</summary>
        public static float PunchRecoverySeconds(int step)
        {
            VoMeleeMove m = PunchMove(Mathf.Max(1, step));
            return CombatTime.Seconds(m.attackFrames - m.hitStart + m.recoveryFrames);
        }

        /// <summary>
        /// 펀치 버튼. 휘두르는 중이 아니면 원을 시작하고, ② 공격 단계면 다음 타를 예약한다(② 끝에서 ③을 건너뛰고 다음 타 ①로. 쓰리 뒤는 없음).
        /// 과열이면 시작도 예약도 안 된다. 못 받았으면 false (입력 버퍼가 다시 시도한다).
        /// </summary>
        public bool TryMeleeAttack()
        {
            if (IsStunned || IsKnockedDown) return false;
            // 과열이면 주먹이 안 나간다. 히트게이지가 없는 기체(더미)는 제한 없음
            if (_heatGauge != null && !_heatGauge.CanPunch()) return false;

            VoMeleeDrive drive = ActiveDrive;
            // 뛰어 베기는 콤보로 이어지지 않는다
            if (IsJumpSlashing) return false;
            if (drive != null)
            {
                if (drive.Step >= 3) return false;
                return drive.Queue(PunchMove(drive.Step + 1));
            }

            if (_stateMachine != null)
            {
                if (!_stateMachine.CanStartLunge) return false;
                if (IsGuarding) DropGuard(applyReraiseDelay: false);
                _stateMachine.V8Melee(PunchMove(1), 1);
            }
            else
            {
                if (_lunge != null && _lunge.IsLunging) return false;
                if (IsGuarding) DropGuard(applyReraiseDelay: false);
                BeginOwnDrive();
            }
            BeginSwing(1);
            return true;
        }

        /// <summary>휘두르기 하나의 시작 (원, 또는 콤보로 넘어간 투·쓰리): 히트 증가, 알림, 한 번 맞기 표시 초기화.</summary>
        private void BeginSwing(int step)
        {
            // 앞 휘두르기가 판정 없이 끝났으면(콤보로 넘어감) 그 기록부터 닫는다
            CloseTrace(null);
            _comboStep = step;
            _swingHit = false;
            _hasLastCapsule = false;
            OpenTrace(step);
            OnComboStep?.Invoke(step);
            // 맞든 빗나가든 휘두른 순간 히트가 오른다
            if (_heatGauge != null) _heatGauge.AddHeat(Punch.heatPerPunch);
            OnAttackStarted?.Invoke(AttackType.Punch);
        }

        /// <summary>진행 중인 펀치를 끊는다 (경직·맞딜·가드 전환·라운드 리셋). notify면 외형·로그에 취소를 알린다.</summary>
        /// <summary>전투 허브가 마지막으로 펀치를 끊은 이유 (clash·hit·guard·stun·reset)와 그 틱. 로그의 근접 취소용.</summary>
        public string LastMeleeCancelReason { get; private set; }
        public int LastMeleeCancelTick { get; private set; } = -1;

        private void CancelMelee(bool notify, string reason)
        {
            if (ActiveDrive != null)
            {
                LastMeleeCancelReason = reason;
                LastMeleeCancelTick = VoTickDriver.TickCount;
            }
            bool active = ActiveDrive != null;
            if (_stateMachine != null)
            {
                if (_stateMachine.IsMelee) _stateMachine.ToStand();
            }
            else
            {
                _ownDrive.Cancel();
            }
            _comboStep = 0;
            if (active) CloseTrace(reason);
            if (active && notify) OnAttackCanceled?.Invoke(AttackType.Punch);
        }

        // ---- 상태 머신이 없는 기체(연습 봇)의 근접 구동: 같은 VoMeleeDrive를 리지드바디로 ----

        private bool TryGetOpponentRelation(out float distanceU, out float signedAngle)
        {
            distanceU = float.MaxValue;
            signedAngle = 0f;
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Vector3 offset = VoMotor.OpponentPosition(opponent.transform) - transform.position;
            offset.y = 0f;
            distanceU = offset.magnitude / U;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            signedAngle = offset.sqrMagnitude > 0.000001f ? Vector3.SignedAngle(forward, offset, Vector3.up) : 0f;
            return true;
        }

        private void BeginOwnDrive()
        {
            bool has = TryGetOpponentRelation(out float distance, out _);
            _ownDrive.Begin(PunchMove(1), 1, has, distance, C);
        }

        private void TickOwnDrive()
        {
            if (!_ownDrive.IsActive || _rb == null) return;
            VoCombatSpec c = C;
            bool has = TryGetOpponentRelation(out float distance, out float angle);
            // 추적 회전: 최대율까지만 돌고, 못 따라간 틱은 속력 × 0.1
            bool saturated = false;
            if (has && _ownDrive.WantsTurn(c, distance <= c.zoneInner))
            {
                float max = VoUnits.AngleUnitsToDegrees(_ownDrive.Move.trackRate);
                saturated = Mathf.Abs(angle) > max;
                float turn = Mathf.Clamp(angle, -max, max);
                _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, turn, 0f));
                angle -= turn;
            }
            float speed = _ownDrive.Tick(c, StyleOverrides.Current.Effective, has, distance, angle, saturated, out _);
            Vector3 forward = _rb.rotation * Vector3.forward;
            forward.y = 0f;
            MotionBridge.SetHorizontalVelocity(_rb, forward.normalized * (speed * U * CombatTime.TickRate));
        }

        // ---------------- 공격 버튼 진단 ----------------

        private PunchTrace _trace;
        private bool _traceOpen;
        // 명중을 상대 기록에 쌓고 결과(다음 틱 정산)를 기다리는 기록
        private PunchTrace _pendingTrace;
        private bool _hasPendingTrace;

        /// <summary>상대까지 수평 거리(m)·정면에서 벗어난 각(°, 오른쪽 +)·높이차(나 − 상대, m). 상대가 없으면 false.</summary>
        public bool TryGetOpponentRelationMeters(out float distance, out float angle, out float heightDiff)
        {
            distance = 0f;
            angle = 0f;
            heightDiff = 0f;
            VoMotor motor = Motor;
            if (motor != null && motor.IsActive)
            {
                if (!motor.HasOpponent) return false;
                distance = motor.OpponentDistanceU * motor.U;
                angle = motor.OpponentAngleOff;
                heightDiff = motor.OpponentHeightDiffU * motor.U;
                return true;
            }
            if (!TryGetOpponentRelation(out float distanceU, out angle)) return false;
            distance = distanceU * U;
            heightDiff = transform.position.y - _identity.Opponent.transform.position.y;
            return true;
        }

        private PunchTrace NewTrace(int step)
        {
            var trace = new PunchTrace { Step = step, MinGap = float.MaxValue, InZone = IsOpponentInMeleeZone() };
            TryGetOpponentRelationMeters(out trace.StartDistance, out trace.StartAngle, out trace.StartHeightDiff);
            return trace;
        }

        private void OpenTrace(int step)
        {
            _trace = NewTrace(step);
            _traceOpen = true;
        }

        /// <summary>
        /// 맞히지 못하고 끝난 휘두르기의 기록을 닫는다. 판정 창을 한 틱이라도 지났으면 사거리 밖(miss_range),
        /// 창이 열리기 전에 끊겼으면 끊긴 이유(canceled_…). cancelReason이 null이면 창을 다 지나고 끝난 것.
        /// </summary>
        private void CloseTrace(string cancelReason)
        {
            if (!_traceOpen) return;
            _traceOpen = false;
            _trace.Result = _trace.WindowTicks > 0 || cancelReason == null ? PunchTrace.MissRange : "canceled_" + cancelReason;
            OnPunchTraced?.Invoke(_trace);
        }

        /// <summary>
        /// 공격 버튼을 눌렀는데 펀치가 아닌 것이 됐을 때의 기록 (WeaponSystem이 부른다):
        /// 근접 구역 밖이라 런지로 바뀜(zone_out_lunge) / 런지 쿨다운이라 아무것도 안 나감(lunge_cooldown).
        /// </summary>
        public void TraceAttackPress(string result)
        {
            PunchTrace trace = NewTrace(0);
            trace.Result = result;
            OnPunchTraced?.Invoke(trace);
        }

        // 공격 캡슐 축의 지난 틱 위치 (쓸기 시작). 휘두르는 동안 틱마다 갱신
        private Vector3 _lastCapsuleNear, _lastCapsuleFar;
        private bool _hasLastCapsule;

        /// <summary>
        /// 펀치 판정 (틱 끝, 모든 기체가 움직인 뒤). 우리 근사: 몸 앞 start(m)부터 사거리 끝까지 이어진 공격 캡슐(손 높이)을
        /// 지난 틱 위치 → 이번 틱 위치로 쓸어서 상대 피격 캡슐과 겹치면 명중. 판정 창이 열린 틱에만 보고, 한 번 휘두를 때 한 상대에게 한 번만 맞는다.
        /// 예전엔 사거리 끝(1.8m)의 구 하나라, 1m 안에 붙은 상대가 정면에서 26° 넘게 벗어나면 구가 닿지 않았다.
        /// </summary>
        private void CollectPunchHit()
        {
            VoMeleeDrive drive = ActiveDrive;
            if (drive == null)
            {
                _hasLastCapsule = false;
                return;
            }
            StyleOverrides style = StyleOverrides.Current;
            bool slash = IsJumpSlashing;
            StyleOverrides.AttackCapsule shape = slash ? style.jumpSlashCapsule : (_comboStep >= 3 ? style.thirdCapsule : style.punchCapsule);
            AttackCapsulePoints(shape.start, shape.range, out Vector3 near, out Vector3 far);
            Vector3 fromNear = _hasLastCapsule ? _lastCapsuleNear : near;
            Vector3 fromFar = _hasLastCapsule ? _lastCapsuleFar : far;
            _lastCapsuleNear = near;
            _lastCapsuleFar = far;
            _hasLastCapsule = true;
            if (!drive.HitActiveThisTick || _swingHit) return;

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;
            float gap = SweepGapToOpponent(fromNear, fromFar, near, far, shape.radius, opponent.gameObject);
            if (_traceOpen)
            {
                _trace.WindowTicks++;
                _trace.MinGap = Mathf.Min(_trace.MinGap, gap);
            }
            if (gap > 0f) return;

            VoCombatSpec c = C;
            if (slash)
            {
                // 뛰어 베기: 근접 계열 3, 다운급. 피해 = 원본 점프 근접 A
                HitInfo slashInfo = NewHit(AttackType.JumpSlash, HitKind.Melee3, c.Damage(c.damageJumpMelee), opponent.gameObject);
                slashInfo.Impact = c.impactJumpMelee;
                QueueHit(opponent.gameObject, slashInfo);
                _swingHit = true;
                return;
            }
            bool third = _comboStep == 3;
            // 피해 = 원본 A1 1타 / A1 2타 / C1 × 체력 스케일. 충격치: 원·투는 경직만(우리 값 280), 쓰리는 원본 C1 1100
            float damage = c.Damage(third ? c.damageC1 : (_comboStep == 2 ? c.damageA1Second : c.damageA1First));
            HitInfo info = NewHit(AttackType.Punch, HitKind.Melee, damage, opponent.gameObject);
            info.Impact = third ? c.impactC1 : (_comboStep == 2 ? c.impactA1Second : c.impactA1First);
            info.ComboStep = _comboStep;
            QueueHit(opponent.gameObject, info);
            _swingHit = true;
            if (_traceOpen)
            {
                // 결과(명중·막힘·무적·슈퍼아머)는 상대의 다음 틱 정산에서 온다
                _traceOpen = false;
                _pendingTrace = _trace;
                _hasPendingTrace = true;
            }
        }

        /// <summary>지금 내 위치·정면 기준 공격 캡슐 축의 두 끝: 손 높이에서 기체 중심 앞 start(m) ~ range(m).</summary>
        public void AttackCapsulePoints(float start, float range, out Vector3 near, out Vector3 far)
        {
            VoMotor motor = Motor;
            bool live = motor != null && motor.IsActive;
            Vector3 position = live ? motor.Position : (_rb != null ? _rb.position : transform.position);
            Vector3 forward = live ? motor.Forward : transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            float feet = position.y + FootOffset;
            float height = BodyHeightMeters * StyleOverrides.Current.attackHandHeightRatio;
            Vector3 hand = new Vector3(position.x, feet + height, position.z);
            near = hand + forward * start;
            far = hand + forward * range;
        }

        /// <summary>
        /// 공격 캡슐(축 near-far)이 from → to로 쓸고 간 자취와 target의 피격 캡슐 사이의 틈(m). 0 이하면 명중.
        /// </summary>
        public static float SweepGapToOpponent(Vector3 fromNear, Vector3 fromFar, Vector3 toNear, Vector3 toFar, float radius, GameObject target)
        {
            MeleeSystem defender = target.GetComponent<MeleeSystem>();
            if (defender != null)
            {
                defender.GetHurtCapsule(out Vector3 a, out Vector3 b, out float r);
                return HitGeometry.SweptCapsuleGap(fromNear, fromFar, toNear, toFar, radius, a, b, r);
            }
            // 전투 허브가 없는 대상: 콜라이더 표면까지의 거리로 (축의 가운데에서)
            Collider col = target.GetComponent<Collider>();
            if (col == null) return float.MaxValue;
            Vector3 mid = (toNear + toFar) * 0.5f;
            return (col.ClosestPoint(mid) - mid).magnitude - radius - (toFar - toNear).magnitude * 0.5f;
        }

        private CapsuleCollider _capsule;
        private bool _capsuleLooked;

        private CapsuleCollider Capsule
        {
            get
            {
                if (!_capsuleLooked)
                {
                    _capsuleLooked = true;
                    _capsule = GetComponent<CapsuleCollider>();
                }
                return _capsule;
            }
        }

        /// <summary>기체 루트에서 발바닥까지(m, 보통 음수).</summary>
        private float FootOffset
        {
            get
            {
                CapsuleCollider capsule = Capsule;
                return capsule != null ? capsule.center.y - capsule.height * 0.5f : 0f;
            }
        }

        /// <summary>기체 키(m): 이동기가 잰 값, 없으면 기준 키.</summary>
        private float BodyHeightMeters
        {
            get
            {
                VoMotor motor = Motor;
                return motor != null && motor.IsActive ? motor.BodyHeight : StyleOverrides.Current.Effective.fallbackBodyHeight;
            }
        }

        /// <summary>
        /// 내 피격 캡슐 (축의 두 끝·반지름, 월드). 기체의 캡슐 콜라이더 그대로이고, 점프 중에는 줄어든다
        /// (상승 × 0.6, 하강 × 0.45 — 원본의 피격 구 7개 → 3개 → 2개를 흉내 낸 우리 근사).
        /// </summary>
        public void GetHurtCapsule(out Vector3 a, out Vector3 b, out float radius)
        {
            VoMotor motor = Motor;
            Vector3 position = motor != null && motor.IsActive ? motor.Position : (_rb != null ? _rb.position : transform.position);
            CapsuleCollider capsule = Capsule;
            float height = capsule != null ? capsule.height : BodyHeightMeters;
            float r = capsule != null ? capsule.radius : 0.5f;
            Vector3 center = position + (capsule != null ? new Vector3(capsule.center.x, 0f, capsule.center.z) : Vector3.zero);
            float scale = 1f;
            if (_stateMachine != null && _stateMachine.CurrentState is Core.VirtualOn.States.VoJumpState jump && jump.IsAirState)
            {
                StyleOverrides style = StyleOverrides.Current;
                scale = jump.IsRising ? style.jumpRisingHurtScale : style.jumpFallingHurtScale;
            }
            HitGeometry.UprightCapsule(center, position.y + FootOffset, height, r, scale, out a, out b, out radius);
        }

        /// <summary>상대가 forward 기준 ±Tackle.halfAngle 안인지 (대시 진행 방향 기준 판정용). 거리는 따로 본다.</summary>
        public bool IsOpponentInTackleArc(Vector3 forward)
        {
            MechIdentity opponent = _identity.Opponent;
            return opponent != null && IsInFrontArc(opponent.transform, Tackle.halfAngle, forward);
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
