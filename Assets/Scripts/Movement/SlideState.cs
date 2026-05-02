using UnityEngine;

namespace MyDongari.Movement
{
    public class SlideState : MechState
    {
        private float _slideTimer = 0f;
        private const float SlideDuration = 0.8f;
        private const float SlideSpeed = 25f;

        public SlideState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Slide 진입");
            _slideTimer = 0f;
            StateMachine.MechController.Rb.linearDamping = 0f;
        }

        public override void Update()
        {
            _slideTimer += Time.deltaTime;

            if (_slideTimer >= SlideDuration)
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
            if (_slideTimer < 0.1f)
            {
                Vector3 slideDir = StateMachine.LastMoveDirection;
                StateMachine.MechController.Rb.linearVelocity = new Vector3(
                    slideDir.x * SlideSpeed,
                    StateMachine.MechController.Rb.linearVelocity.y,
                    slideDir.z * SlideSpeed
                );
            }

            float t = _slideTimer / SlideDuration;
            float damping = Mathf.Lerp(0f, 8f, t);
            StateMachine.MechController.Rb.linearDamping = damping;
        }

        public override void Exit()
        {
            Debug.Log("Slide 탈출");
            StateMachine.MechController.Rb.linearDamping = 2f;
        }
    }
}