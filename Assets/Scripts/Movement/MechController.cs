using MyDongari.Combat;
using MyDongari.Camera;
using MyDongari.Input;
using MyDongari.Core;
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
        public ExternalControlLock ControlLock { get; private set; }
        public bool IsExternallyControlled => ControlLock != null && ControlLock.IsLocked;

        private float _defaultDamping;

        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();

            ControlLock = GetComponent<ExternalControlLock>();
            if (ControlLock == null) ControlLock = gameObject.AddComponent<ExternalControlLock>();

            _defaultDamping = Rb.linearDamping;
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

        [Header("무게감 (관성 보정)")]
        [Tooltip("0 = 관성 없음(현재 상태), 1 = 방향 반대로 꺾을 때 추진력 최대 90%까지 깎임")]
        [SerializeField] private float turnResistance = 0.6f;
        [Tooltip("정지 상태에서 출발할 때 목표 가속도까지 도달하는 데 걸리는 시간(초). 0이면 즉시 풀파워.")]
        [SerializeField] private float spinUpTime = 0.25f;

        private float _spinUpTimer = 0f;

        /// <summary>
        /// 관성/무게감을 보정해서 힘을 주는 이동 함수.
        /// 기존 AddForce + ClampVelocity 두 줄을 이 함수 한 줄로 대체해서 쓴다.
        /// </summary>
        public void ApplyMoveForce(Vector3 moveDir, float accel, float maxSpeed)
        {
            if (IsExternallyControlled) return; // 앵커클로 등에 붙잡혀 있으면 자기 힘 넣지 않고 양보

            if (moveDir == Vector3.zero)
            {
                _spinUpTimer = 0f;
                return;
            }

            // 1) 방향 전환 저항: 현재 속도 방향과 입력 방향이 어긋날수록 힘을 깎는다
            //    (기존 관성이 남아있는 상태에서 억지로 방향을 트는 걸 표현)
            Vector3 flatVel = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z);
            float alignment = flatVel.sqrMagnitude > 0.25f
                ? Vector3.Dot(flatVel.normalized, moveDir)
                : 1f; // 거의 정지 상태면 저항 없음
            float turnPenalty = Mathf.Lerp(1f, 1f - turnResistance, (1f - alignment) * 0.5f);

            // 2) 스핀업: 정지→출발 순간에는 풀파워가 아니라 점점 힘이 붙는 느낌
            _spinUpTimer = Mathf.Min(_spinUpTimer + Time.fixedDeltaTime, spinUpTime);
            float spinUpRatio = spinUpTime > 0f ? _spinUpTimer / spinUpTime : 1f;

            Rb.AddForce(moveDir * accel * turnPenalty * spinUpRatio, ForceMode.Acceleration);
            ClampVelocity(maxSpeed);
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

        // ---- Rigidbody 댐핑 소유권 조정 ----
        // Slide/SideStep(진행 중 감속 램프)과 Guard(방어 자세)가 각자 rb.linearDamping을
        // 직접 덮어쓰면 같은 프레임에 겹칠 때 실행 순서에 따라 결과가 달라진다.
        // 그래서 직접 대입 대신 매 프레임 "이 값으로 써달라"고 요청만 하게 하고,
        // 여기서 우선순위가 제일 높은 요청 하나만 실제로 적용한다.
        // 요청은 매 프레임 다시 해야 유지된다 (안 하면 자동으로 기본값으로 복귀).
        private float? _requestedDamping;
        private int _requestedDampingPriority = int.MinValue;

        public void RequestDamping(float value, int priority)
        {
            if (priority >= _requestedDampingPriority)
            {
                _requestedDamping = value;
                _requestedDampingPriority = priority;
            }
        }

        /// <summary>
        /// 요청이 하나도 없는 프레임에 기본으로 돌아갈 댐핑 값 자체를 바꾼다.
        /// (Slide/SideStep이 끝난 뒤 "일반 이동 시 댐핑 2"로 유지되던 기존 동작을 보존하기 위함)
        /// </summary>
        public void SetBaselineDamping(float value)
        {
            _defaultDamping = value;
        }

        private void LateUpdate()
        {
            Rb.linearDamping = _requestedDamping ?? _defaultDamping;
            _requestedDamping = null;
            _requestedDampingPriority = int.MinValue;
        }
    }
}