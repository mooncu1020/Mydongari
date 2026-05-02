using UnityEngine;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        public bool IsTackleCharged => _tackleChargeTimer >= 1.5f;
        public float TackleChargePower => Mathf.Clamp01((_tackleChargeTimer - 1.5f) / 1.5f);

        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Sprint 진입");
            _tackleChargeTimer = 0f;
        }

        public override void Update()
        {
            _tackleChargeTimer += Time.deltaTime;

            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.25f ||
                !StateMachine.InputReader.SprintInput)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
            }
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            Debug.Log("Sprint 탈출");
        }
    }
}