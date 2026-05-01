using UnityEngine;

namespace MyDongari.Movement
{
    public class WalkState : MechState
    {
        public WalkState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            Debug.Log("Walk 진입");
        }

        public override void Update()
        {
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
            Debug.Log("Walk 탈출");
        }
    }
}
