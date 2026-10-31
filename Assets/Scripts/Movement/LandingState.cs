using System;
using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 착지 경직 (V5): 점프·공중 대시·공중 피격 뒤 착지하면 landingLag(0.2초), 부스트 0%로 착지하면 exhaustedLandingLag(0.8초).
    /// 그동안 이동·공격·대시·점프·가드 불가 — 상대가 노릴 틈이다. 발밑 표시는 CombatFeedback이 OnLanded로 띄운다.
    /// </summary>
    public class LandingState : MechState
    {
        private float _timer;
        private float _duration;

        /// <summary>착지했을 때 (경직 시간, 부스트 0%였는지). 그냥 떨어진 착지(경직 0)도 알린다 — 먼지·흔들림용.</summary>
        public event Action<float, bool> OnLanded;

        /// <summary>남은 경직 시간(초).</summary>
        public float Remaining => Mathf.Max(0f, _duration - _timer);

        public LandingState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        /// <summary>MechStateMachine.Land가 Enter 직전에 경직 시간을 정한다.</summary>
        public void Prepare(float duration, bool exhausted)
        {
            _duration = duration;
            _timer = 0f;
            OnLanded?.Invoke(duration, exhausted);
        }

        public override void Enter()
        {
            _timer = 0f;
            StateMachine.MechController.SetHorizontalVelocity(Vector3.zero);
        }

        public override void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= _duration)
            {
                StateMachine.ChangeState(StateMachine.InputReader.IsMoveInputActive
                    ? (MechState)StateMachine.RunState
                    : StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            // 경직 중엔 제자리 (넉백 유지 중이면 SetHorizontalVelocity가 양보한다)
            StateMachine.MechController.SetHorizontalVelocity(Vector3.zero);
        }

        public override void Exit()
        {
        }
    }
}
