using UnityEngine;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        public bool IsTackleCharged => _tackleChargeTimer >= 1.5f;
        public float TackleChargePower => Mathf.Clamp01((_tackleChargeTimer - 1.5f) / 1.5f);

        private bool _isDashing = false;
        private float _dashTimer = 0f;
        private const float DashDuration = 0.15f;
        private const float DashSpeed = 50f;
        private Vector3 _dashDir;

        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Sprint 진입");
            _tackleChargeTimer = 0f;
            _isDashing = false;
            _dashTimer = 0f;
            StateMachine.MechController.MechCamera.LockToForward();
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.MechController.BoostGauge.IsOverheated)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;

            if (_isDashing)
            {
                _dashTimer += Time.deltaTime;
                if (_dashTimer >= DashDuration)
                {
                    StateMachine.ChangeState(StateMachine.BrakeState);
                }
                return;
            }

            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.25f ||
                !StateMachine.InputReader.SprintInput)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
            }
        }

        public override void FixedUpdate()
        {
            if (_isDashing)
            {
                StateMachine.MechController.Rb.linearVelocity = _dashDir * DashSpeed;
                return;
            }

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

            if (IsTackleCharged)
            {
                Collider[] hits = Physics.OverlapSphere(
                    StateMachine.MechController.transform.position, 2f);
                foreach (Collider hit in hits)
                {
                    if (!hit.CompareTag("Enemy")) continue;
                    _dashDir = (hit.transform.position -
                                StateMachine.MechController.transform.position).normalized;
                    _isDashing = true;
                    _dashTimer = 0f;
                    StateMachine.MechController.MeleeSystem.TryTackle(TackleChargePower);
                    return;
                }
            }
        }

        public override void Exit()
        {
            Debug.Log("Sprint 탈출");
        }
    }
}