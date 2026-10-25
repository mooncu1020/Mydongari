using System;
using UnityEngine;
using MyDongari.Input;

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
        }

        private void FixedUpdate()
        {
            _currentState?.FixedUpdate();
            // 상태가 끝난 뒤에 돌려야 상태 쪽 회전과 겹치지 않는다 (RotateToCamera는 이때 양보함)
            MechController.ApplyHardLockFacing();
        }
    }
}