using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 대시 끝 단계 (V6b, 템진 기준): 분사가 끊기고 dashStopDuration(0.27초) 동안 들어온 속도를 0까지 선형으로 줄이며 미끄러진다.
    /// 스틱은 받지 않는다(정해진 미끄러짐). 대시는 안 나가고, △ 점프는 내내, 공격은 들어오고 dashStopAttackDelay(0.05초) 뒤부터 받는다
    /// (V6.1, 원본 분석). 미끄러지던 속도를 안고 점프한다. 끝난 뒤 경직은 없다.
    /// 공중 대시의 끝 단계(PrepareAirborne)는 공중에서 같은 감속을 하며 중력을 받고, 끝나면 떨어진다(AirState, 착지 경직 있음).
    /// </summary>
    public class DashStopState : MechState
    {
        private float _timer;
        // 들어온 순간 수평 속력(m/s). 감속도 = 이 값 / 끝 시간 → 정확히 끝 시간에 0
        private float _decel;
        private bool _airborne;
        private bool _pendingAirborne;

        /// <summary>공중 대시의 끝 단계인지.</summary>
        public bool IsAirborne => _airborne;

        /// <summary>공격을 받는지 (땅 끝 단계에서 dashStopAttackDelay가 지났을 때). 공중 끝 단계는 공중이라 공격 없음.</summary>
        public bool AllowsAttack => !_airborne && _timer >= StateMachine.MechController.Tuning.Movement.dashStopAttackDelay;

        public DashStopState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        /// <summary>다음 Enter를 공중 끝 단계로 (DashState가 공중 대시를 끝낼 때).</summary>
        public void PrepareAirborne(bool airborne)
        {
            _pendingAirborne = airborne;
        }

        public override void Enter()
        {
            _timer = 0f;
            _airborne = _pendingAirborne;
            _pendingAirborne = false;
            Vector3 v = StateMachine.MechController.Rb.linearVelocity;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;
            _decel = speed / Mathf.Max(0.01f, StateMachine.MechController.Tuning.Movement.dashStopDuration);
        }

        public override void Update()
        {
            MechController controller = StateMachine.MechController;
            var m = controller.Tuning.Movement;

            if (_airborne)
            {
                // 공중 끝 단계: 땅에 닿으면 착지 경직, 스턴(공중 피격은 MechStateMachine이 따로 떨어뜨림)이면 바로 떨어짐
                if (controller.IsGrounded && controller.Rb.linearVelocity.y <= 0.5f)
                {
                    StateMachine.Land(true);
                    return;
                }
                _timer += Time.deltaTime;
                if (StateMachine.IsStunned || _timer >= m.dashStopDuration)
                {
                    StateMachine.AirState.PrepareFallWithLandingLag();
                    StateMachine.ChangeState(StateMachine.AirState);
                }
                return;
            }

            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.CheckFall()) return;
            // 끝 단계 내내 △ 점프를 받는다 (V6.1)
            if (StateMachine.TryStartJump()) return;

            _timer += Time.deltaTime;
            if (_timer >= m.dashStopDuration)
            {
                StateMachine.ChangeState(StateMachine.InputReader.IsMoveInputActive
                    ? (MechState)StateMachine.RunState
                    : StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            MechController controller = StateMachine.MechController;
            if (_airborne) controller.ApplyAirGravity(controller.Tuning.Movement.jumpGravity);
            Vector3 velocity = controller.Rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            controller.SetHorizontalVelocity(Vector3.MoveTowards(horizontal, Vector3.zero, _decel * Time.fixedDeltaTime));
        }

        public override void Exit()
        {
            _airborne = false;
        }
    }
}
