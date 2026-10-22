using UnityEngine;
using MyDongari.Input;

namespace MyDongari.Movement
{
    public class MechStateMachine : MonoBehaviour
    {
        public InputReader InputReader { get; private set; }
        public MechController MechController { get; private set; }

        private MechState _currentState;

        public IdleState IdleState { get; private set; }
        public WalkState WalkState { get; private set; }
        public RunState RunState { get; private set; }
        public SprintState SprintState { get; private set; }
        public BrakeState BrakeState { get; private set; }
        public SlideState SlideState { get; private set; }
        public SideStepState SideStepState { get; private set; }
        public QuickTurnState QuickTurnState { get; private set; }
        public LungeState LungeState { get; private set; }

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
        }

        private void Update()
        {
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