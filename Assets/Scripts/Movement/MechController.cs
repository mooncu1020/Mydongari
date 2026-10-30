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
        private TuningRef _tuning;
        /// <summary>이 기체의 수치 에셋. 상태들도 이걸 통해 읽는다.</summary>
        public BernardTuning Tuning => _tuning.Get(this);
        private BernardTuning.MovementValues Move => Tuning.Movement;

        public float MaxRunSpeed => Move.maxRunSpeed;
        public float CloseMoveSpeed => Move.closeMoveSpeed;
        public float DashSpeed => Move.dashSpeed;

        public Rigidbody Rb { get; private set; }
        public InputReader InputReader { get; private set; }
        public MechStateMachine StateMachine { get; private set; }

        [Header("물리 보간")]
        [Tooltip("켜면 시작할 때 Rigidbody Interpolate를 강제로 켠다. 끄면(None) 물리 스텝(50Hz)마다 위치가 계단처럼 바뀌어 " +
                 "화면 분할처럼 프레임이 물리 주기에 가까울 때 카메라·락온·외형이 떨린다 (J3)")]
        [SerializeField] private bool forceInterpolation = true;

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
        private MeleeSystem _melee;

        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            if (forceInterpolation && Rb != null) Rb.interpolation = RigidbodyInterpolation.Interpolate;
            InputReader = GetComponent<InputReader>();
            StateMachine = GetComponent<MechStateMachine>();

            ControlLock = GetComponent<ExternalControlLock>();
            if (ControlLock == null) ControlLock = gameObject.AddComponent<ExternalControlLock>();
            Identity = MechIdentity.GetOrAdd(gameObject);
            LockOn = GetComponent<LockOnSystem>();
            _melee = GetComponent<MeleeSystem>();

            _defaultDamping = Rb.linearDamping;
        }

        // 넉백 뒤 이동 로직이 속도를 덮어쓰지 않는 남은 시간(초). 이동이 속도를 직접 넣으므로, 안 막으면 넉백 임펄스가 다음 물리 스텝에 지워진다
        private float _moveHoldTimer;

        /// <summary>넉백 유지 중이라 이동 로직이 속도를 건드리면 안 되는지.</summary>
        public bool IsMoveHeld => _moveHoldTimer > 0f;

        /// <summary>넉백을 받았을 때 부른다 (MeleeSystem). knockbackMoveHold초 동안 달리기·정지가 속도를 덮어쓰지 않는다.</summary>
        public void HoldMoveControl()
        {
            _moveHoldTimer = Mathf.Max(_moveHoldTimer, Move.knockbackMoveHold);
        }

        /// <summary>
        /// V3 이동의 핵심: 가속도 없이 수평 속도를 바로 direction × speed로 (세로 속도는 그대로).
        /// 콤보 중·쓰리 회복 중엔 느리게 (N2). 외부 제어(앵커 클로 등)·넉백 유지 중엔 양보.
        /// </summary>
        public void SetMoveVelocity(Vector3 direction, float speed)
        {
            if (IsExternallyControlled || IsMoveHeld) return;
            float speedScale = _melee != null ? _melee.MoveSpeedScale : 1f;
            Vector3 horizontal = direction * (speed * speedScale);
            Rb.linearVelocity = new Vector3(horizontal.x, Rb.linearVelocity.y, horizontal.z);
        }

        /// <summary>정지: 수평 속도 0 (V3: 미끄러지는 감속 없음). 스턴 중엔 넉백·밀림이 남게 건드리지 않는다.</summary>
        public void StopHorizontal()
        {
            if (_melee != null && _melee.IsStunned) return;
            SetMoveVelocity(Vector3.zero, 0f);
        }

        /// <summary>
        /// 항상 달리기 (V3). 스틱 기울기(데드존 ~ 끝을 0 ~ 1로 다시 맞춤)만큼 즉시 그 속도.
        /// 하드락 밖: 몸 기준, 최대 maxRunSpeed. 하드락 안: 상대 기준(위 = 상대 쪽), 최대 closeMoveSpeed —
        /// 20m/s면 6m를 0.3초에 지나쳐 거리 조절이 안 되므로 거리로 저절로 바뀐다.
        /// </summary>
        public void ApplyRunVelocity(Vector2 input)
        {
            float deadzone = InputReader != null ? InputReader.MoveDeadzone : 0f;
            float tilt = Mathf.InverseLerp(deadzone, 1f, Mathf.Clamp01(input.magnitude));
            Vector2 stick = input.sqrMagnitude > 0.0001f ? input.normalized : Vector2.zero;

            Vector3 direction;
            float maxSpeed;
            if (TryGetHardLockForward(out Vector3 toOpponent))
            {
                Vector3 right = new Vector3(toOpponent.z, 0f, -toOpponent.x);
                direction = (toOpponent * stick.y + right * stick.x).normalized;
                maxSpeed = Move.closeMoveSpeed;
            }
            else
            {
                direction = BodyRelativeDirection(stick);
                maxSpeed = Move.maxRunSpeed;
            }
            SetMoveVelocity(direction, maxSpeed * tilt);
        }

        /// <summary>하드락 중이면 상대 쪽 수평 방향(크기 1). 물리 위치로 잰다.</summary>
        public bool TryGetHardLockForward(out Vector3 forward)
        {
            forward = Vector3.zero;
            if (LockOn == null || !LockOn.IsHardLocked || LockOn.Target == null) return false;
            Rigidbody targetRb = LockOn.Target.GetComponent<Rigidbody>();
            Vector3 targetPosition = targetRb != null ? targetRb.position : LockOn.Target.position;
            forward = targetPosition - Rb.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return false;
            forward.Normalize();
            return true;
        }

        /// <summary>
        /// 지금 하드 락온 몸통 회전이 기체 방향을 맡고 있는지.
        /// 이때는 R스틱 몸 회전(ApplyTurn)을 끈다 (V3: 하드락 안에서는 R스틱 회전 꺼짐).
        /// </summary>
        public bool IsHardLockFacing =>
            LockOn != null && LockOn.IsHardLocked && LockOn.Target != null &&
            StateMachine != null && StateMachine.AllowsHardLockFacing &&
            !IsExternallyControlled;

        /// <summary>하드 락온 중 몸통을 상대 쪽으로 수평 회전. MechStateMachine이 매 물리 스텝 부른다.</summary>
        public void ApplyHardLockFacing()
        {
            if (!IsHardLockFacing) return;

            // 물리 스텝이라 보간된 트랜스폼이 아니라 두 리지드바디의 물리 위치로 방향을 잰다 (보간을 켜면 트랜스폼은 한 스텝 뒤라 J3)
            Rigidbody targetRb = LockOn.Target.GetComponent<Rigidbody>();
            Vector3 targetPosition = targetRb != null ? targetRb.position : LockOn.Target.position;
            Vector3 toTarget = targetPosition - Rb.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, Move.hardLockTurnSpeed * Time.fixedDeltaTime));
        }

        /// <summary>
        /// 몸 전체를 수평으로 degrees만큼 돌린다 (V3 R스틱 제자리 회전). MechStateMachine이 물리 스텝에서 부른다.
        /// 카메라가 몸을 따라오므로 시점을 바꾸는 방법은 이것과 하드락 몸통 회전뿐이다.
        /// </summary>
        public void ApplyTurn(float degrees)
        {
            if (Mathf.Abs(degrees) < 0.0001f || IsExternallyControlled) return;
            Rb.MoveRotation(Rb.rotation * Quaternion.Euler(0f, degrees, 0f));
        }

        /// <summary>
        /// 스틱 입력을 몸 기준 수평 방향으로 (V3: 이동 기준은 카메라가 아니라 몸). 위 = 몸 정면, 오른쪽 = 몸 오른쪽. 크기 1로 맞춘다.
        /// 물리 위치·회전(Rb)으로 재야 보간된 트랜스폼과 한 스텝 어긋나지 않는다.
        /// </summary>
        public Vector3 BodyRelativeDirection(Vector2 input)
        {
            Vector3 forward = Rb.rotation * Vector3.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            return (forward * input.y + right * input.x).normalized;
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

        private void FixedUpdate()
        {
            if (_moveHoldTimer > 0f) _moveHoldTimer -= Time.fixedDeltaTime;
        }

        /// <summary>
        /// 대시 중 공격 없이 상대 몸에 부딪히면 둘 다 멈추고 살짝 밀려난다(V3 "충돌", 피해 없음).
        /// 차징이 찬 상태로 정면에서 닿으면 예전처럼 자동 태클로 넘긴다. 상대가 대시 중이면 상대 대시도 끝난다.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (StateMachine == null || !StateMachine.IsDashing || Identity == null) return;
            if (!Identity.IsOpponentCollider(collision.collider)) return;
            DashState dash = StateMachine.DashState;
            if (dash.IsAttacking) return;
            if (dash.IsTackleCharged && _melee != null && _melee.IsOpponentInTackleArc() && dash.RequestTackle()) return;

            MechController other = Identity.Opponent != null ? Identity.Opponent.GetComponent<MechController>() : null;
            Vector3 apart = Rb.position - (other != null ? other.Rb.position : collision.transform.position);
            apart.y = 0f;
            if (apart.sqrMagnitude < 0.0001f) apart = -(Rb.rotation * Vector3.forward);
            apart.Normalize();

            ApplyBump(apart);
            if (other != null) other.ApplyBump(-apart);
        }

        /// <summary>몸 충돌 반동: 대시를 끝내고 direction 쪽으로 bumpSpeed만큼 밀린다(넉백처럼 잠깐 이동 입력을 막음).</summary>
        public void ApplyBump(Vector3 direction)
        {
            if (IsExternallyControlled) return;
            if (StateMachine != null && StateMachine.IsDashing && !StateMachine.DashState.IsAttacking) StateMachine.DashState.EndByBump();
            Vector3 push = direction * Move.bumpSpeed;
            Rb.linearVelocity = new Vector3(push.x, Rb.linearVelocity.y, push.z);
            HoldMoveControl();
        }

        /// <summary>라운드 시작: 넉백 유지 시간 초기화.</summary>
        public void ResetForRound()
        {
            _moveHoldTimer = 0f;
        }

        private void LateUpdate()
        {
            Rb.linearDamping = _requestedDamping ?? _defaultDamping;
            _requestedDamping = null;
            _requestedDampingPriority = int.MinValue;

            if (InputTrace.ShouldLog(this, "5 MechController"))
            {
                InputTrace.Log("5 MechController", $"{gameObject.name} reader={(InputReader != null ? InputReader.gameObject.name : "없음")} " +
                    $"move={(InputReader != null ? InputReader.MoveInput.ToString() : "-")} vel={Rb.linearVelocity} " +
                    $"external={IsExternallyControlled} kinematic={Rb.isKinematic}");
            }
        }
    }
}