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