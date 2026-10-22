using MyDongari.Combat;

namespace MyDongari.Movement
{
    /// <summary>
    /// 스러스터 런지 돌진 중. 이동 입력은 무시하고 ThrusterLunge가 정한 방향으로만 나간다.
    /// </summary>
    public class LungeState : MechState
    {
        private ThrusterLunge _lunge;

        public LungeState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            _lunge = StateMachine.GetComponent<ThrusterLunge>();
            if (_lunge == null)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }
            _lunge.BeginDash();
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            if (_lunge == null || _lunge.TickDash())
            {
                StateMachine.ChangeState(StateMachine.IdleState);
            }
        }

        public override void Exit()
        {
            if (_lunge != null) _lunge.EndDash();
            StateMachine.MechController.SetBaselineDamping(2f);
        }
    }
}
