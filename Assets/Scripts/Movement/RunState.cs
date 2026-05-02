using UnityEngine;

namespace MyDongari.Movement
{
    public class RunState : MechState
    {
        public RunState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Run 진입");
        }

        public override void Update()
        {
            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.64f)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (!StateMachine.InputReader.RunInput)
            {
                StateMachine.ChangeState(StateMachine.WalkState);
                return;
            }

            if (StateMachine.InputReader.SprintInput)
            {
                StateMachine.ChangeState(StateMachine.SprintState);
            }
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            Debug.Log("Run 탈출");
        }
    }
}