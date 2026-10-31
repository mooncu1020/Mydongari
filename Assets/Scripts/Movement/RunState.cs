using UnityEngine;
using MyDongari.Input;

namespace MyDongari.Movement
{
    /// <summary>
    /// 걷기 (V6a, 템진 기준 — 이름은 예전 "달리기" 그대로). L스틱 0.3 이상이면 그 방향 최대 속도(앞 22 ~ 뒤대각 17.7m/s) 즉시.
    /// 하드락 밖은 몸 기준, 안은 상대 기준 방향이고 속도는 같다 (MechController.ApplyRunVelocity).
    /// </summary>
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

            if (StateMachine.CheckFall()) return;
            if (StateMachine.TryStartJump()) return;
            if (StateMachine.TryStartDash()) return;

            InputReader input = StateMachine.InputReader;
            if (input.SideStepDirection != 0 && !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            if (!input.IsMoveInputActive)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }
        }

        public override void FixedUpdate()
        {
            StateMachine.MechController.ApplyRunVelocity(StateMachine.InputReader.MoveInput);
        }

        public override void Exit()
        {
        }
    }
}
