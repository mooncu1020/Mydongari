using UnityEngine;

namespace MyDongari.Movement
{
    public class WalkState : MechState
    {
        public WalkState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Walk 진입");
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

            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.64f)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.InputReader.RunInput)
            {
                StateMachine.ChangeState(StateMachine.RunState);
            }
        }

        public override void FixedUpdate()
        {
            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 camForward = StateMachine.MechController.CameraTransform.forward;
            Vector3 camRight = StateMachine.MechController.CameraTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;
            Rigidbody rb = StateMachine.MechController.Rb;

            rb.AddForce(moveDir * StateMachine.MechController.WalkSpeed, ForceMode.Acceleration);
            StateMachine.MechController.ClampVelocity(StateMachine.MechController.MaxWalkSpeed);

            StateMachine.MechController.RotateToCamera();
        }

        public override void Exit()
        {
            Debug.Log("Walk 탈출");
        }
    }
}