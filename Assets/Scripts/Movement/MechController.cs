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
        // 이동 수치는 BernardTuning(이동)으로 옮겼다. 아래 숨긴 필드는 씬에 저장된 예전 값을
        // "Copy P1 Values To Tuning" 메뉴가 읽어 가기 위해서만 남겨 둔다. 런타임 코드는 읽지 않는다.
#pragma warning disable 0414
        [HideInInspector] [SerializeField] private float walkSpeed = 10f;
        [HideInInspector] [SerializeField] private float runSpeed = 20f;
        [HideInInspector] [SerializeField] private float sprintSpeed = 35f;
        [HideInInspector] [SerializeField] private float maxWalkSpeed = 5f;
        [HideInInspector] [SerializeField] private float maxRunSpeed = 10f;
        [HideInInspector] [SerializeField] private float maxSprintSpeed = 20f;
        [HideInInspector] [SerializeField] private float rotationSpeed = 15f;
        [HideInInspector] [SerializeField] private float hardLockTurnSpeed = 10f;
        [HideInInspector] [SerializeField] private float turnResistance = 0.6f;
        [HideInInspector] [SerializeField] private float spinUpTime = 0.25f;
#pragma warning restore 0414

        private TuningRef _tuning;
        /// <summary>이 기체의 수치 에셋. 상태들도 이걸 통해 읽는다.</summary>
        public BernardTuning Tuning => _tuning.Get(this);
        private BernardTuning.MovementValues Move => Tuning.Movement;

        public float WalkSpeed => Move.walkSpeed;
        public float RunSpeed => Move.runSpeed;
        public float SprintSpeed => Move.sprintSpeed;
        public float MaxWalkSpeed => Move.maxWalkSpeed;
        public float MaxRunSpeed => Move.maxRunSpeed;
        public float MaxSprintSpeed => Move.maxSprintSpeed;
        public float RotationSpeed => Move.rotationSpeed;

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
        public MechIdentity Identity { get; private set; }
        public LockOnSystem LockOn { get; private set; }
        public bool IsExternallyControlled => ControlLock != null && ControlLock.IsLocked;

        private float _defaultDamping;

        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();

            ControlLock = GetComponent<ExternalControlLock>();
            if (ControlLock == null) ControlLock = gameObject.AddComponent<ExternalControlLock>();
            Identity = MechIdentity.GetOrAdd(gameObject);
            LockOn = GetComponent<LockOnSystem>();

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
            float turnPenalty = Mathf.Lerp(1f, 1f - Move.turnResistance, (1f - alignment) * 0.5f);

            // 2) 스핀업: 정지→출발 순간에는 풀파워가 아니라 점점 힘이 붙는 느낌
            float spinUp = Move.spinUpTime;
            _spinUpTimer = Mathf.Min(_spinUpTimer + Time.fixedDeltaTime, spinUp);
            float spinUpRatio = spinUp > 0f ? _spinUpTimer / spinUp : 1f;

            Rb.AddForce(moveDir * accel * turnPenalty * spinUpRatio, ForceMode.Acceleration);
            ClampVelocity(maxSpeed);
        }

        public void RotateTowards(Vector3 direction)
        {
            if (direction == Vector3.zero) return;
            Quaternion targetRot = Quaternion.LookRotation(direction, Vector3.up);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, Move.rotationSpeed * Time.fixedDeltaTime));
        }

        /// <summary>
        /// 지금 하드 락온 몸통 회전이 기체 방향을 맡고 있는지.
        /// 이때는 상태들의 RotateToCamera가 같은 물리 스텝에 회전을 덮어쓰지 않게 양보한다.
        /// </summary>
        public bool IsHardLockFacing =>
            LockOn != null && LockOn.IsHardLocked && LockOn.Target != null &&
            StateMachine != null && StateMachine.AllowsHardLockFacing &&
            !IsExternallyControlled;

        /// <summary>하드 락온 중 몸통을 상대 쪽으로 수평 회전. MechStateMachine이 매 물리 스텝 부른다.</summary>
        public void ApplyHardLockFacing()
        {
            if (!IsHardLockFacing) return;

            Vector3 toTarget = LockOn.Target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, Move.hardLockTurnSpeed * Time.fixedDeltaTime));
        }

        public void RotateToCamera()
        {
            if (IsHardLockFacing) return;
            if (CameraTransform == null) return;
            Vector3 camForward = CameraTransform.forward;
            camForward.y = 0f;
            if (camForward == Vector3.zero) return;
            Quaternion targetRot = Quaternion.LookRotation(camForward);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, Move.rotationSpeed * Time.fixedDeltaTime));
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