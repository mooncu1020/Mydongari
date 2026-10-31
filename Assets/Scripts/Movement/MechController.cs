using MyDongari.Combat;
using MyDongari.Camera;
using MyDongari.Input;
using MyDongari.Core;
using UnityEngine;
using MyDongari.Mech;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    public class MechController : MonoBehaviour
    {
        private TuningRef _tuning;
        /// <summary>이 기체의 수치 에셋. 상태들도 이걸 통해 읽는다.</summary>
        public BernardTuning Tuning => _tuning.Get(this);
        private BernardTuning.MovementValues Move => Tuning.Movement;

        /// <summary>앞 걷기 최대 속도 (방향별 값 중 앞).</summary>
        public float MaxRunSpeed => Move.walkSpeed.front;
        /// <summary>대시 최고 속도 (방향별 값 중 가장 큰 값). FOV 기준.</summary>
        public float DashSpeed => Move.dashSpeed.Max;

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

            // V8 이동기: 항상 붙여 두고, BernardTuning 이동기 선택이 VirtualOn일 때만 켜진다
            Motor = GetComponent<MechMotor>();
            if (Motor == null) Motor = gameObject.AddComponent<MechMotor>();
        }

        /// <summary>V8 이동기 (이동기 선택이 Legacy면 꺼져 있음).</summary>
        public MechMotor Motor { get; private set; }
        /// <summary>V8 이동기가 이 기체를 움직이는지.</summary>
        public bool IsV8 => Motor != null && Motor.IsActive;
        /// <summary>지금 속도(m/s). V8이면 이동기가 실제로 움직인 속도 (키네마틱 리지드바디의 속도는 쓸 수 없다).</summary>
        public Vector3 Velocity => MotionBridge.GetVelocity(Rb);

        // 넉백 뒤 이동 로직이 속도를 덮어쓰지 않는 남은 시간(초). 이동이 속도를 직접 넣으므로, 안 막으면 넉백 임펄스가 다음 물리 스텝에 지워진다
        private float _moveHoldTimer;

        /// <summary>넉백 유지 중이라 이동 로직이 속도를 건드리면 안 되는지.</summary>
        public bool IsMoveHeld => _moveHoldTimer > 0f;

        /// <summary>
        /// 넉백을 받았을 때 부른다 (MeleeSystem). knockbackMoveHold초 동안 달리기·정지가 속도를 덮어쓰지 않는다.
        /// 대시 중이었으면 대시를 끝낸다 — 안 끝내면 유지 시간이 지난 뒤 맞은 자리에서 다시 30m/s로 돌진한다.
        /// </summary>
        public void HoldMoveControl()
        {
            _moveHoldTimer = Mathf.Max(_moveHoldTimer, Move.knockbackMoveHold);
            if (StateMachine != null && StateMachine.IsDashing) StateMachine.ActiveDash.EndByKnockback();
        }

        /// <summary>
        /// V3 이동의 핵심: 가속도 없이 수평 속도를 바로 direction × speed로 (세로 속도는 그대로).
        /// 콤보 중·쓰리 회복 중엔 느리게 (N2). 외부 제어(앵커 클로 등)·넉백 유지 중엔 양보.
        /// </summary>
        public void SetMoveVelocity(Vector3 direction, float speed)
        {
            float speedScale = _melee != null ? _melee.MoveSpeedScale : 1f;
            SetHorizontalVelocity(direction * (speed * speedScale));
        }

        /// <summary>수평 속도를 그대로 지정 (배율 없음). 외부 제어·넉백 유지 중엔 양보.</summary>
        public void SetHorizontalVelocity(Vector3 horizontal)
        {
            if (IsExternallyControlled || IsMoveHeld) return;
            // V8: 이동 상태는 틱마다 속력을 직접 넣으므로 여기선 외부 속도 쪽만 (착지 경직의 "제자리" 등은 원래 0)
            if (IsV8)
            {
                if (Motor.IsExternalMode) MotionBridge.SetHorizontalVelocity(Rb, horizontal);
                return;
            }
            Rb.linearVelocity = new Vector3(horizontal.x, Rb.linearVelocity.y, horizontal.z);
        }

        /// <summary>정지: 수평 속도 0 (V3: 미끄러지는 감속 없음). 스턴 중엔 넉백·밀림이 남게 건드리지 않는다.</summary>
        public void StopHorizontal()
        {
            if (_melee != null && _melee.IsStunned) return;
            SetMoveVelocity(Vector3.zero, 0f);
        }

        /// <summary>
        /// 걷기 (V6a, 예전 "달리기"). 스틱 기울기가 walkStickThreshold(0.3) 이상이면 그 방향의 최대 속도, 미만이면 정지 — 디지털에 가깝게.
        /// 하드락 밖: 몸 기준 방향. 하드락 안: 상대 기준 방향(위 = 상대 쪽), 같은 속도 (V6a에서 근접 8m/s 감속 제거).
        /// 속도는 스틱 각도(앞 0° ~ 뒤 180°)로 방향별 5점 값을 보간한다.
        /// </summary>
        public void ApplyRunVelocity(Vector2 input)
        {
            SetHorizontalVelocity(ComputeRunVelocity(input));
        }

        /// <summary>걷기가 원하는 수평 속도 (콤보 이동 배율 포함).</summary>
        public Vector3 ComputeRunVelocity(Vector2 input)
        {
            if (input.magnitude < Move.walkStickThreshold || input.sqrMagnitude < 0.000001f) return Vector3.zero;
            Vector2 stick = input.normalized;

            Vector3 direction;
            if (TryGetHardLockForward(out Vector3 toOpponent))
            {
                Vector3 right = new Vector3(toOpponent.z, 0f, -toOpponent.x);
                direction = (toOpponent * stick.y + right * stick.x).normalized;
            }
            else
            {
                direction = BodyRelativeDirection(stick);
            }
            float speedScale = _melee != null ? _melee.MoveSpeedScale : 1f;
            return direction * (Move.walkSpeed.Evaluate(stick) * speedScale);
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
            // V8은 이동기가 지면을 직접 본다
            if (IsV8) IsGrounded = Motor.IsGrounded;
            else UpdateGrounded();
        }

        /// <summary>
        /// 대시 중 공격 없이 상대 몸에 부딪히면 둘 다 멈추고 살짝 밀려난다(V3 "충돌", 피해 없음).
        /// 차징이 찬 상태로 진행 방향 정면에서 닿으면 예전처럼 자동 태클로 넘긴다. 상대가 대시 중이면 상대 대시도 끝난다.
        /// 벽·지형(리지드바디 없는 콜라이더)에 진행 방향으로 부딪히면 대시가 끝난다(바닥은 무시).
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            // V8: 벽·몸 충돌은 이동기가 캐스트로 처리한다 (키네마틱이라 정적 지형과는 접촉이 안 생김)
            if (IsV8) return;
            if (StateMachine == null || !StateMachine.IsDashing || Identity == null) return;
            DashState dash = StateMachine.DashState;
            if (!Identity.IsOpponentCollider(collision.collider))
            {
                CheckDashWall(collision, dash);
                return;
            }
            if (dash.IsAttacking) return;
            if (dash.IsTackleCharged && _melee != null && _melee.IsOpponentInTackleArc(dash.Heading) && dash.RequestTackle()) return;

            MechController other = Identity.Opponent != null ? Identity.Opponent.GetComponent<MechController>() : null;
            Vector3 apart = Rb.position - (other != null ? other.Rb.position : collision.transform.position);
            apart.y = 0f;
            if (apart.sqrMagnitude < 0.0001f) apart = -(Rb.rotation * Vector3.forward);
            apart.Normalize();

            ApplyBump(apart);
            if (other != null) other.ApplyBump(-apart);
        }

        // 벽에 붙은 채 그쪽으로 대시를 시작하면 Enter가 다시 안 오므로 붙어 있는 동안에도 본다
        private void OnCollisionStay(Collision collision)
        {
            if (IsV8) return;
            if (StateMachine == null || !StateMachine.IsDashing || Identity == null) return;
            if (Identity.IsOpponentCollider(collision.collider)) return;
            CheckDashWall(collision, StateMachine.DashState);
        }

        /// <summary>정적 지형의 옆면(법선이 수평에 가까움)이 진행 방향을 막고 있으면 대시를 끝낸다. 공격 중엔 끝까지 간다.</summary>
        private void CheckDashWall(Collision collision, DashState dash)
        {
            if (dash.IsAttacking || collision.collider == null || collision.collider.attachedRigidbody != null) return;
            // Wall 레이어(V5)는 면 방향과 상관없이 벽, 나머지 정적 지형은 옆면만 벽
            bool wallLayer = GroundLayers.IsWallLayer(collision.collider);
            int count = collision.contactCount;
            for (int i = 0; i < count; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (!wallLayer && Mathf.Abs(normal.y) > Move.dashWallMaxNormalY) continue;
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Dot(normal.normalized, dash.Heading) <= -Move.dashWallFacingDot)
                {
                    dash.EndByWall();
                    return;
                }
            }
        }

        /// <summary>몸 충돌 반동: 대시를 끝내고 direction 쪽으로 bumpSpeed만큼 밀린다(넉백처럼 잠깐 이동 입력을 막음).</summary>
        public void ApplyBump(Vector3 direction)
        {
            if (IsExternallyControlled) return;
            if (StateMachine != null && StateMachine.IsDashing) StateMachine.ActiveDash.EndByBump();
            Vector3 push = direction * Move.bumpSpeed;
            // 넉백 유지를 먼저 걸어야 V8 외부 속도가 "걷던 속도 + 반동"이 아니라 반동만으로 시작한다
            if (IsV8) MotionBridge.AddImpulse(Rb, new Vector3(push.x, 0f, push.z) * Rb.mass);
            else Rb.linearVelocity = new Vector3(push.x, Rb.linearVelocity.y, push.z);
            HoldMoveControl();
        }

        /// <summary>라운드 시작: 넉백 유지 시간 초기화, 땅에 있는 것으로.</summary>
        public void ResetForRound()
        {
            _moveHoldTimer = 0f;
            IsGrounded = true;
            _lastGroundedTime = Time.time;
            if (IsV8) Motor.ResetForRound();
        }

        // ---- 접지 (V5) ----

        /// <summary>발밑에 설 수 있는 땅이 있는지 (Walkable 레이어의 위를 향한 면, GroundLayers). 물리 스텝마다 갱신.</summary>
        public bool IsGrounded { get; private set; } = true;
        private float _lastGroundedTime;
        /// <summary>마지막으로 땅을 밟은 뒤 지난 시간(초).</summary>
        public float TimeSinceGrounded => IsGrounded ? 0f : Time.time - _lastGroundedTime;

        private Collider _bodyCollider;
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        /// <summary>
        /// 기체 콜라이더 중심에서 아래로 구를 쏴서, 콜라이더 바닥 + groundProbeSkin 안에 땅이 있으면 접지.
        /// 내 콜라이더·다른 기체·트리거는 무시. 면 판정은 GroundLayers (Walkable + 법선 y ≥ groundMinNormalY).
        /// </summary>
        private void UpdateGrounded()
        {
            if (_bodyCollider == null) _bodyCollider = GetComponent<Collider>();
            if (_bodyCollider == null)
            {
                IsGrounded = true;
                return;
            }
            Bounds bounds = _bodyCollider.bounds;
            float radius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f);
            Vector3 origin = bounds.center;
            float distance = Mathf.Max(0f, bounds.extents.y - radius) + Move.groundProbeSkin;
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, GroundHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool grounded = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = GroundHits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (Identity != null && Identity.IsOwnCollider(hit.collider)) continue;
                // 시작부터 겹친 것(distance 0)은 법선이 의미 없다 — 바닥 판정 기준만 본다
                Vector3 normal = hit.distance > 0f ? hit.normal : Vector3.up;
                if (GroundLayers.IsGround(hit.collider, normal, Move.groundMinNormalY))
                {
                    grounded = true;
                    break;
                }
            }
            IsGrounded = grounded;
            if (grounded) _lastGroundedTime = Time.time;
        }

        /// <summary>
        /// 공중 중력 (V5): 알짜 중력이 아래로 gravity(m/s²)가 되게 물리 중력과의 차이를 더한다. 낙하 속도는 maxFallSpeed(V6c 7.5m/s)까지 —
        /// 떠오르듯 천천히 내려오는 템진식 하강.
        /// 앵커 클로에 잡혀 끌려가는 중(외부 제어)엔 끌어당기는 쪽에 맡긴다.
        /// </summary>
        public void ApplyAirGravity(float gravity)
        {
            if (IsExternallyControlled || Rb.isKinematic) return;
            float extra = -Physics.gravity.y - gravity;
            if (Rb.useGravity) Rb.AddForce(Vector3.up * extra, ForceMode.Acceleration);
            else Rb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            Vector3 v = Rb.linearVelocity;
            if (v.y < -Move.maxFallSpeed) Rb.linearVelocity = new Vector3(v.x, -Move.maxFallSpeed, v.z);
        }

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