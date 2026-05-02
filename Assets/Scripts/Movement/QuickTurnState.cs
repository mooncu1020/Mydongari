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
            Debug.Log("QuickTurn 진입");
            _quickTurnTimer = 0f;
            _targetRotation = Quaternion.Euler(
                0f,
                StateMachine.MechController.transform.eulerAngles.y + 180f,
                0f
            );
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
            float t = _quickTurnTimer / QuickTurnDuration;
            StateMachine.MechController.Rb.MoveRotation(
                Quaternion.Slerp(StateMachine.MechController.Rb.rotation, _targetRotation, t)
            );

            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 moveDir = new Vector3(input.x, 0f, input.y).normalized;
            if (moveDir != Vector3.zero)
            {
                StateMachine.MechController.Rb.AddForce(moveDir * StateMachine.MechController.WalkSpeed, ForceMode.Acceleration);
                StateMachine.MechController.ClampVelocity(StateMachine.MechController.MaxWalkSpeed);
            }
        }
        public override void Exit()
        {
            Debug.Log("QuickTurn 탈출");
        }
    }
}