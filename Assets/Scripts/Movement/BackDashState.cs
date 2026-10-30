using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 백대시 (V3 "이동"): ○ + L스틱 뒤. 뒤로 짧게 튀어 거리를 벌린다. 부스트는 시작할 때 한 번(backDashCost).
    /// 하드락 밖은 몸 뒤쪽, 하드락 안은 상대 반대 방향 — 하드락(근접 8m/s)에서 빠져나가는 수단이다.
    /// 거리·시간은 튜닝(backDashDistance, backDashDuration). 몸은 돌리지 않는다(상대를 본 채 물러남).
    /// </summary>
    public class BackDashState : MechState
    {
        private float _timer;
        private Vector3 _direction;

        public BackDashState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            MechController controller = StateMachine.MechController;
            if (!controller.BoostGauge.TryConsume(controller.BoostGauge.GetBackDashCost()))
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            _timer = 0f;
            _direction = controller.TryGetHardLockForward(out Vector3 toOpponent)
                ? -toOpponent
                : controller.BodyRelativeDirection(Vector2.down);
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            _timer += Time.deltaTime;
            if (_timer >= StateMachine.MechController.Tuning.Movement.backDashDuration)
            {
                StateMachine.ChangeState(StateMachine.InputReader.IsMoveInputActive
                    ? (MechState)StateMachine.RunState
                    : StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            var m = StateMachine.MechController.Tuning.Movement;
            float speed = m.backDashDuration > 0f ? m.backDashDistance / m.backDashDuration : 0f;
            StateMachine.MechController.SetMoveVelocity(_direction, speed);
        }

        public override void Exit()
        {
        }
    }
}
