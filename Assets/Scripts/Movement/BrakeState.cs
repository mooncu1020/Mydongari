using UnityEngine;

namespace MyDongari.Movement
{
    public class BrakeState : MechState
    {
        public BrakeState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Brake 진입");
        }

        public override void Update()
        {
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            Debug.Log("Brake 탈출");
        }
    }
}