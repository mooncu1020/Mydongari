using MyDongari.Combat;
using MyDongari.Camera;
using MyDongari.Input;
using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    public class MechController : MonoBehaviour
    {
        [Header("이동 수치")]
        [SerializeField] private float walkSpeed = 10f;
        [SerializeField] private float runSpeed = 20f;
        [SerializeField] private float sprintSpeed = 35f;

        [Header("최대 속도")]
        [SerializeField] private float maxWalkSpeed = 5f;
        [SerializeField] private float maxRunSpeed = 10f;
        [SerializeField] private float maxSprintSpeed = 20f;

        [Header("회전 수치")]
        [SerializeField] private float rotationSpeed = 15f;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float SprintSpeed => sprintSpeed;
        public float MaxWalkSpeed => maxWalkSpeed;
        public float MaxRunSpeed => maxRunSpeed;
        public float MaxSprintSpeed => maxSprintSpeed;
        public float RotationSpeed => rotationSpeed;

        public Rigidbody Rb { get; private set; }
        public InputReader InputReader { get; private set; }
        public MechStateMachine StateMachine { get; private set; }

        

        [Header("카메라")]
        [SerializeField] private BoostGauge boostGauge;
        public BoostGauge BoostGauge => boostGauge;

        [SerializeField] private MeleeSystem meleeSystem;
        public MeleeSystem MeleeSystem => meleeSystem;

        [SerializeField] private Transform cameraTransform;
        [SerializeField] private MechCamera mechCamera;
        public MechCamera MechCamera => mechCamera;
        public Transform CameraTransform => cameraTransform;
        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();
        }

        public void ClampVelocity(float maxSpeed)
        {
            Vector3 flatVel = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
            if (flatVel.magnitude > maxSpeed)
            {
                Vector3 clamped = flatVel.normalized * maxSpeed;
                Rb.linearVelocity = new Vector3(clamped.x, Rb.linearVelocity.y, clamped.z);
            }
        }

        public void RotateTowards(Vector3 direction)
        {
            if (direction == Vector3.zero) return;
            Quaternion targetRot = Quaternion.LookRotation(direction, Vector3.up);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
        }

        public void RotateToCamera()
        {
            if (CameraTransform == null) return;
            Vector3 camForward = CameraTransform.forward;
            camForward.y = 0f;
            if (camForward == Vector3.zero) return;
            Quaternion targetRot = Quaternion.LookRotation(camForward);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
        }
    }
}