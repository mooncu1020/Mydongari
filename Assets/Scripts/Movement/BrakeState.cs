using UnityEngine;

namespace MyDongari.Movement
{
    public class BrakeState : MechState
    {
        private float _brakeTimer = 0f;
        private const float BrakeDuration = 1.0f;
        private const float SlideWindowDuration = 0.5f;
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

            if (_brakeTimer >= BrakeDuration)
            {
                if (StateMachine.InputReader.MoveInput.sqrMagnitude >= 0.64f)
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