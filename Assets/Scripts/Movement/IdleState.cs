using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 정지 (V3). L스틱이 중립이면 그 자리에 바로 선다 — 가속도 시스템이 없으니 감속 미끄러짐도 없다.
    /// 넉백·스턴·외부 제어 중엔 속도를 건드리지 않는다(MechController.StopHorizontal이 거른다).
    /// </summary>
    public class IdleState : MechState
    {
        public IdleState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
        }

        public override void Update()
        {
            if (StateMachine.IsStunned) return;

            if (StateMachine.InputReader.SideStepDirection != 0 &&
                !StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            if (StateMachine.InputReader.IsMoveInputActive)
            {
                StateMachine.ChangeState(StateMachine.RunState);
            }
        }

        public override void FixedUpdate()
        {
            StateMachine.MechController.StopHorizontal();
        }

        public override void Exit()
        {
        }
    }
}
