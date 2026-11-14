using System;
using UnityEngine;
using MyDongari.Bridge;

namespace MyDongari.Game.States
{
    /// <summary>
    /// 착지 경직 (V5): 점프·공중 대시·공중 피격 뒤 착지하면 착지 단계(12f, 틱으로 센다). V7c: 부스트 0% 착지의 긴 경직은 없앴다.
    /// 그동안 이동·공격·대시·점프·가드 불가 — 상대가 노릴 틈이다. 발밑 표시는 CombatFeedback이 OnLanded로 띄운다.
    /// </summary>
    public class LandingState : GameState
    {
        private int _timer;
        private int _duration;

        /// <summary>착지했을 때 (경직 시간, 초 — 연출·로그용). 그냥 떨어진 착지(경직 0)도 알린다 — 먼지·흔들림용.</summary>
        public event Action<float> OnLanded;

        /// <summary>남은 경직(틱).</summary>
        public int RemainingFrames => Mathf.Max(0, _duration - _timer);

        public LandingState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        /// <summary>MechStateMachine.Land가 Enter 직전에 경직 길이(틱)를 정한다.</summary>
        public void Prepare(int frames)
        {
            _duration = frames;
            _timer = 0;
            OnLanded?.Invoke(frames / 60f);
        }

        public override void Enter()
        {
            _timer = 0;
            StateMachine.MechController.SetHorizontalVelocity(Vector3.zero);
        }

        public override void Update()
        {
            // 이동기가 틱마다 한 번 부른다
            _timer++;
            if (_timer >= _duration)
            {
                StateMachine.ToStandOrWalk();
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
