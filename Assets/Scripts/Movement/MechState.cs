using UnityEngine;

namespace MyDongari.Movement
{
    public abstract class MechState
    {
        protected MechStateMachine StateMachine;

        public MechState(MechStateMachine stateMachine)
        {
            StateMachine = stateMachine;
        }

        public abstract void Enter();
        public abstract void Update();
        public abstract void FixedUpdate();
        public abstract void Exit();
    }
}
