using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    public class MechStateMachine : MonoBehaviour
    {
        public InputReader InputReader { get; private set; }
        public MechController MechController { get; private set; }

        private MechState _currentState;

        /// <summary>상태가 실제로 바뀌었을 때(Enter에서 곧바로 다른 상태로 튕겨 나간 경우는 제외). 로그용.</summary>
        public event Action<MechState> OnStateChanged;

        public IdleState IdleState { get; private set; }
        public WalkState WalkState { get; private set; }
        public RunState RunState { get; private set; }
        public SprintState SprintState { get; private set; }
        public BrakeState BrakeState { get; private set; }
        public SlideState SlideState { get; private set; }
        public SideStepState SideStepState { get; private set; }
        public QuickTurnState QuickTurnState { get; private set; }
        public LungeState LungeState { get; private set; }

        /// <summary>패드 두 스틱 전력질주·슬라이딩 판정기 (E6). 매 프레임 상태 Update 전에 갱신된다.</summary>
        public TwinStickSprintInput TwinStick { get; } = new TwinStickSprintInput();

        public bool IsSprinting => _currentState is SprintState;

        public bool IsStunned => MechController.MeleeSystem != null && MechController.MeleeSystem.IsStunned;
        public bool IsWalking => _currentState is WalkState;
        public bool IsIdle => _currentState is IdleState;

        public bool IsRunning => _currentState is RunState;
        public bool IsLunging => _currentState is LungeState;

        /// <summary>스러스터 런지를 시작할 수 있는 상태(정지·걷기·달리기·제동). 다른 기동 중엔 불가.</summary>
        public bool CanStartLunge =>
            _currentState is IdleState || _currentState is WalkState ||
            _currentState is RunState || _currentState is BrakeState;

        /// <summary>
        /// 하드 락온 몸통 자동 회전을 허용하는 상태인지.
        /// 전력질주·슬라이딩·초신지선회(·런지)는 진행 방향이 정해진 기동이라 몸을 돌리면 안 된다.
        /// </summary>
        public bool AllowsHardLockFacing =>
            !(_currentState is SprintState) &&
            !(_currentState is SlideState) &&
            !(_currentState is QuickTurnState) &&
            !(_currentState is LungeState);

        public string CurrentStateName => _currentState != null ? _currentState.GetType().Name : "None";

        public Vector3 LastMoveDirection { get; set; }

        /// <summary>
        /// 카메라가 오른쪽 스틱(시점) 입력을 무시해야 하는지 (F2): 전력질주 진입 판정 중, 전력질주 중,
        /// 그리고 전력질주가 풀린 뒤 오른쪽 스틱이 중립으로 한 번 돌아올 때까지.
        /// 질주 조작에 쓰던 오른쪽 스틱이 그대로 카메라를 돌려 화면이 튀는 것을 막는다.
        /// </summary>
        public bool IsLookInputBlocked { get; private set; }
        /// <summary>카메라가 기체 진행 방향 뒤로 따라가야 하는지 (진입 판정 중·전력질주 중).</summary>
        public bool IsCameraFollowingVelocity { get; private set; }
        private bool _lookBlockedUntilNeutral;
        private bool _wasSprinting;

        private void Awake()
        {
            InputReader = GetComponent<InputReader>();
            MechController = GetComponent<MechController>();

            IdleState = new IdleState(this);
            WalkState = new WalkState(this);
            RunState = new RunState(this);
            SprintState = new SprintState(this);
            BrakeState = new BrakeState(this);
            SlideState = new SlideState(this);
            SideStepState = new SideStepState(this);
            QuickTurnState = new QuickTurnState(this);
            LungeState = new LungeState(this);
        }

        private void Start()
        {
            ChangeState(IdleState);
        }

        /// <summary>라운드 시작: 사망으로 꺼졌던 상태머신을 켜고 Idle로.</summary>
        public void ResetForRound()
        {
            enabled = true;
            LastMoveDirection = Vector3.zero;
            TwinStick.Reset();
            _lookBlockedUntilNeutral = false;
            _wasSprinting = false;
            ChangeState(IdleState);
        }

        public void ChangeState(MechState newState)
        {
            _currentState?.Exit();
            if (newState is SlideState)
            {
                newState = new SlideState(this);
            }
            _currentState = newState;
            _currentState.Enter();
            // Enter 안에서 부스트 부족 등으로 다른 상태로 넘어갔으면 그쪽 호출이 이미 알렸다
            if (_currentState == newState) OnStateChanged?.Invoke(newState);
        }

        private void Update()
        {
            // InputReader와 같이 히트스톱(timeScale 0) 중에도 실제 시간으로 잰다
            if (InputReader != null)
            {
                TwinStick.Tick(InputReader.MoveStickRaw, InputReader.LookStickRaw,
                    Time.unscaledTime, MechController.Tuning.Movement);
            }
            _currentState?.Update();
            UpdateLookBlock();
        }

        /// <summary>상태 Update 뒤(이번 프레임 상태가 정해진 뒤)에 카메라 입력 차단을 정한다. 카메라는 LateUpdate에서 읽는다.</summary>
        private void UpdateLookBlock()
        {
            if (InputReader == null)
            {
                IsLookInputBlocked = false;
                IsCameraFollowingVelocity = false;
                return;
            }

            BernardTuning.MovementValues m = MechController.Tuning.Movement;
            bool rightNeutral = InputReader.LookStickRaw.magnitude < m.cameraLookResumeNeutral;
            bool sprinting = IsSprinting;

            // 질주가 끝나는 프레임에 오른쪽 스틱이 아직 기울어 있으면(슬라이딩 ↖ 입력 포함) 중립으로 돌아올 때까지 막는다
            if (_wasSprinting && !sprinting) _lookBlockedUntilNeutral = !rightNeutral;
            else if (rightNeutral) _lookBlockedUntilNeutral = false;
            _wasSprinting = sprinting;

            bool judging = !sprinting && TwinStick.IsJudgingEntry(InputReader.MoveStickRaw, Time.unscaledTime, m);
            IsCameraFollowingVelocity = sprinting || judging;
            IsLookInputBlocked = IsCameraFollowingVelocity || _lookBlockedUntilNeutral;
        }

        private void FixedUpdate()
        {
            _currentState?.FixedUpdate();
            // 상태가 끝난 뒤에 돌려야 상태 쪽 회전과 겹치지 않는다 (RotateToCamera는 이때 양보함)
            MechController.ApplyHardLockFacing();
        }
    }
}