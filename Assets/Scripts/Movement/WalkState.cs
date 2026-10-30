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
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.InputReader.QuickTurnInput &&
                !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.QuickTurnState);
                return;
            }

            if (StateMachine.InputReader.SideStepDirection != 0 &&
                !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            if (!StateMachine.InputReader.IsMoveInputActive)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.InputReader.RunInput &&
                !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.RunState);
            }
        }

        public override void FixedUpdate()
        {
            Vector2 input = StateMachine.InputReader.MoveInput;
            // V3: 이동 기준은 몸 (카메라는 몸을 따라오므로 화면 위 = 몸 정면)
            Vector3 moveDir = StateMachine.MechController.BodyRelativeDirection(input);

            StateMachine.MechController.ApplyMoveForce(
                moveDir,
                StateMachine.MechController.WalkSpeed,
                StateMachine.MechController.MaxWalkSpeed);
        }

        public override void Exit()
        {
        }
    }
}