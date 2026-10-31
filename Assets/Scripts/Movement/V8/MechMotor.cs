using System.Collections.Generic;
using UnityEngine;
using MyDongari.Core;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// V8 버추얼온식 이동기 본체 (docs/REFERENCE_MOVEMENT.md, 계획 docs/PLAN_V8.md). 기체마다 하나, MechController가 붙인다.
    /// 60Hz 고정 틱(VoTickDriver)에서 한 틱: 입력 → 상태 보조 → 상대 관계 → 자동 선회 → 수평 속력 0 → 상태 메인 → 이동 적분.
    /// 위치는 "위치 += 속도"로 직접 옮기고 리지드바디는 키네마틱(MovePosition, 보간 켬) — 충돌은 캐스트·겹침 판정에만 쓴다.
    /// 거리·속도 수치는 원본 단위(u, u/f)로 받고 1u = 기체 키 / 22(렌더러로 잼)로 바꾼다.
    /// 전투가 미는 것(넉백·넉다운·런지·앵커 클로·몸 충돌)은 외부 속도(MotionBridge)로 들어와, 그동안은 상태 대신 그 속도로 움직인다.
    /// BernardTuning의 이동기 선택이 Legacy면 꺼져 있고 리지드바디는 예전처럼 다이내믹이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MechMotor : MonoBehaviour
    {
        [Tooltip("켜면 시작할 때 잰 기체 키·단위와 상태 전환을 콘솔에 찍는다")]
        [SerializeField] private bool debugLog = false;

        private static readonly Dictionary<Rigidbody, MechMotor> ByBody = new Dictionary<Rigidbody, MechMotor>();

        /// <summary>리지드바디의 이동기 (없으면 null).</summary>
        public static MechMotor Of(Rigidbody rb) => rb != null && ByBody.TryGetValue(rb, out MechMotor m) ? m : null;

        public MechController Controller { get; private set; }
        public MechStateMachine StateMachine { get; private set; }
        public Rigidbody Rb { get; private set; }
        public InputReader Input { get; private set; }
        public BernardTuning.V8Values V => Controller.Tuning.V8;

        /// <summary>V8 이동기가 지금 이 기체를 움직이는지.</summary>
        public bool IsActive { get; private set; }

        // ---- 단위 ----
        /// <summary>렌더러로 잰 기체 키(m).</summary>
        public float BodyHeight { get; private set; } = 2.6f;
        /// <summary>1u(m).</summary>
        public float U => BodyHeight / Mathf.Max(1f, V.bodyHeightUnits);
        /// <summary>틱 길이(초).</summary>
        public float Dt => 1f / Mathf.Max(1f, V.tickRate);
        /// <summary>u/f → m/s.</summary>
        public float SpeedScale => U / Dt;
        private bool _heightMeasured;

        // ---- 위치·방향 (권위 있는 값. 리지드바디엔 틱 끝에 넣는다) ----
        private Vector3 _position;
        private Vector3 _lastCommitted;
        private float _yaw;
        private float _lastCommittedYaw;
        private bool _yawChanged;
        private float _footOffset = -1f;
        /// <summary>기체 루트 위치(이번 틱 결과).</summary>
        public Vector3 Position => _position;
        /// <summary>기체 정면 방위(°, Unity Y 회전).</summary>
        public float Yaw => _yaw;
        public Vector3 Forward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        /// <summary>발바닥 높이(m).</summary>
        public float FeetY
        {
            get => _position.y + _footOffset;
            set => _position.y = value - _footOffset;
        }
        /// <summary>수직 속도(u/f, 위 +).</summary>
        public float Vy { get; set; }
        /// <summary>지난 틱에 실제로 움직인 속도(m/s). 카메라 FOV·외형·로그가 읽는다.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>땅을 밟고 있는지 (지상 상태가 지면에 붙었거나 착지했을 때).</summary>
        public bool IsGrounded { get; private set; } = true;

        // ---- 이번 틱 이동 (상태 메인이 넣는다) ----
        /// <summary>이번 틱 수평 속력(u/f). 매 틱 0으로 시작.</summary>
        public float HorizontalSpeed { get; set; }
        /// <summary>이번 틱 진행 방향(수평, 크기 1).</summary>
        public Vector3 MoveDirection { get; set; }

        // ---- 외부 속도 ----
        private Vector3 _external;
        private bool _externalMode;
        /// <summary>외부 속도(m/s). 넉백·넉다운·런지·앵커 클로가 넣는다.</summary>
        public Vector3 ExternalVelocity => _external;
        /// <summary>이번 틱이 외부 속도로 움직이는지.</summary>
        public bool IsExternalMode => _externalMode;

        // ---- 상대 관계 ----
        public bool HasOpponent { get; private set; }
        /// <summary>상대까지 수평 거리(u).</summary>
        public float OpponentDistanceU { get; private set; } = float.MaxValue;
        /// <summary>상대 방위(°, Unity Y 회전).</summary>
        public float OpponentBearing { get; private set; }
        /// <summary>정면에서 상대까지 각(°, 오른쪽 +).</summary>
        public float OpponentAngleOff { get; private set; }
        public bool InCloseZone { get; private set; }

        // ---- 대시 게이지 (사양서 1.2) ----
        public float DashGauge { get; set; }
        public bool DashAllowed => DashGauge < V.gaugeAllow;

        // ---- 입력 (틱 단위) ----
        public VoLever Lever { get; } = new VoLever();
        public bool TurboNew { get; private set; }
        public bool TurboHeld { get; private set; }
        /// <summary>○를 누른 뒤 지난 틱 (안 눌렀으면 큰 값).</summary>
        public int TurboFrames { get; private set; } = 9999;
        /// <summary>이번 틱에 잠긴 대시 방향(8방향 코드, -1 = 없음).</summary>
        public int LockedDirection { get; private set; } = -1;
        /// <summary>스틱 중립으로 ○를 누른 뒤 남은 대기 틱 (근접 구역 서기: 스틱이 오면 근접 스텝, 안 오면 앞 대시).</summary>
        private int _neutralDashWait = -1;
        /// <summary>잠금이 ○를 누른 틱보다 나중(스틱이 나중에 들어옴)인지 — 근접 스텝 조건.</summary>
        public bool LockedAfterPress { get; private set; }
        public bool JumpNew { get; private set; }
        public bool JumpHeld { get; private set; }
        /// <summary>△를 누른 채 지난 틱.</summary>
        public int JumpHeldFrames { get; private set; }
        /// <summary>급강하 입력(V8.1: 공중에서 △ 다시 누르기) 새 입력.</summary>
        public bool DiveNew { get; private set; }
        /// <summary>수동 선회 계수 (−1 ~ 1, 부호 유지 제곱). 오른쪽 +.</summary>
        public float TurnCoefficient { get; private set; }
        /// <summary>마우스로 모은 선회(°, 키보드·마우스 예비 조작).</summary>
        public float MouseTurnDegrees { get; private set; }
        private int _lastDashCount;
        private int _lastJumpCount;
        private float _pendingMouseTurn;

        // ---- 자동 선회·카메라 방위 (사양서 2.3) ----
        public enum AutoTurnMode { Default, JumpAim, SustainAim, AttackAim, AirDashReturn }
        public AutoTurnMode AutoTurn { get; private set; }
        private int _autoTurnFrames;
        /// <summary>카메라 방위(°). 기본 모드에선 몸에 붙고, 점프 조준·공중 대시 중엔 몸을 최대 22.5°/f로 쫓는다.</summary>
        public float CameraYaw { get; private set; }
        /// <summary>선회 리드각(°). 선회 방향으로 화면이 앞서 돈다.</summary>
        public float CameraLead { get; private set; }
        /// <summary>이번 틱 사격 조준이 최대 선회율에 걸렸는지 (수평 속력 × 0.1).</summary>
        private bool _attackAimSaturated;

        private Collider _body;
        private CapsuleCollider _capsule;
        private VoState _lastLoggedState;

        private void Awake()
        {
            Controller = GetComponent<MechController>();
            StateMachine = GetComponent<MechStateMachine>();
            Rb = GetComponent<Rigidbody>();
            Input = GetComponent<InputReader>();
            _body = GetComponent<Collider>();
            _capsule = _body as CapsuleCollider;
            if (Rb != null) ByBody[Rb] = this;
        }

        private void OnDestroy()
        {
            if (Rb != null && ByBody.TryGetValue(Rb, out MechMotor m) && m == this) ByBody.Remove(Rb);
            VoTickDriver.Unregister(this);
        }

        private void OnDisable()
        {
            VoTickDriver.Unregister(this);
        }

        private void OnEnable()
        {
            if (IsActive) VoTickDriver.Register(this);
        }

        private void Start()
        {
            RefreshModel();
        }

        private void Update()
        {
            // 이동기 선택이 바뀌면(플레이 중 에셋을 고쳐도) 바로 전환
            RefreshModel();
            // 마우스 이동량은 프레임마다 오므로 모아 두고 틱에서 쓴다
            if (IsActive && Input != null && !Input.IsLookFromGamepad)
            {
                _pendingMouseTurn += Input.LookInput.x * Controller.Tuning.Movement.mouseTurnSensitivity;
            }
        }

        /// <summary>BernardTuning의 이동기 선택에 맞춰 켜거나 끈다.</summary>
        public void RefreshModel()
        {
            if (Controller == null || Rb == null) return;
            bool want = Controller.Tuning.Model == MovementModel.VirtualOn && Input != null && StateMachine != null;
            if (want == IsActive) return;
            IsActive = want;
            if (want)
            {
                MeasureBody();
                Rb.isKinematic = true;
                Rb.interpolation = RigidbodyInterpolation.Interpolate;
                _position = Rb.position;
                _lastCommitted = _position;
                _yaw = Rb.rotation.eulerAngles.y;
                _lastCommittedYaw = _yaw;
                CameraYaw = _yaw;
                CameraLead = 0f;
                Vy = 0f;
                _external = Vector3.zero;
                Lever.Reset();
                if (Input != null)
                {
                    _lastDashCount = Input.DashPressCount;
                    _lastJumpCount = Input.JumpPressCount;
                }
                VoTickDriver.Register(this);
            }
            else
            {
                VoTickDriver.Unregister(this);
                Rb.isKinematic = false;
            }
            if (StateMachine != null) StateMachine.OnMovementModelChanged();
        }

        /// <summary>기체 키를 렌더러 경계로 잰다 (선 자세). 파티클·트레일·라인은 뺀다. 1~6m 밖이면 기본값.</summary>
        private void MeasureBody()
        {
            if (_body != null) _footOffset = _body.bounds.min.y - transform.position.y;
            if (_heightMeasured) return;
            _heightMeasured = true;
            float feet = transform.position.y + _footOffset;
            float top = float.MinValue;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                top = Mathf.Max(top, r.bounds.max.y);
            }
            float measured = top - feet;
            BodyHeight = measured >= 1f && measured <= 6f ? measured : V.fallbackBodyHeight;
            if (debugLog)
            {
                Debug.Log($"[MechMotor] {name}: 렌더러로 잰 키 {measured:0.00}m → 사용 {BodyHeight:0.00}m, 1u = {U:0.0000}m, " +
                          $"1u/f = {SpeedScale:0.00}m/s, 발 오프셋 {_footOffset:0.00}m", this);
            }
        }

        // ---- 외부 속도 통로 (MotionBridge) ----

        public void SetExternalVelocity(Vector3 velocity)
        {
            _external = velocity;
        }

        public void SetExternalHorizontal(Vector3 horizontal)
        {
            _external = new Vector3(horizontal.x, _external.y, horizontal.z);
        }

        public void AddExternalImpulse(Vector3 deltaVelocity)
        {
            // 걷던 속도에 얹지 않고 넉백만 (그 자리에서 밀려남). 세로는 지금 낙하 속도에서
            if (!_externalMode) _external = new Vector3(0f, Vy * SpeedScale, 0f);
            _external += deltaVelocity;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _lastCommitted = position;
            _yaw = rotation.eulerAngles.y;
            _lastCommittedYaw = _yaw;
            CameraYaw = _yaw;
            CameraLead = 0f;
            Vy = 0f;
            _external = Vector3.zero;
            Velocity = Vector3.zero;
        }

        /// <summary>라운드 시작: 게이지·입력·외부 속도 초기화, 땅에 있는 것으로.</summary>
        public void ResetForRound()
        {
            if (Rb != null) Teleport(Rb.position, Rb.rotation);
            DashGauge = 0f;
            Lever.Reset();
            AutoTurn = AutoTurnMode.Default;
            IsGrounded = true;
            TurboFrames = 9999;
            _neutralDashWait = -1;
            if (Input != null)
            {
                _lastDashCount = Input.DashPressCount;
                _lastJumpCount = Input.JumpPressCount;
            }
        }

        // ---- 선회 ----

        public void SetYaw(float degrees)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, degrees)) < 0.0001f) return;
            _yaw = Mathf.Repeat(degrees, 360f);
            _yawChanged = true;
        }

        public void AddYaw(float degrees)
        {
            if (Mathf.Abs(degrees) < 0.0001f) return;
            SetYaw(_yaw + degrees);
        }

        /// <summary>
        /// 수동 선회 한 틱 (사양서 2.2): 선회 계수 × 상황별 최대 선회율. 상대가 정면 ±33.75° 밖이면 759°/s, 안이면 132°/s,
        /// 근접 구역 정면 안은 2.81°/f(±22.5° 안 3.02°/f), 지상 대시 중은 2.11/1.76°/f. 돌린 각(°)을 돌려준다.
        /// </summary>
        public float ApplyManualTurn(bool dashing)
        {
            float rateUnits = ManualTurnRate(dashing);
            float degrees = TurnCoefficient * BernardTuning.AngleUnitsToDegrees(rateUnits) + MouseTurnDegrees;
            AddYaw(degrees);
            return degrees;
        }

        private float ManualTurnRate(bool dashing)
        {
            BernardTuning.V8Values v = V;
            bool inside = HasOpponent && Mathf.Abs(OpponentAngleOff) <= v.frontHalfAngle;
            if (dashing) return inside ? v.turnDashInside : v.turnDashOutside;
            if (!inside) return v.turnOutside;
            if (InCloseZone) return Mathf.Abs(OpponentAngleOff) <= v.nearCenterHalfAngle ? v.turnInsideNearCenter : v.turnInsideNear;
            return v.turnInsideFar;
        }

        /// <summary>점프·공중 대시의 고정율 선회 (부호만 씀). 돌린 각(°).</summary>
        public float ApplyFixedTurn(float rateUnits)
        {
            float sign = Mathf.Abs(TurnCoefficient) > 0f ? Mathf.Sign(TurnCoefficient) : 0f;
            float degrees = sign * BernardTuning.AngleUnitsToDegrees(rateUnits) + MouseTurnDegrees;
            AddYaw(degrees);
            return degrees;
        }

        public void SetAutoTurn(AutoTurnMode mode)
        {
            AutoTurn = mode;
            _autoTurnFrames = 0;
        }

        /// <summary>공격을 시작했을 때 (WeaponSystem). 사격 조준 모드 — 15f 동안 몸을 상대 쪽으로. 대시 중 공격은 대시가 지속 조준을 건다.</summary>
        public void NotifyAttackStarted()
        {
            if (!IsActive || (StateMachine != null && StateMachine.IsDashing)) return;
            if (AutoTurn == AutoTurnMode.JumpAim) return;
            SetAutoTurn(AutoTurnMode.AttackAim);
        }

        /// <summary>정지 맞춤: 카메라 방위를 몸에 바로 맞추고 기본 모드로.</summary>
        public void AlignCamera()
        {
            CameraYaw = _yaw;
            SetAutoTurn(AutoTurnMode.Default);
        }

        // ---- 한 틱 ----

        /// <summary>VoTickDriver가 1P → 2P 순서로 부른다.</summary>
        public void Tick()
        {
            if (!IsActive) return;
            SyncFromRigidbody();
            _yawChanged = false;
            Vector3 start = _position;

            ReadInput();

            MechState state = StateMachine.CurrentState;
            VoState vo = state as VoState;
            bool machineOn = StateMachine.enabled;
            _externalMode = Controller.IsMoveHeld || Controller.IsExternallyControlled ||
                            state is LungeState || state is KnockdownState;

            // 상태 보조 (중력·일부 y 적분·대시 게이지 가산·대시 정지 입력)
            if (machineOn && !_externalMode && vo != null) vo.Aux();

            UpdateOpponentRelation();
            // 공용: 대시 게이지 −200/f
            DashGauge = Mathf.Max(0f, DashGauge - V.gaugeDecay);

            if (!_externalMode) UpdateAutoTurn();
            UpdateCameraYaw();

            HorizontalSpeed = 0f;
            MoveDirection = Forward;

            // 상태가 Aux에서 바뀌었을 수 있다
            state = StateMachine.CurrentState;
            vo = state as VoState;
            if (machineOn)
            {
                if (vo != null)
                {
                    if (!_externalMode) vo.Main();
                }
                else if (state != null)
                {
                    // 예전 상태(런지·넉다운·착지 경직)는 그대로 돌린다 — 프레임·물리 스텝 일을 틱에서 한 번씩
                    state.Update();
                    if (StateMachine.CurrentState == state) state.FixedUpdate();
                }
            }
            // 외부 상태가 이번 틱에 시작·끝났을 수 있다
            bool external = Controller.IsMoveHeld || Controller.IsExternallyControlled ||
                            StateMachine.CurrentState is LungeState || StateMachine.CurrentState is KnockdownState;
            if (_externalMode && !external) LeaveExternal();
            _externalMode = external;

            if (_externalMode) IntegrateExternal();
            else IntegrateLocomotion(StateMachine.CurrentState as VoState);

            Velocity = (_position - start) / Dt;
            Commit();

            if (debugLog && StateMachine.CurrentState is VoState now && now != _lastLoggedState)
            {
                _lastLoggedState = now;
                Debug.Log($"[MechMotor] {name} → {now.GetType().Name} (게이지 {DashGauge:0}, vy {Vy:0.000}u/f)", this);
            }
        }

        /// <summary>외부 속도가 끝나면 세로 속도를 이동기 vy로 넘기고 비운다.</summary>
        private void LeaveExternal()
        {
            Vy = _external.y / Mathf.Max(0.0001f, SpeedScale);
            _external = Vector3.zero;
            if (!IsGrounded && StateMachine.CurrentState is VoState vo && vo.IsGroundState) StateMachine.V8Fall(Vy, true);
        }

        /// <summary>다른 코드가 리지드바디를 직접 옮겼으면(리스폰·넉다운 회전 등) 그 값을 받아들인다.</summary>
        private void SyncFromRigidbody()
        {
            if ((Rb.position - _lastCommitted).sqrMagnitude > 0.000001f) _position = Rb.position;
            float rbYaw = Rb.rotation.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(rbYaw, _lastCommittedYaw)) > 0.01f)
            {
                _yaw = rbYaw;
                _lastCommittedYaw = rbYaw;
            }
        }

        private void Commit()
        {
            Rb.MovePosition(_position);
            _lastCommitted = _position;
            if (_yawChanged)
            {
                Rb.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));
                _lastCommittedYaw = _yaw;
            }
        }

        private void ReadInput()
        {
            BernardTuning.V8Values v = V;
            int dashCount = Input.DashPressCount;
            int jumpCount = Input.JumpPressCount;
            TurboNew = dashCount != _lastDashCount;
            JumpNew = jumpCount != _lastJumpCount;
            _lastDashCount = dashCount;
            _lastJumpCount = jumpCount;
            // 두 틱 사이에 눌렀다 뗀 탭도 "누름"으로 본다
            TurboHeld = Input.DashHeld || TurboNew;
            JumpHeld = Input.JumpHeld || JumpNew;
            TurboFrames = TurboNew ? 0 : (TurboHeld ? Mathf.Min(TurboFrames + 1, 9999) : 9999);
            JumpHeldFrames = JumpHeld ? (JumpNew ? 0 : JumpHeldFrames + 1) : 0;

            Vector2 stick = Input.MoveInput;
            bool airborne = StateMachine.CurrentState is VoState s && s.IsAirState;
            // 급강하 = 공중에서 △ 다시 누르기 (V8.1)
            DiveNew = airborne && JumpNew;

            // 입력 축소: 상대 8.3m 안 × 0.7, 11.8m까지 선형으로 1 (걷기·점프 속력 배율)
            float distanceScale = 1f;
            if (HasOpponent)
            {
                distanceScale = OpponentDistanceU <= v.leverNearDistance
                    ? v.leverNearScale
                    : Mathf.Lerp(v.leverNearScale, 1f, Mathf.InverseLerp(v.leverNearDistance, v.leverFarDistance, OpponentDistanceU));
            }
            Lever.Tick(stick, v, distanceScale);

            // R스틱 좌우 = 수동 선회 (사양서의 레버 차동 대신). |s| ≤ 0.05 무시, 부호 유지 제곱
            float x = Input.IsLookFromGamepad ? Mathf.Clamp(Input.LookInput.x, -1f, 1f) : 0f;
            TurnCoefficient = Mathf.Abs(x) <= v.turnDeadzone ? 0f : Mathf.Sign(x) * x * x;
            MouseTurnDegrees = _pendingMouseTurn;
            _pendingMouseTurn = 0f;

            // 대시 방향 잠금 (사양서 2.1 + V8.1): ① ○를 새로 누른 틱에 스틱 방향(서기·걷기·지상 대시·점프·근접 스텝).
            // 스틱 중립이면 앞(0) — 단 근접 구역 서기에서는 15f 동안 스틱을 기다린다(근접 스텝 규칙 유지).
            // ② 서기·걷기에서 ○를 누른 뒤 15f 안(누르고 있는 중)에 스틱이 들어옴 → "나중에 잠김"(근접 스텝 조건)
            LockedDirection = -1;
            LockedAfterPress = false;
            VoState current = StateMachine.CurrentState as VoState;
            if (current == null)
            {
                _neutralDashWait = -1;
                return;
            }
            bool closeStand = current is VoStandState && InCloseZone;
            if (TurboNew && current.LocksOnTurboPress)
            {
                if (Lever.Direction >= 0)
                {
                    LockedDirection = Lever.Direction;
                    _neutralDashWait = -1;
                }
                else if (closeStand)
                {
                    _neutralDashWait = v.neutralDashWaitFrames;
                }
                else
                {
                    LockedDirection = 0;
                    _neutralDashWait = -1;
                }
            }
            else if (!TurboNew && TurboHeld && TurboFrames <= v.dashLockWindow && current.LocksWithinWindow && Lever.Direction >= 0)
            {
                LockedDirection = Lever.Direction;
                LockedAfterPress = true;
                _neutralDashWait = -1;
            }
            else if (_neutralDashWait >= 0)
            {
                // 근접 구역 중립 ○: 기다리는 동안 스틱이 안 오면 앞 대시. 서기가 아니게 됐으면(점프 등) 취소
                if (!(current is VoStandState)) _neutralDashWait = -1;
                else if (--_neutralDashWait < 0) LockedDirection = 0;
            }
        }

        private void UpdateOpponentRelation()
        {
            MechIdentity opponent = Controller.Identity != null ? Controller.Identity.Opponent : null;
            HasOpponent = opponent != null && opponent.gameObject.activeInHierarchy;
            if (!HasOpponent)
            {
                OpponentDistanceU = float.MaxValue;
                InCloseZone = false;
                return;
            }
            Vector3 target = OpponentPosition(opponent);
            Vector3 offset = target - _position;
            offset.y = 0f;
            OpponentDistanceU = offset.magnitude / U;
            OpponentBearing = offset.sqrMagnitude > 0.000001f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : _yaw;
            OpponentAngleOff = Mathf.DeltaAngle(_yaw, OpponentBearing);
            InCloseZone = OpponentDistanceU < V.closeZoneDistance;
        }

        /// <summary>
        /// 상대 위치: 상대도 V8이면 그 이동기의 값(1P는 2P의 지난 틱, 2P는 1P의 이번 틱 — 사양서 1.1 비대칭), 아니면 리지드바디.
        /// </summary>
        public static Vector3 OpponentPosition(MechIdentity opponent)
        {
            Rigidbody rb = opponent.GetComponent<Rigidbody>();
            MechMotor m = Of(rb);
            if (m != null && m.IsActive) return m.Position;
            return rb != null ? rb.position : opponent.transform.position;
        }

        /// <summary>자동 선회 (사양서 2.3). 상태 메인보다 먼저.</summary>
        private void UpdateAutoTurn()
        {
            BernardTuning.V8Values v = V;
            _attackAimSaturated = false;
            _autoTurnFrames++;
            switch (AutoTurn)
            {
                case AutoTurnMode.JumpAim:
                    if (!HasOpponent || TurnToward(OpponentBearing, v.jumpAimRate)) SetAutoTurn(AutoTurnMode.Default);
                    break;
                case AutoTurnMode.SustainAim:
                    if (HasOpponent) TurnToward(OpponentBearing, v.dashAttackAimRate);
                    break;
                case AutoTurnMode.AttackAim:
                    if (HasOpponent)
                    {
                        float before = Mathf.Abs(Mathf.DeltaAngle(_yaw, OpponentBearing));
                        TurnToward(OpponentBearing, v.attackAimRate);
                        _attackAimSaturated = before > BernardTuning.AngleUnitsToDegrees(v.attackAimRate);
                    }
                    if (!HasOpponent || _autoTurnFrames > v.attackAimFrames || InCloseZone) SetAutoTurn(AutoTurnMode.Default);
                    break;
                case AutoTurnMode.AirDashReturn:
                    if (TurnToward(CameraYaw, v.airDashReturnRate)) SetAutoTurn(AutoTurnMode.Default);
                    break;
            }
        }

        /// <summary>목표 방위로 최대 rateUnits/f 돈다. 정렬됐으면 true.</summary>
        private bool TurnToward(float bearing, float rateUnits)
        {
            float diff = Mathf.DeltaAngle(_yaw, bearing);
            float max = BernardTuning.AngleUnitsToDegrees(rateUnits);
            AddYaw(Mathf.Clamp(diff, -max, max));
            return Mathf.Abs(Mathf.DeltaAngle(_yaw, bearing)) <= V.alignEpsilon;
        }

        /// <summary>
        /// 카메라 방위: 기본 모드는 몸에 강체로 붙고, 점프 조준·공중 대시 중엔 몸을 최대 22.5°/f로 쫓는다(공중 대시 복귀는 카메라가 기준이라 그대로).
        /// 선회 리드: 수동 선회 입력 쪽으로 1.41°/f씩 최대 8.4°, 입력이 없으면 같은 속도로 0.
        /// </summary>
        private void UpdateCameraYaw()
        {
            BernardTuning.V8Values v = V;
            switch (AutoTurn)
            {
                case AutoTurnMode.AirDashReturn:
                    break;
                case AutoTurnMode.JumpAim:
                {
                    float max = BernardTuning.AngleUnitsToDegrees(v.cameraYawFollowRate);
                    CameraYaw = Mathf.Repeat(CameraYaw + Mathf.Clamp(Mathf.DeltaAngle(CameraYaw, _yaw), -max, max), 360f);
                    break;
                }
                default:
                    CameraYaw = _yaw;
                    break;
            }
            float step = BernardTuning.AngleUnitsToDegrees(v.cameraLeadStep);
            float maxLead = BernardTuning.AngleUnitsToDegrees(v.cameraLeadMax);
            float target = TurnCoefficient > 0f ? maxLead : TurnCoefficient < 0f ? -maxLead : 0f;
            if (Controller.MeleeSystem != null && Controller.MeleeSystem.IsStunned) target = 0f;
            CameraLead = Mathf.MoveTowards(CameraLead, target, step);
        }

        // ---- 이동 적분 (사양서 1.4) ----

        private void IntegrateLocomotion(VoState state)
        {
            BernardTuning.V8Values v = V;
            float speed = HorizontalSpeed;
            // 근접 감속: 상대 200u 안이면 × 0.78
            if (HasOpponent && OpponentDistanceU < v.closeSlowDistance) speed *= v.closeSlowScale;
            if (_attackAimSaturated) speed *= v.attackAimSlowScale;
            Vector3 dir = MoveDirection;
            dir.y = 0f;
            Vector3 delta = dir.sqrMagnitude > 0.000001f ? dir.normalized * (speed * U) : Vector3.zero;

            bool groundState = state == null || state.IsGroundState;
            MoveHorizontal(delta, groundState);

            if (groundState)
            {
                // 지상: y를 지면으로 덮어쓴다. 5u 이하 단차는 따라 내려가고, 더 아래면 낙하
                float stepM = v.stepHeight * U;
                if (ProbeGround(FeetY, stepM, stepM, out float ground))
                {
                    FeetY = ground;
                    IsGrounded = true;
                    Vy = 0f;
                }
                else
                {
                    IsGrounded = false;
                    if (state != null && StateMachine.enabled) StateMachine.V8Fall(v.ledgeFallVy, false);
                }
            }
            SeparateFromOpponent();
        }

        /// <summary>외부 속도로 한 틱 (넉백·넉다운·런지·앵커 클로). 중력(리지드바디 useGravity일 때)·감속·벽·지면 포함.</summary>
        private void IntegrateExternal()
        {
            BernardTuning.V8Values v = V;
            float dt = Dt;
            Vector3 vel = _external;
            float gravity = v.gravity * U / (dt * dt);
            if (!IsGrounded && Rb.useGravity) vel.y -= gravity * dt;
            // 리지드바디 감속을 흉내 (PhysX: v × 1 / (1 + dt × damping))
            float damping = Mathf.Max(0f, Rb.linearDamping);
            float k = 1f / (1f + dt * damping);
            vel.x *= k;
            vel.z *= k;

            MoveHorizontal(new Vector3(vel.x, 0f, vel.z) * dt, IsGrounded);

            float stepM = v.stepHeight * U;
            float prevFeet = FeetY;
            if (IsGrounded && vel.y <= 0f)
            {
                if (ProbeGround(FeetY, stepM, stepM, out float ground))
                {
                    FeetY = ground;
                    vel.y = 0f;
                }
                else
                {
                    IsGrounded = false;
                }
            }
            else
            {
                FeetY += vel.y * dt;
                if (vel.y <= 0f && ProbeLanding(prevFeet, FeetY, out float landY))
                {
                    FeetY = landY;
                    vel.y = 0f;
                    IsGrounded = true;
                }
                else if (vel.y > 0f)
                {
                    IsGrounded = false;
                }
            }
            _external = vel;
            SeparateFromOpponent();
        }

        /// <summary>
        /// 벽 보정 (사양서 6.2): 다음 위치까지 캡슐을 쓸어 보고, 정적 지형에 닿으면 그 앞까지 간 뒤 남은 이동에서 법선 성분을 지워 미끄러진다.
        /// 정면이면 남는 게 없어 멈춘다. 속력 값은 안 줄인다(벽에서 벗어나면 원래 속도). 지상은 캡슐 바닥을 5u 들어 올려 낮은 단차는 넘는다.
        /// </summary>
        private void MoveHorizontal(Vector3 delta, bool liftForStep)
        {
            Depenetrate();
            Vector3 remaining = delta;
            for (int i = 0; i < 3 && remaining.sqrMagnitude > 0.0000001f; i++)
            {
                float distance = remaining.magnitude;
                Vector3 dir = remaining / distance;
                if (!CastWall(dir, distance, liftForStep, out RaycastHit hit))
                {
                    _position += remaining;
                    return;
                }
                float move = Mathf.Max(0f, hit.distance - WallSkin);
                _position += dir * move;
                remaining -= dir * move;
                Vector3 n = hit.normal;
                n.y = 0f;
                if (n.sqrMagnitude < 0.000001f) return;
                n.Normalize();
                float into = Vector3.Dot(remaining, n);
                if (into < 0f) remaining -= n * into;
            }
        }

        private const float WallSkin = 0.02f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Overlaps = new Collider[16];

        private void CapsulePoints(Vector3 root, bool liftForStep, out Vector3 p1, out Vector3 p2, out float radius)
        {
            float height = _capsule != null ? _capsule.height : (_body != null ? _body.bounds.size.y : 2f);
            radius = _capsule != null ? _capsule.radius : 0.5f;
            Vector3 center = root + (_capsule != null ? _capsule.center : Vector3.zero);
            float feet = root.y + _footOffset;
            float bottom = feet + radius + (liftForStep ? V.stepHeight * U : 0f);
            float top = feet + height - radius;
            if (bottom > top) bottom = top;
            p1 = new Vector3(center.x, bottom, center.z);
            p2 = new Vector3(center.x, top, center.z);
            radius *= 0.98f;
        }

        private bool CastWall(Vector3 dir, float distance, bool liftForStep, out RaycastHit nearest)
        {
            nearest = default;
            CapsulePoints(_position, liftForStep, out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, dir, Hits, distance + WallSkin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (!IsStaticObstacle(hit.collider)) continue;
                // 시작부터 겹친 것은 밀어내기(Depenetrate)가 맡는다
                if (hit.distance <= 0f) continue;
                // 위를 향한 면(바닥·완만한 경사)은 벽이 아니다 — Wall 레이어는 면 방향과 상관없이 벽
                if (!GroundLayers.IsWallLayer(hit.collider) && hit.normal.y >= Controller.Tuning.Movement.groundMinNormalY) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>정적 지형에 파묻혀 있으면 수평으로 밀어낸다.</summary>
        private void Depenetrate()
        {
            if (_body == null) return;
            CapsulePoints(_position, true, out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                Collider other = Overlaps[i];
                if (!IsStaticObstacle(other)) continue;
                if (Physics.ComputePenetration(_body, _position, rotation, other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float depth))
                {
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.000001f) continue;
                    _position += direction.normalized * depth;
                }
            }
        }

        private bool IsStaticObstacle(Collider c)
        {
            if (c == null || c.isTrigger || c.attachedRigidbody != null) return false;
            if (Controller.Identity != null && Controller.Identity.IsOwnCollider(c)) return false;
            return true;
        }

        /// <summary>
        /// 발밑 지면: feet 위 up(m)에서 아래로 up + down(m)까지 레이. Walkable + 위를 향한 면(GroundLayers) 중 가장 높은 점.
        /// </summary>
        public bool ProbeGround(float feet, float up, float down, out float groundY)
        {
            groundY = float.MinValue;
            Vector3 origin = new Vector3(_position.x, feet + up + 0.01f, _position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, up + down + 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (Controller.Identity != null && Controller.Identity.IsOwnCollider(hit.collider)) continue;
                if (!GroundLayers.IsGround(hit.collider, hit.normal, Controller.Tuning.Movement.groundMinNormalY)) continue;
                if (hit.point.y > groundY)
                {
                    groundY = hit.point.y;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>공중에서 prevFeet → newFeet로 내려오는 사이에 지면을 지났는지 (착지 판정, 사양서 6.1: y ≤ 지면).</summary>
        public bool ProbeLanding(float prevFeet, float newFeet, out float groundY)
        {
            float up = Mathf.Max(0f, prevFeet - newFeet) + 0.05f;
            return ProbeGround(newFeet, up, 0.01f, out groundY) && groundY >= newFeet - 0.0001f;
        }

        /// <summary>천장: 머리 위로 up(m) 안에 정적 지형이 있는지 (점프 상승 중 → 급강하).</summary>
        public bool CheckCeiling(float up)
        {
            if (up <= 0f) return false;
            CapsulePoints(_position, false, out _, out Vector3 top, out float radius);
            int count = Physics.SphereCastNonAlloc(top, radius, Vector3.up, Hits, up, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsStaticObstacle(Hits[i].collider) && Hits[i].distance > 0f) return true;
            }
            return false;
        }

        /// <summary>공중 상태가 y += vy(u/f)를 할 때. 착지했으면 true (y = 지면, vy = 0, 땅에 있음).</summary>
        public bool AddVyAndCheckLanding()
        {
            float prev = FeetY;
            FeetY += Vy * U;
            IsGrounded = false;
            if (Vy <= 0f && ProbeLanding(prev, FeetY, out float ground))
            {
                FeetY = ground;
                Vy = 0f;
                IsGrounded = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 기체끼리: 사양서엔 밀어내기가 없지만 키네마틱끼리는 서로 통과하므로 캡슐 반경 합만큼만 떨어뜨린다(내 쪽만 — 상대는 자기 틱에).
        /// 지상 대시가 공격 없이 닿으면 몸 충돌 반동(V3)으로 넘긴다.
        /// </summary>
        private void SeparateFromOpponent()
        {
            MechIdentity opponent = Controller.Identity != null ? Controller.Identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;
            Vector3 other = OpponentPosition(opponent);
            Collider otherCollider = opponent.GetComponent<Collider>();
            float otherRadius = otherCollider is CapsuleCollider cc ? cc.radius : (otherCollider != null ? Mathf.Min(otherCollider.bounds.extents.x, otherCollider.bounds.extents.z) : 0.5f);
            float myRadius = _capsule != null ? _capsule.radius : 0.5f;
            float minDistance = myRadius + otherRadius;
            Vector3 offset = _position - other;
            float heightOverlap = otherCollider != null ? otherCollider.bounds.extents.y + (_body != null ? _body.bounds.extents.y : 1f) : 2f;
            if (Mathf.Abs(offset.y) >= heightOverlap) return;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance >= minDistance) return;
            Vector3 apart = distance > 0.0001f ? offset / distance : -Forward;
            _position += apart * (minDistance - distance);

            if (StateMachine.IsDashing && StateMachine.ActiveDash != null && !StateMachine.ActiveDash.IsAttacking && !StateMachine.IsAirborne)
            {
                if (StateMachine.ActiveDash.IsTackleCharged && StateMachine.ActiveDash.RequestTackle()) return;
                Controller.ApplyBump(apart);
                MechController otherController = opponent.GetComponent<MechController>();
                if (otherController != null) otherController.ApplyBump(-apart);
            }
        }
    }
}
