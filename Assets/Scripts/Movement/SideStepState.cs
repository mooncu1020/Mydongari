using UnityEngine;

namespace MyDongari.Movement
{
    public class SideStepState : MechState
    {
        private float _sideStepTimer = 0f;
        private const float SideStepDuration = 0.3f;
        private const float SideStepSpeed = 20f;
        private Vector3 _stepDirection;

        public SideStepState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            if (!StateMachine.MechController.BoostGauge.TryConsume(
                StateMachine.MechController.BoostGauge.GetSideStepCost()))
            {
                StateMachine.ChangeState(StateMachine.WalkState);
                return;
            }

            _sideStepTimer = 0f;

            _stepDirection = StateMachine.InputReader.SideStepDirection == 1
                ? StateMachine.MechController.transform.right
                : -StateMachine.MechController.transform.right;

            StateMachine.MechController.Rb.linearVelocity = new Vector3(
                _stepDirection.x * SideStepSpeed,
                StateMachine.MechController.Rb.linearVelocity.y,
                _stepDirection.z * SideStepSpeed
            );
        }

        public override void Update()
        {
            _sideStepTimer += Time.deltaTime;

            if (_sideStepTimer >= SideStepDuration)
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
            float t = _sideStepTimer / SideStepDuration;
            StateMachine.MechController.RequestDamping(Mathf.Lerp(0f, 8f, t), priority: 5);
        }

        public override void Exit()
        {
            StateMachine.MechController.SetBaselineDamping(2f);
        }
    }
}