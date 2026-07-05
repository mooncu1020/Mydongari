using UnityEngine;

namespace MyDongari.Movement
{
    public class QuickTurnState : MechState
    {
        private float _quickTurnTimer = 0f;
        private const float QuickTurnDuration = 0.15f;
        private Quaternion _targetRotation;

        public QuickTurnState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            if (!StateMachine.MechController.BoostGauge.TryConsume(
                StateMachine.MechController.BoostGauge.GetQuickTurnCost()))
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            _quickTurnTimer = 0f;
            StateMachine.MechController.MechCamera.LockToForward();
            StateMachine.MechController.MechCamera.QuickTurn();
        }

        public override void Update()
        {
            _quickTurnTimer += Time.deltaTime;

            if (_quickTurnTimer >= QuickTurnDuration)
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
            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 camForward = StateMachine.MechController.CameraTransform.forward;
            Vector3 camRight = StateMachine.MechController.CameraTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;
            if (moveDir != Vector3.zero)
            {
                StateMachine.MechController.Rb.AddForce(moveDir * StateMachine.MechController.WalkSpeed, ForceMode.Acceleration);
                StateMachine.MechController.ClampVelocity(StateMachine.MechController.MaxWalkSpeed);
            }

            StateMachine.MechController.RotateToCamera();
        }

        public override void Exit()
        {
        }
    }
}