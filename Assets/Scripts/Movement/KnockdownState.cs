using MyDongari.Combat;

namespace MyDongari.Movement
{
    /// <summary>
    /// 넉다운 중 (R1): 비행 → 착지 미끄러짐 → 기상. 이동 입력을 무시하고 아무 힘도 넣지 않는다 —
    /// 속도는 KnockdownController가 물리 스텝마다 직접 넣는다. 넉다운이 끝나면 Idle로.
    /// </summary>
    public class KnockdownState : MechState
    {
        public KnockdownState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
        }

        public override void Update()
        {
            if (!StateMachine.IsKnockedDown) StateMachine.ChangeState(StateMachine.IdleState);
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            StateMachine.MechController.SetBaselineDamping(2f);
        }
    }
}
