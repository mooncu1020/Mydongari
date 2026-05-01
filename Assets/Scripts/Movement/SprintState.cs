using UnityEngine;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Sprint 진입");
        }

        public override void Update()
        {
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