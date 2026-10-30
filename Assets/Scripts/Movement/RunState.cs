using UnityEngine;
using MyDongari.Input;

namespace MyDongari.Movement
{
    public class RunState : MechState
    {
        public RunState(MechStateMachine stateMachine) : base(stateMachine)
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

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.WalkState);
                return;
            }

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

            if (!StateMachine.InputReader.IsMoveInputActive)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (!StateMachine.InputReader.RunInput)
            {
                StateMachine.ChangeState(StateMachine.WalkState);
                return;
            }

            // 키보드 Space는 누르고 있으면 진입, 패드는 두 스틱 조작(오른쪽 스틱을 왼쪽 방향으로 꺾기)
            InputReader input = StateMachine.InputReader;
            if (input.SprintInput)
            {
                StateMachine.SprintState.PrepareEntry(byStick: false);
                StateMachine.ChangeState(StateMachine.SprintState);
            }
            else if (StateMachine.TwinStick.ConsumeEntry(
                         input.MoveStickRaw, input.LookStickRaw, StateMachine.MechController.Tuning.Movement))
            {
                StateMachine.SprintState.PrepareEntry(byStick: true);
                StateMachine.ChangeState(StateMachine.SprintState);
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

            StateMachine.MechController.ApplyMoveForce(
                moveDir,
                StateMachine.MechController.RunSpeed,
                StateMachine.MechController.MaxRunSpeed);

            StateMachine.MechController.RotateToCamera();
        }

        public override void Exit()
        {
        }
    }
}