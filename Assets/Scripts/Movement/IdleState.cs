using UnityEngine;

namespace MyDongari.Movement
{
    public class IdleState : MechState
    {
        public IdleState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Idle 진입");
        }

        public override void Update()
        {
            if (StateMachine.InputReader.QuickTurnInput)
            {
                StateMachine.ChangeState(StateMachine.QuickTurnState);
                return;
            }

            if (StateMachine.InputReader.SideStepDirection != 0)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            if (StateMachine.InputReader.MoveInput.sqrMagnitude >= 0.64f)
            {
                StateMachine.ChangeState(StateMachine.WalkState);
            }
        }

        public override void FixedUpdate()
        {
            StateMachine.MechController.RotateToCamera();
        }


        public override void Exit()
        {
            Debug.Log("Idle 탈출");
        }
    }
}