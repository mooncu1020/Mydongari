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

        public bool IsSprinting => _currentState is SprintState;

        public bool IsStunned => MechController.MeleeSystem != null && MechController.MeleeSystem.IsStunned;
        public bool IsWalking => _currentState is WalkState;
        public bool IsIdle => _currentState is IdleState;

        public bool IsRunning => _currentState is RunState;

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
        }

        private void Start()
        {
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
        }
    }
}