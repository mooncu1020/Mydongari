using UnityEngine;

namespace MyDongari.Movement
{
    public class BrakeState : MechState
    {
        private float _brakeTimer = 0f;
        private float BrakeDuration => StateMachine.MechController.Tuning.Movement.brakeDuration;
        private float SlideWindowDuration => StateMachine.MechController.Tuning.Movement.brakeSlideWindow;
        private bool _slideWindowOpen = true;

        public BrakeState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            _brakeTimer = 0f;
            _slideWindowOpen = true;
        }

        public override void Update()
        {
            _brakeTimer += Time.deltaTime;

            // 버튼 슬라이딩(키보드 Ctrl) 경로. 패드 두 스틱 슬라이딩은 제동 전에 SprintState가 대기하며 받는다
            if (_slideWindowOpen)
            {
                if (_brakeTimer <= SlideWindowDuration &&
                    StateMachine.InputReader.SlideInput)
                {
                    StateMachine.ChangeState(StateMachine.SlideState);
                    return;
                }

                if (_brakeTimer > SlideWindowDuration)
                {
                    _slideWindowOpen = false;
                }
            }

            // 패드는 스틱을 튕기려면 중립을 거쳐야 해서 질주가 먼저 Brake로 넘어온다.
            // 그래서 질주 중 초신지선회·사이드스텝을 Brake에서도 받아 준다.
            if (StateMachine.InputReader.QuickTurnInput)
            {
                StateMachine.ChangeState(StateMachine.QuickTurnState);
                return;
            }

            if (StateMachine.InputReader.SideStepDirection != 0)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            if (_brakeTimer >= BrakeDuration)
            {
                if (StateMachine.InputReader.IsMoveInputActive)
                {
                    StateMachine.ChangeState(StateMachine.WalkState);
                }
                else
                {
                    StateMachine.ChangeState(StateMachine.IdleState);
                }
            }
        }

        public override void FixedUpdate()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.MechController.Rb.linearVelocity = Vector3.zero;
            }
        }

        public override void Exit()
        {
        }
    }
}