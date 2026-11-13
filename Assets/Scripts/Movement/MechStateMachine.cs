using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    /// <summary>
    /// 이동 상태 머신. 이동 상태는 버추얼온식 이동기(V8)의 서기·걷기·근접 스텝·지상 대시·대시 정지·점프·공중 대시·급강하이고,
    /// 상태는 Update/FixedUpdate가 아니라 MechMotor의 60Hz 틱에서 돈다.
    /// 그 위에 우리 게임 상태 셋(착지 경직·런지·넉다운)이 있다 — 이동기가 틱마다 대신 돌려 주고, 끝나면 ToStand / ToStandOrWalk로 돌아간다.
    /// 전투·외형·HUD가 묻는 것(IsDashing·IsAirborne …)도 여기서 답한다.
    /// (V6.1까지의 Idle/Run/Dash/Air/SideStep 상태와 이동기 선택 스위치는 CL3에서 지웠다.)
    /// </summary>
    public class MechStateMachine : MonoBehaviour
    {
        public InputReader InputReader { get; private set; }
        public MechController MechController { get; private set; }

        private MechState _currentState;

        /// <summary>상태가 실제로 바뀌었을 때(Enter에서 곧바로 다른 상태로 튕겨 나간 경우는 제외). 로그용.</summary>
        public event Action<MechState> OnStateChanged;

        /// <summary>대시(지상·공중)가 실제로 시작됐을 때. 로그·출발 분사 연출용.</summary>
        public event Action OnDashStarted;
        /// <summary>대시가 끝났을 때 (이유). 로그용.</summary>
        public event Action<DashExitReason> OnDashEnded;
        /// <summary>점프했을 때 (로그·연출용).</summary>
        public event Action OnJumped;

        /// <summary>대시·점프 상태가 부른다.</summary>
        public void NotifyDashStarted() => OnDashStarted?.Invoke();
        public void NotifyDashEnded(DashExitReason reason) => OnDashEnded?.Invoke(reason);
        public void NotifyJumped() => OnJumped?.Invoke();

        // ---- 게임 상태 (이동기가 틱마다 대신 돌린다) ----
        public LandingState LandingState { get; private set; }
        public LungeState LungeState { get; private set; }
        public KnockdownState KnockdownState { get; private set; }

        // ---- 이동 상태 ----
        /// <summary>이동기 (MechController가 붙인다).</summary>
        public MechMotor Motor
        {
            get
            {
                if (_motor == null) _motor = GetComponent<MechMotor>();
                return _motor;
            }
        }
        private MechMotor _motor;
        public VoStandState VoStand { get; private set; }
        public VoWalkState VoWalk { get; private set; }
        public VoCloseStepState VoCloseStep { get; private set; }
        public VoGroundDashState VoGroundDash { get; private set; }
        public VoDashStopState VoDashStop { get; private set; }
        public VoJumpState VoJump { get; private set; }
        public VoAirDashState VoAirDash { get; private set; }
        public VoDiveState VoDive { get; private set; }
        /// <summary>지금 상태.</summary>
        public MechState CurrentState => _currentState;

        /// <summary>
        /// 외형·연출용 "대시 모션 중" (V8.2): IsDashing + 앞 대시에서 바뀐 근접 스텝. 전환할 때 호버 활주·숙임·불꽃이 끊기지 않게.
        /// 가드·히트 같은 규칙은 IsDashing을 그대로 본다.
        /// </summary>
        public bool IsDashMotion => IsDashing || (_currentState is VoCloseStepState c && c.FromDash);
        /// <summary>대시 모션 중 진로 (외형이 몸을 트는 방향, 출발 분사 방향).</summary>
        public Vector3 DashMotionHeading
        {
            get
            {
                if (_currentState is VoCloseStepState c) return c.Heading;
                IDashMove dash = ActiveDash;
                return dash != null ? dash.Heading : transform.forward;
            }
        }

        /// <summary>지금 대시(지상·공중 대시 중이면 그것, 아니면 null). IsDashing과 같이 본다.</summary>
        public IDashMove ActiveDash => _currentState as IDashMove;

        /// <summary>○ 대시 중 (끝 미끄러짐 제외). 가드 불가·히트 냉각 감소·호버 활주 외형의 기준.</summary>
        public bool IsDashing => (_currentState is VoGroundDashState g && g.IsDashingPhase) ||
                                 _currentState is VoAirDashState;
        /// <summary>대시 끝 단계 중 (종료 16f 선형 감속, 대시 정지 포함). 대시 불가, 점프는 내내·공격은 3f째부터 (땅만).</summary>
        public bool IsDashStopping => _currentState is VoDashStopState ||
                                      (_currentState is VoGroundDashState g && g.IsEndPhase);
        /// <summary>대시 끝 단계·대시 정지에서 지금 공격을 받는지 (3f째부터).</summary>
        public bool DashEndAllowsAttack =>
            (_currentState is VoDashStopState s && s.AllowsAttack) ||
            (_currentState is VoGroundDashState g && g.EndAllowsAttack);
        /// <summary>공중 (점프·낙하·공중 대시). 가드·패리·앵커 클로·공격 불가, 외형 접지 보정 끔. 대공 판정의 "공중"도 이것.</summary>
        public bool IsAirborne => _currentState is VoState vo && vo.IsAirState;
        /// <summary>착지 경직 중. 이동·공격·대시·점프·가드 불가.</summary>
        public bool IsLanding => _currentState is LandingState;

        public bool IsStunned => MechController.MeleeSystem != null && MechController.MeleeSystem.IsStunned;
        public bool IsIdle => _currentState is VoStandState;

        public bool IsRunning => _currentState is VoWalkState;
        public bool IsLunging => _currentState is LungeState;

        private Combat.KnockdownController _knockdown;
        /// <summary>파일 드라이버 넉다운 중 (R1). 컴포넌트는 MeleeSystem이 런타임에 붙이기도 해서 처음 쓸 때 찾는다.</summary>
        public bool IsKnockedDown
        {
            get
            {
                if (_knockdown == null) _knockdown = GetComponent<Combat.KnockdownController>();
                return _knockdown != null && _knockdown.IsKnockedDown;
            }
        }

        /// <summary>스러스터 런지를 시작할 수 있는 상태(서기·걷기, 공격을 받는 대시 끝 단계). 다른 기동 중엔 불가.</summary>
        public bool CanStartLunge => _currentState is VoStandState || _currentState is VoWalkState ||
                                     ((IsDashStopping) && DashEndAllowsAttack);

        public string CurrentStateName => _currentState != null ? _currentState.GetType().Name : "None";

        private void Awake()
        {
            InputReader = GetComponent<InputReader>();
            MechController = GetComponent<MechController>();

            LandingState = new LandingState(this);
            LungeState = new LungeState(this);
            KnockdownState = new KnockdownState(this);

            VoStand = new VoStandState(this);
            VoWalk = new VoWalkState(this);
            VoCloseStep = new VoCloseStepState(this);
            VoGroundDash = new VoGroundDashState(this);
            VoDashStop = new VoDashStopState(this);
            VoJump = new VoJumpState(this);
            VoAirDash = new VoAirDashState(this);
            VoDive = new VoDiveState(this);
        }

        private void Start()
        {
            ToStand();
            // 공중에서 맞으면 그 자리에서 떨어져 착지 경직 (V5). 넉백은 수평만 지운다
            if (MechController.MeleeSystem != null) MechController.MeleeSystem.OnHitResolved += OnHitResolved;
        }

        private void OnDestroy()
        {
            if (MechController != null && MechController.MeleeSystem != null) MechController.MeleeSystem.OnHitResolved -= OnHitResolved;
        }

        private void OnHitResolved(Combat.HitInfo info, Combat.HitResult result)
        {
            if (!IsAirborne) return;
            if (result != Combat.HitResult.Hit && result != Combat.HitResult.GuardBroken) return;
            // 공중에서 맞으면 수평 이동을 끊고 그 자리에서 떨어진다(착지 경직). 넉백은 외부 속도로 따로 들어온다
            Motor.Vy = Mathf.Min(0f, Motor.Vy);
            if (_currentState is VoJumpState && !VoJump.IsRising) VoJump.MarkLandingLag();
            else V8Fall(Motor.Vy, true);
        }

        /// <summary>라운드 시작: 사망으로 꺼졌던 상태머신을 켜고 서기로.</summary>
        public void ResetForRound()
        {
            enabled = true;
            ToStand();
        }

        /// <summary>
        /// 착지: 경직이 필요하면(점프·공중 대시·공중 피격) landingLag(0.2초) 동안 LandingState.
        /// 그냥 떨어진 착지는 경직 없이 바로 걷기·서기 (먼지·흔들림은 둘 다).
        /// </summary>
        public void Land(bool needsLag)
        {
            BernardTuning.MovementValues m = MechController.Tuning.Movement;
            float duration = needsLag ? m.landingLag : 0f;
            LandingState.Prepare(duration);
            if (duration > 0f) ChangeState(LandingState);
            else ToStandOrWalk();
        }

        // ---- 중립 상태로 돌아가기 ----

        /// <summary>서기로 (런지·넉다운·사망·라운드 리셋에서 돌아올 때).</summary>
        public void ToStand()
        {
            ChangeState(VoStand);
        }

        /// <summary>이동 입력이 있으면 걷기, 없으면 서기로 (착지 뒤).</summary>
        public void ToStandOrWalk()
        {
            bool moving = InputReader != null && InputReader.IsMoveInputActive;
            ChangeState(moving ? (MechState)VoWalk : VoStand);
        }

        // ---- 이동 상태 전환 도우미 ----

        /// <summary>점프 시작 (△). 스턴이면 false.</summary>
        public bool TryStartJumpV8()
        {
            if (IsStunned) return false;
            VoJump.PrepareJump();
            ChangeState(VoJump);
            return true;
        }

        /// <summary>낙하 (발판 끝·공중 대시 끝·공중 피격). vy는 u/f.</summary>
        public void V8Fall(float vy, bool landingLag)
        {
            VoJump.PrepareFall(vy, landingLag);
            ChangeState(VoJump);
        }

        public void V8GroundDash(int code, bool fromWalk, bool closeStepWindow)
        {
            VoGroundDash.Prepare(code, fromWalk, closeStepWindow);
            ChangeState(VoGroundDash);
        }

        public void V8CloseStep(int code, bool fromDash)
        {
            VoCloseStep.Prepare(code, fromDash);
            ChangeState(VoCloseStep);
        }

        public void V8DashStop(Vector3 heading, float offset, bool airborne)
        {
            VoDashStop.Prepare(heading, offset, airborne);
            ChangeState(VoDashStop);
        }

        public void V8AirDash(int code)
        {
            VoAirDash.Prepare(code);
            ChangeState(VoAirDash);
        }

        public void V8Dive()
        {
            ChangeState(VoDive);
        }

        public void ChangeState(MechState newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
            // Enter 안에서 다른 상태로 넘어갔으면 그쪽 호출이 이미 알렸다
            if (_currentState == newState) OnStateChanged?.Invoke(newState);
        }

        private void Update()
        {
            // 넉다운이 시작되면 어느 상태에서든 넉다운 상태로 (입력 무시, 속도는 KnockdownController가).
            // 상태 자체는 MechMotor의 60Hz 틱에서 돈다
            if (IsKnockedDown && !(_currentState is KnockdownState)) ChangeState(KnockdownState);
        }
    }
}
