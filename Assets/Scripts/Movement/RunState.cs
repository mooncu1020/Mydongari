using UnityEngine;
using MyDongari.Input;

namespace MyDongari.Movement
{
    /// <summary>
    /// 항상 달리기 (V3). L스틱을 기울인 만큼 즉시 그 속도 — 하드락 밖은 몸 기준 최대 20m/s,
    /// 하드락 안은 상대 기준 근접 이동 최대 8m/s (거리로 저절로 바뀐다, MechController.ApplyRunVelocity).
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

            // 임시: 키보드 Space 전력질주 (3단계 ○ 대시가 대신할 때까지). 패드 진입 경로는 없다
            if (input.SprintInput && !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.SprintState);
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
