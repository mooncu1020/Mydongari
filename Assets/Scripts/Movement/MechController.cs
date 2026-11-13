using MyDongari.Combat;
using MyDongari.Camera;
using MyDongari.Input;
using MyDongari.Core;
using UnityEngine;
using MyDongari.Mech;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    /// <summary>
    /// 기체의 참조 모음 + 이동기와 전투 사이의 공용 규칙: 넉백 유지(이동 로직이 속도를 덮어쓰지 않는 시간), 몸 충돌 반동,
    /// 리지드바디 감속 요청(외부 속도 감속이 linearDamping을 읽는다). 이동 자체는 MechMotor(버추얼온식 이동기)가 한다.
    /// (V6.1까지의 리지드바디 이동·하드락 몸통 회전·접지 검사·대시 벽 충돌은 CL3에서 지웠다.)
    /// </summary>
    public class MechController : MonoBehaviour
    {
        private TuningRef _tuning;
        /// <summary>이 기체의 수치 에셋. 상태들도 이걸 통해 읽는다.</summary>
        public BernardTuning Tuning => _tuning.Get(this);
        private BernardTuning.MovementValues Move => Tuning.Movement;

        public Rigidbody Rb { get; private set; }
        public InputReader InputReader { get; private set; }
        public MechStateMachine StateMachine { get; private set; }

        [Header("카메라")]
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
            // 물리 스텝마다 위치가 계단처럼 바뀌면 카메라·락온·외형이 떨린다 (J3). 이동기도 켜질 때 다시 켠다
            if (Rb != null) Rb.interpolation = RigidbodyInterpolation.Interpolate;
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();

            ControlLock = GetComponent<ExternalControlLock>();
            if (ControlLock == null) ControlLock = gameObject.AddComponent<ExternalControlLock>();
            Identity = MechIdentity.GetOrAdd(gameObject);
            LockOn = GetComponent<LockOnSystem>();

            _defaultDamping = Rb.linearDamping;

            // 이동기: 항상 붙여 둔다 (입력·상태 머신이 있는 기체에서 켜진다)
            Motor = GetComponent<MechMotor>();
            if (Motor == null) Motor = gameObject.AddComponent<MechMotor>();
        }

        /// <summary>이동기.</summary>
        public MechMotor Motor { get; private set; }
        /// <summary>지금 속도(m/s). 이동기가 실제로 움직인 속도 (키네마틱 리지드바디의 속도는 쓸 수 없다).</summary>
        public Vector3 Velocity => MotionBridge.GetVelocity(Rb);

        // 넉백 뒤 이동 로직이 속도를 덮어쓰지 않는 남은 시간(초). 이 동안 이동기는 외부 속도로 움직인다
        private float _moveHoldTimer;

        /// <summary>넉백 유지 중이라 이동 로직이 속도를 건드리면 안 되는지.</summary>
        public bool IsMoveHeld => _moveHoldTimer > 0f;

        /// <summary>
        /// 넉백을 받았을 때 부른다 (MeleeSystem). knockbackMoveHold초 동안 이동 상태가 속도를 덮어쓰지 않는다.
        /// 대시 중이었으면 대시를 끝낸다 — 안 끝내면 유지 시간이 지난 뒤 맞은 자리에서 다시 돌진한다.
        /// </summary>
        public void HoldMoveControl()
        {
            _moveHoldTimer = Mathf.Max(_moveHoldTimer, Move.knockbackMoveHold);
            if (StateMachine != null && StateMachine.IsDashing) StateMachine.ActiveDash.EndByKnockback();
        }

        /// <summary>
        /// 수평 속도를 그대로 지정. 외부 제어·넉백 유지 중엔 양보.
        /// 이동 상태는 틱마다 속력을 직접 넣으므로 여기선 외부 속도 쪽만 (착지 경직의 "제자리" 등은 원래 0).
        /// </summary>
        public void SetHorizontalVelocity(Vector3 horizontal)
        {
            if (IsExternallyControlled || IsMoveHeld) return;
            if (Motor.IsActive && Motor.IsExternalMode) MotionBridge.SetHorizontalVelocity(Rb, horizontal);
        }

        // ---- Rigidbody 댐핑 소유권 조정 ----
        // 런지(돌진·박기)·넉다운과 Guard(방어 자세)가 각자 rb.linearDamping을
        // 직접 덮어쓰면 같은 프레임에 겹칠 때 실행 순서에 따라 결과가 달라진다.
        // 그래서 직접 대입 대신 매 프레임 "이 값으로 써달라"고 요청만 하게 하고,
        // 여기서 우선순위가 제일 높은 요청 하나만 실제로 적용한다.
        // 요청은 매 프레임 다시 해야 유지된다 (안 하면 자동으로 기본값으로 복귀).
        // 이동기의 외부 속도(넉백·런지·넉다운) 감속이 이 값을 읽는다.
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
        /// (런지·넉다운·가드가 끝난 뒤 "일반 이동 시 댐핑 2"로 유지되던 기존 동작을 보존하기 위함)
        /// </summary>
        public void SetBaselineDamping(float value)
        {
            _defaultDamping = value;
        }

        private void FixedUpdate()
        {
            if (_moveHoldTimer > 0f) _moveHoldTimer -= Time.fixedDeltaTime;
            // 지면은 이동기가 직접 본다
            if (Motor.IsActive) IsGrounded = Motor.IsGrounded;
        }

        /// <summary>몸 충돌 반동: 대시를 끝내고 direction 쪽으로 bumpSpeed만큼 밀린다(넉백처럼 잠깐 이동 입력을 막음).</summary>
        public void ApplyBump(Vector3 direction)
        {
            if (IsExternallyControlled) return;
            if (StateMachine != null && StateMachine.IsDashing) StateMachine.ActiveDash.EndByBump();
            Vector3 push = direction * Move.bumpSpeed;
            // 넉백 유지를 먼저 걸어야 외부 속도가 "걷던 속도 + 반동"이 아니라 반동만으로 시작한다
            MotionBridge.AddImpulse(Rb, new Vector3(push.x, 0f, push.z) * Rb.mass);
            HoldMoveControl();
        }

        /// <summary>라운드 시작: 넉백 유지 시간 초기화, 땅에 있는 것으로.</summary>
        public void ResetForRound()
        {
            _moveHoldTimer = 0f;
            IsGrounded = true;
            if (Motor.IsActive) Motor.ResetForRound();
        }

        /// <summary>발밑에 설 수 있는 땅이 있는지 (이동기의 지면 판정). 물리 스텝마다 갱신.</summary>
        public bool IsGrounded { get; private set; } = true;

        private void LateUpdate()
        {
            Rb.linearDamping = _requestedDamping ?? _defaultDamping;
            _requestedDamping = null;
            _requestedDampingPriority = int.MinValue;

            if (InputTrace.ShouldLog(this, "5 MechController"))
            {
                InputTrace.Log("5 MechController", $"{gameObject.name} reader={(InputReader != null ? InputReader.gameObject.name : "없음")} " +
                    $"move={(InputReader != null ? InputReader.MoveInput.ToString() : "-")} vel={Velocity} " +
                    $"external={IsExternallyControlled} kinematic={Rb.isKinematic}");
            }
        }
    }
}
