using UnityEngine;
using MyDongari.Input;

namespace MyDongari.Movement
{
    public class MechController : MonoBehaviour
    {
        [Header("이동 수치")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float sprintSpeed = 12f;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float SprintSpeed => sprintSpeed;

        public Rigidbody Rb { get; private set; }
        public InputReader InputReader { get; private set; }
        public MechStateMachine StateMachine { get; private set; }

        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();
        }
    }
}
