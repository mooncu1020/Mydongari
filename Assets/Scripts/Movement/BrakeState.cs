using UnityEngine;

namespace MyDongari.Movement
{
    public class BrakeState : MechState
    {
        private float _brakeTimer = 0f;
        private const float BrakeDuration = 0.3f;

        public BrakeState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Brake 진입");
            _brakeTimer = 0f;
        }

        public override void Update()
        {
            _brakeTimer += Time.deltaTime;

            if (_brakeTimer >= BrakeDuration)
            {
                if (StateMachine.InputReader.MoveInput.sqrMagnitude >= 0.64f)
                {
                    StateMachine.ChangeState(StateMachine.WalkState);
                }
                else
                {
                    StateMachine.ChangeState(StateMachine.IdleState);
                }
            }
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            Debug.Log("Brake 탈출");
        }
    }
}