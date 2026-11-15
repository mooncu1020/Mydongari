using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core.VirtualOn;
using MyDongari.Core.VirtualOn.States;
using MyDongari.Game.Combat;
using MyDongari.Game.Knockdown;

namespace MyDongari.Game.Bot
{
    /// <summary>
    /// 봇의 눈 (사람과 같은 정보만): 상대의 위치·움직임·보이는 행동, 자기 상태. 상대 입력은 읽지 않는다.
    /// 상대는 틱마다 찍어 두고 "반응 지연"만큼 지난 장면을 준다 — 사람이 화면을 보고 알아차리는 데 걸리는 시간.
    /// 자기 상태는 지연 없이 지금 값이다.
    /// </summary>
    public class BotSenses
    {
        private const int HistorySize = 64;

        private readonly OpponentView[] _history = new OpponentView[HistorySize];
        private int _count;
        private int _head;

        private MechIdentity _watched;
        private MechStateMachine _oppMachine;
        private MeleeSystem _oppMelee;
        private KnockdownController _oppKnockdown;
        private Vector3 _lastPosition;
        private bool _hasLast;
        private SeenAction _lastAction;
        private int _actionFrames;
        private int _selfDashFrames;

        /// <summary>기록을 비운다 (라운드 시작·모드 전환).</summary>
        public void Clear()
        {
            _count = 0;
            _head = 0;
            _hasLast = false;
            _actionFrames = 0;
            _lastAction = SeenAction.None;
        }

        /// <summary>이번 틱의 상대를 찍어 둔다.</summary>
        public void Record(MechIdentity opponent, int tick)
        {
            if (opponent != _watched)
            {
                _watched = opponent;
                _oppMachine = opponent != null ? opponent.GetComponent<MechStateMachine>() : null;
                _oppMelee = opponent != null ? opponent.GetComponent<MeleeSystem>() : null;
                _oppKnockdown = opponent != null ? opponent.GetComponent<KnockdownController>() : null;
                Clear();
            }
            var view = new OpponentView { Tick = tick };
            if (opponent != null && opponent.gameObject.activeInHierarchy)
            {
                view.Valid = true;
                view.Position = VoMotor.OpponentPosition(opponent.transform);
                VoMotor m = VoMotor.Of(opponent.GetComponent<Rigidbody>());
                view.Yaw = m != null && m.IsActive ? m.Yaw : opponent.transform.eulerAngles.y;
                view.Velocity = _hasLast ? (view.Position - _lastPosition) * CombatTime.TickRate : Vector3.zero;
                _lastPosition = view.Position;
                _hasLast = true;
                view.Action = Classify();
                view.Airborne = _oppMachine != null && _oppMachine.IsAirborne;
                _actionFrames = view.Action == _lastAction ? _actionFrames + 1 : 0;
                _lastAction = view.Action;
                view.ActionFrames = _actionFrames;
            }
            else
            {
                _hasLast = false;
            }
            _history[_head] = view;
            _head = (_head + 1) % HistorySize;
            if (_count < HistorySize) _count++;
        }

        /// <summary>delayFrames 틱 전에 본 상대 (기록이 모자라면 가장 오래된 것).</summary>
        public OpponentView Seen(int delayFrames)
        {
            if (_count == 0) return default;
            int back = Mathf.Clamp(delayFrames, 0, _count - 1);
            int index = (_head - 1 - back + HistorySize * 2) % HistorySize;
            return _history[index];
        }

        /// <summary>보이는 모습으로 상대 행동을 분류한다 (상태·자세 — 입력은 안 본다).</summary>
        private SeenAction Classify()
        {
            if (_oppKnockdown != null && _oppKnockdown.IsKnockedDown) return SeenAction.Down;
            if (_oppMelee != null)
            {
                if (_oppMelee.IsGuardStaggered) return SeenAction.GuardStagger;
                if (_oppMelee.IsStaggered) return SeenAction.Stagger;
            }
            MechStateMachine sm = _oppMachine;
            if (sm == null) return SeenAction.Stand;
            if (sm.IsLunging) return SeenAction.Lunge;
            if (sm.IsJumpMelee) return SeenAction.JumpMelee;
            if (sm.IsMelee) return SeenAction.Melee;
            if (sm.IsDashing && sm.ActiveDash != null && sm.ActiveDash.IsAttacking) return SeenAction.DashAttack;
            if (_oppMelee != null && _oppMelee.IsGuarding) return SeenAction.Guard;
            if (sm.IsLanding) return SeenAction.Landing;
            MechState state = sm.CurrentState;
            if (state is VoCloseStepState) return SeenAction.CloseStep;
            if (state is VoAirDashState) return SeenAction.AirDash;
            if (state is VoDiveState) return SeenAction.Dive;
            if (sm.IsDashing || sm.IsDashStopping) return SeenAction.Dash;
            if (sm.IsAirborne) return SeenAction.Jump;
            if (sm.IsRunning) return SeenAction.Walk;
            return SeenAction.Stand;
        }

        /// <summary>자기 상태 (지금 값).</summary>
        public SelfView ReadSelf(VoMotor motor, MechStateMachine sm, MeleeSystem melee, ThrusterLunge lunge, KnockdownController knockdown)
        {
            var self = new SelfView();
            if (motor == null || !motor.IsActive || sm == null) return self;
            self.Valid = true;
            self.Position = motor.Position;
            self.Yaw = motor.Yaw;
            self.U = motor.U;
            self.Grounded = motor.IsGrounded;
            self.Airborne = sm.IsAirborne;
            self.Standing = sm.IsIdle || sm.IsRunning;
            self.Dashing = sm.IsDashing && !sm.IsAirborne;
            _selfDashFrames = self.Dashing ? _selfDashFrames + 1 : 0;
            self.DashFrames = _selfDashFrames;
            self.DashAllowed = motor.DashAllowed;
            self.Landing = sm.IsLanding;
            self.Lunging = sm.IsLunging;
            self.LungeReady = lunge != null && !lunge.IsOnCooldown;
            if (melee != null)
            {
                self.Melee = melee.IsMeleeActive;
                self.ComboStep = melee.ComboStep;
                self.Guarding = melee.IsGuarding;
                self.Locked = melee.IsActionLocked || melee.IsHitStopped;
            }
            self.KnockedDown = knockdown != null && knockdown.IsKnockedDown;
            self.WaitingWake = knockdown != null && knockdown.IsWaitingWake;
            return self;
        }
    }
}
