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
            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 moveDir = new Vector3(input.x, 0f, input.y).normalized;
            Rigidbody rb = StateMachine.MechController.Rb;

            rb.AddForce(moveDir * StateMachine.MechController.RunSpeed, ForceMode.Acceleration);
        }

        public override void Exit()
        {
            Debug.Log("Run 탈출");
        }
    }

}