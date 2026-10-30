using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 대시 끝 (V3.1 "대시 상세" 끝 단계): 분사가 끊기고 dashStopDuration(약 0.2초) 동안 짧게 미끄러지며 감속하다 일어선다.
    /// 그동안 공격(WeaponSystem)·대시는 안 나가고, 이동은 된다 — 속도가 스틱이 원하는 달리기 속도 쪽으로
    /// "대시 속도 / 끝 시간" 감속도로 다가간다(스틱 중립이면 0까지 미끄러져 섬).
    /// </summary>
    public class DashStopState : MechState
    {
        private float _timer;

        public DashStopState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            _timer = 0f;
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.CheckFall()) return;

            _timer += Time.deltaTime;
            if (_timer >= StateMachine.MechController.Tuning.Movement.dashStopDuration)
            {
                StateMachine.ChangeState(StateMachine.InputReader.IsMoveInputActive
                    ? (MechState)StateMachine.RunState
                    : StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            MechController controller = StateMachine.MechController;
            var m = controller.Tuning.Movement;
            Vector3 target = controller.ComputeRunVelocity(StateMachine.InputReader.MoveInput);
            Vector3 velocity = controller.Rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float decel = m.dashSpeed / Mathf.Max(0.01f, m.dashStopDuration);
            controller.SetHorizontalVelocity(Vector3.MoveTowards(horizontal, target, decel * Time.fixedDeltaTime));
        }

        public override void Exit()
        {
        }
    }
}
