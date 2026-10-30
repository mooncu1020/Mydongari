using UnityEngine;

namespace MyDongari.Movement
{
    public class QuickTurnState : MechState
    {
        private float _quickTurnTimer = 0f;
        private float QuickTurnDuration => StateMachine.MechController.Tuning.Movement.quickTurnDuration;
        private Quaternion _startRotation;
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
            // V1: 카메라가 몸을 따라오므로 카메라 대신 몸을 180° 돌린다 (V2에서 초신지선회 자체를 삭제)
            _startRotation = StateMachine.MechController.Rb.rotation;
            _targetRotation = _startRotation * Quaternion.Euler(0f, 180f, 0f);
        }

        public override void Update()
        {
            _quickTurnTimer += Time.deltaTime;

            if (_quickTurnTimer >= QuickTurnDuration)
            {
                if (StateMachine.InputReader.IsMoveInputActive)
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
            Vector3 moveDir = StateMachine.MechController.BodyRelativeDirection(input);
            if (moveDir != Vector3.zero)
            {
                StateMachine.MechController.Rb.AddForce(moveDir * StateMachine.MechController.WalkSpeed, ForceMode.Acceleration);
                StateMachine.MechController.ClampVelocity(StateMachine.MechController.MaxWalkSpeed);
            }

            float t = QuickTurnDuration > 0f ? Mathf.Clamp01(_quickTurnTimer / QuickTurnDuration) : 1f;
            StateMachine.MechController.Rb.MoveRotation(Quaternion.Slerp(_startRotation, _targetRotation, t));
        }

        public override void Exit()
        {
        }
    }
}