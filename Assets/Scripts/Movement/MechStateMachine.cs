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

        private void Awake()
        {
            InputReader = GetComponent<InputReader>();
            MechController = GetComponent<MechController>();

            IdleState = new IdleState(this);
            WalkState = new WalkState(this);
            RunState = new RunState(this);
            SprintState = new SprintState(this);
            BrakeState = new BrakeState(this);
        }

        private void Start()
        {
            ChangeState(IdleState);
        }

        public void ChangeState(MechState newState)
        {
            _currentState?.Exit();
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