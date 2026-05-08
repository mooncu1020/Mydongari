using UnityEngine;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        public bool IsTackleCharged => _tackleChargeTimer >= 1.5f;
        public float TackleChargePower => Mathf.Clamp01((_tackleChargeTimer - 1.5f) / 1.5f);

        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Sprint 진입");
            _tackleChargeTimer = 0f;
            StateMachine.MechController.MechCamera.LockToForward();
        }

        public override void Update()
        {
            if (StateMachine.MechController.BoostGauge.IsOverheated)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;

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
                StateMachine.LastMoveDirection = moveDir;
            }

            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.25f ||
                !StateMachine.InputReader.SprintInput)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
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
                StateMachine.LastMoveDirection = moveDir;
            }

            Rigidbody rb = StateMachine.MechController.Rb;
            rb.AddForce(moveDir * StateMachine.MechController.SprintSpeed, ForceMode.Acceleration);
            StateMachine.MechController.ClampVelocity(StateMachine.MechController.MaxSprintSpeed);

            StateMachine.MechController.RotateToCamera();
        }

        public override void Exit()
        {
            Debug.Log("Sprint 탈출");
        }
    }
}