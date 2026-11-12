using UnityEngine;

namespace MyDongari.Movement
{
    public class SideStepState : MechState
    {
        private float _sideStepTimer = 0f;
        // 시간·속도는 BernardTuning(이동)에서 매번 읽는다 (플레이 중 조정 반영)
        private float SideStepDuration => StateMachine.MechController.Tuning.Movement.sideStepDuration;
        private float SideStepSpeed => StateMachine.MechController.Tuning.Movement.sideStepSpeed;
        private Vector3 _stepDirection;

        public SideStepState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            // V7c: 사이드스텝도 부스트를 쓰지 않는다 (부스트는 런지에만)
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
                if (StateMachine.InputReader.IsMoveInputActive)
                {
                    StateMachine.ChangeState(StateMachine.RunState);
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