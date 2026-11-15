using System.Collections.Generic;
using UnityEngine;
using MyDongari.Core.VirtualOn.States;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// V8 버추얼온식 이동기 본체 (docs/REFERENCE_MOVEMENT.md, 계획 docs/PLAN_V8.md). 기체마다 하나, MechController가 붙인다.
    /// 60Hz 고정 틱(VoTickDriver)에서 한 틱 (전투 사양서 4.2): 입력 → 상태 보조 → 상대 관계 → 자동 선회 → 피격 정산(호스트)
    /// → 수평 속력 0 → 상태 메인 → 이동 적분 → 틱 끝(호스트: 히트스톱 카운트). 히트스톱 중인 틱은 상태·이동을 건너뛴다.
    /// 위치는 "위치 += 속도"로 직접 옮기고 리지드바디는 키네마틱(MovePosition, 보간 켬) — 충돌은 캐스트·겹침 판정에만 쓴다.
    /// 거리·속도 수치는 원본 단위(u, u/f)로 받고 1u = 기체 키 / 22(렌더러로 잼)로 바꾼다. 수치 = VoSpec(원본값) + StyleOverrides(우리 값).
    /// 전투가 미는 것(넉백·넉다운·런지·앵커 클로·몸 충돌)은 외부 속도(MotionBridge)로 들어와, 그동안은 상태 대신 그 속도로 움직인다.
    /// 우리 게임과는 IVoHost(수치·입력·스턴·콤보 감속·가드 자동 선회·상대·카메라)와 IVoMachine(상태 전환)으로만 이어진다 — Bind로 받는다.
    /// 입력·상태 머신이 없는 기체(연습 봇)에서는 켜지지 않는다 — 그 기체는 다이내믹 리지드바디로 남고 MotionBridge가 리지드바디로 보낸다.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class VoMotor : MonoBehaviour
    {
        [Tooltip("켜면 시작할 때 잰 기체 키·단위와 상태 전환을 콘솔에 찍는다")]
        [SerializeField] private bool debugLog = false;

        private static readonly Dictionary<Rigidbody, VoMotor> ByBody = new Dictionary<Rigidbody, VoMotor>();

        /// <summary>리지드바디의 이동기 (없으면 null).</summary>
        public static VoMotor Of(Rigidbody rb) => rb != null && ByBody.TryGetValue(rb, out VoMotor m) ? m : null;

        /// <summary>우리 게임 쪽 연결 (수치·입력·스턴 등).</summary>
        public IVoHost Host { get; private set; }
        /// <summary>상태 머신.</summary>
        public IVoMachine StateMachine { get; private set; }
        public Rigidbody Rb { get; private set; }
        /// <summary>이동기 수치 (원본값 + 호스트가 덮어쓴 우리 스타일).</summary>
        public VoSpec V => Host.Spec;

        /// <summary>호스트·상태 머신을 받는다 (MechController가 붙인 직후 한 번).</summary>
        public void Bind(IVoHost host, IVoMachine stateMachine)
        {
            Host = host;
            StateMachine = stateMachine;
        }

        /// <summary>V8 이동기가 지금 이 기체를 움직이는지.</summary>
        public bool IsActive { get; private set; }

        // ---- 단위 ----
        /// <summary>렌더러로 잰 기체 키(m).</summary>
        public float BodyHeight { get; private set; } = 1.93f;
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
        /// <summary>
        /// 이번 틱 수평 속력이 보정(근접 감속 × 0.78·추적 포화 × 0.1·넘어가기 방지)까지 끝난 값인지. 근접 구동이 켠다 —
        /// 그러면 이동 적분이 다시 곱하지 않는다. 매 틱 꺼진 채로 시작.
        /// </summary>
        public bool SpeedIsFinal { get; set; }

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
        /// <summary>높이차(내 발 − 상대 발, u). 내가 위면 +.</summary>
        public float OpponentHeightDiffU { get; private set; }
        /// <summary>근접 구역 A 안인지 (전투 사양서 1.1: 높이차 창 → 안쪽 원 → 원뿔). 공격 버튼이 근접으로 바뀌는 조건.</summary>
        public bool InMeleeZone { get; private set; }
        /// <summary>높이차 창을 빼고 본 근접 구역 (점프 이륙·점프 근접 예외).</summary>
        public bool InMeleeZoneAnyHeight { get; private set; }
        /// <summary>근접 구역의 안쪽 원 안(밀착)인지.</summary>
        public bool InMeleeInner { get; private set; }
        /// <summary>지연 표적 (이동 사양서 1.2 ②-1). 근접 추적 회전의 표적.</summary>
        public VoDelayedTarget DelayedTarget { get; } = new VoDelayedTarget();
        /// <summary>지연 표적의 방위(°, Unity Y 회전). 상대가 없으면 내 방향.</summary>
        public float DelayedTargetBearing { get; private set; }

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
        /// <summary>이번 틱 잠금이 근접 구역 서기의 스틱 중립 ○인지 — 앞 대시를 시작하고 0.25초 안에 스틱이 오면 근접 스텝으로 (V8.2).</summary>
        public bool LockedNeutralClose { get; private set; }
        /// <summary>잠금이 ○를 누른 틱보다 나중(스틱이 나중에 들어옴)인지 — 근접 스텝 조건.</summary>
        public bool LockedAfterPress { get; private set; }
        public bool JumpNew { get; private set; }
        public bool JumpHeld { get; private set; }
        /// <summary>△를 누른 채 지난 틱.</summary>
        public int JumpHeldFrames { get; private set; }
        /// <summary>△를 누른 뒤 지난 틱 (안 눌렀으면 큰 값). 점프 근접의 "점프 입력 후 8f 이내" 판정용.</summary>
        public int JumpPressFrames { get; private set; } = 9999;
        /// <summary>급강하 입력(V9a: 공중에서 ✕) 새 입력.</summary>
        public bool DiveNew { get; private set; }
        /// <summary>급강하 입력을 누르고 있는지 (공중에서만). 하강 가속이 읽는다.</summary>
        public bool DiveHeld { get; private set; }
        /// <summary>수동 선회 계수 (−1 ~ 1, 부호 유지 제곱). 오른쪽 +.</summary>
        public float TurnCoefficient { get; private set; }
        /// <summary>마우스로 모은 선회(°, 키보드·마우스 예비 조작).</summary>
        public float MouseTurnDegrees { get; private set; }

        // ---- 자동 선회·카메라 방위 (사양서 2.3) ----
        public enum AutoTurnMode { Default, JumpAim, SustainAim, MeleeTrack, AirDashReturn }
        public AutoTurnMode AutoTurn { get; private set; }
        private int _autoTurnFrames;
        /// <summary>카메라 방위(°). 기본 모드에선 몸에 붙고, 점프 조준·공중 대시 중엔 몸을 최대 22.5°/f로 쫓는다.</summary>
        public float CameraYaw { get; private set; }
        /// <summary>선회 리드각(°). 선회 방향으로 화면이 앞서 돈다.</summary>
        public float CameraLead { get; private set; }
        /// <summary>이번 틱 근접 추적이 최대 선회율에 걸렸는지 (수평 속력 × 0.1).</summary>
        public bool MeleeTrackSaturated { get; private set; }
        /// <summary>이번 틱 가드 자동 선회가 몸을 돌렸는지 (수동 선회를 막는다).</summary>
        private bool _guardTurning;

        private Collider _body;
        private CapsuleCollider _capsule;
        private VoState _lastLoggedState;

        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            _body = GetComponent<Collider>();
            _capsule = _body as CapsuleCollider;
            if (Rb != null) ByBody[Rb] = this;
        }

        private void OnDestroy()
        {
            if (Rb != null && ByBody.TryGetValue(Rb, out VoMotor m) && m == this) ByBody.Remove(Rb);
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
            Activate();
        }

        /// <summary>
        /// 입력·상태 머신이 있는 기체면 이동기를 켠다 (리지드바디 키네마틱, 틱 드라이버 등록). 시작할 때 한 번.
        /// 없으면 꺼진 채로 둔다 — 그 기체는 다이내믹 리지드바디로 남는다.
        /// </summary>
        private void Activate()
        {
            if (IsActive || Host == null || Rb == null || !Host.HasInput || StateMachine == null) return;
            IsActive = true;
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
            Host.ResetInput();
            VoTickDriver.Register(this);
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
            // 스모크 테스트(빌드)에서도 남긴다 — 에디터 없이 실제 기체 키를 확인하려고 (V7b)
            if (debugLog || AppRuntime.IsSmokeTest)
            {
                Debug.Log($"[VoMotor] {name}: 렌더러로 잰 키 {measured:0.00}m → 사용 {BodyHeight:0.00}m, 1u = {U:0.0000}m, " +
                          $"1u/f = {SpeedScale:0.00}m/s, 발 오프셋 {_footOffset:0.00}m", this);
                // 정리 뒤에도 수치가 그대로인지 비교하려고 유효값 전부와 원본과 다른 칸을 남긴다
                Debug.Log($"[VoSpec] {name} 유효값:\n{V.Dump()}", this);
                Debug.Log($"[VoSpec] {name} 원본과 다른 칸:\n{V.DescribeDifferences()}", this);
                Debug.Log($"[VoCombatSpec] {name} 원본과 다른 칸:\n{Host.CombatSpec.DescribeDifferences()}", this);
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

        // 이번 틱 외부 속도 적분에서 감속 흉내를 건너뛸지 (틱마다 속도를 정해 주는 피격 반응용)
        private bool _skipExternalDamping;

        /// <summary>이번 틱은 외부 속도를 감속 없이 그대로 적분한다.</summary>
        public void SkipExternalDampingThisTick()
        {
            _skipExternalDamping = true;
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
            DelayedTarget.Reset();
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
            JumpPressFrames = 9999;
            Host.ResetInput();
        }

        /// <summary>호스트가 이동 적분 뒤 위치를 고칠 때 (기체끼리 분리).</summary>
        public void Nudge(Vector3 delta)
        {
            _position += delta;
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
        /// 스위치(원본은 꺼짐, 우리 스타일 V9e가 켠다): fastTurnOnlyTowardOpponent면 빠른 '정면 밖' 선회율은 상대 쪽으로 도는 입력에만 쓰고,
        /// turnBlendEndAngle이 정면 경계보다 크면 그 사이는 선형으로 올린다. 상대를 지나쳐 계속 돌 때 느림 ↔ 759°/s가 번갈아 끊기던 것을 없앤다.
        /// </summary>
        public float ApplyManualTurn(bool dashing)
        {
            // 호스트가 요청한 자동 선회(가드 중, V9a)가 몸을 돌리는 틱엔 R스틱·마우스 선회를 받지 않는다
            if (_guardTurning) return 0f;
            float rateUnits = ManualTurnRate(dashing, TurnCoefficient);
            float degrees = TurnCoefficient * VoUnits.AngleUnitsToDegrees(rateUnits) + MouseTurnDegrees;
            AddYaw(degrees);
            return degrees;
        }

        private float ManualTurnRate(bool dashing, float turnInput)
        {
            VoSpec v = V;
            float off = Mathf.Abs(OpponentAngleOff);
            float insideRate, outsideRate;
            if (dashing)
            {
                insideRate = v.turnDashInside;
                outsideRate = v.turnDashOutside;
            }
            else
            {
                insideRate = !InCloseZone ? v.turnInsideFar
                    : (HasOpponent && off <= v.nearCenterHalfAngle ? v.turnInsideNearCenter : v.turnInsideNear);
                outsideRate = v.turnOutside;
            }
            // 상대가 없으면 찾을 것도 없으니 예전대로 '정면 밖' 값
            if (!HasOpponent) return outsideRate;
            // 스위치(V9e): 상대에게서 멀어지는 쪽으로 도는 입력은 항상 '정면 안' 선회율 — 빠른 선회는 상대를 찾아 도는 동작에만
            // (+선회 입력은 +각, OpponentAngleOff도 +면 +각 쪽에 상대가 있다). 원본은 방향을 가리지 않는다
            bool toward = turnInput * OpponentAngleOff > 0f;
            if (v.fastTurnOnlyTowardOpponent && !toward) return insideRate;
            return Mathf.Lerp(insideRate, outsideRate, OutsideBlend(off));
        }

        /// <summary>
        /// 상대 각도(°, 절댓값)가 정면 경계(frontHalfAngle)에서 turnBlendEndAngle까지 벌어지는 정도 0~1 (V9e).
        /// 0 = 정면 안 선회율, 1 = 정면 밖 선회율.
        /// </summary>
        private float OutsideBlend(float absAngleOff)
        {
            VoSpec v = V;
            if (v.turnBlendEndAngle <= v.frontHalfAngle) return absAngleOff > v.frontHalfAngle ? 1f : 0f;
            return Mathf.InverseLerp(v.frontHalfAngle, v.turnBlendEndAngle, absAngleOff);
        }

        /// <summary>점프·공중 대시의 고정율 선회 (부호만 씀). 돌린 각(°).</summary>
        public float ApplyFixedTurn(float rateUnits)
        {
            float sign = Mathf.Abs(TurnCoefficient) > 0f ? Mathf.Sign(TurnCoefficient) : 0f;
            float degrees = sign * VoUnits.AngleUnitsToDegrees(rateUnits) + MouseTurnDegrees;
            AddYaw(degrees);
            return degrees;
        }

        public void SetAutoTurn(AutoTurnMode mode)
        {
            AutoTurn = mode;
            _autoTurnFrames = 0;
        }

        /// <summary>
        /// 근접 추적 시작 (전투 사양서 1.3, 이동 사양서 2.3 "근접 추적"). 다음 틱부터 attackAimFrames(15f) 동안 몸을 상대 쪽으로
        /// 최대 rateUnits(각 단위/f, 근접 변형별)로 돌린다. 상대가 근접 구역의 안쪽 원에 들어오면 즉시 끝.
        /// 표적은 원본대로 "지연 표적 위치"다 (DelayedTarget — VoSpec.delayedTargetEnabled를 끄면 상대의 지금 위치).
        /// </summary>
        public void StartMeleeTrack(float rateUnits)
        {
            if (!IsActive) return;
            _meleeTrackRate = rateUnits;
            SetAutoTurn(AutoTurnMode.MeleeTrack);
        }

        // 이번 근접 추적의 최대 선회율(각 단위/f)
        private float _meleeTrackRate;

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
            bool machineOn = StateMachine.StatesEnabled;
            _externalMode = Host.IsExternalMode;

            // 히트스톱 중: 모션·수평 이동·중력이 멈춘다 (전투 사양서 2.4). 입력 카운터·무적은 계속 돈다
            bool frozen = Host.IsHitStopped;

            // 상태 보조 (중력·일부 y 적분·대시 게이지 가산·대시 정지 입력)
            if (machineOn && !_externalMode && vo != null && !frozen) vo.Aux();

            // 본 갱신 1 → 2: 지연 표적 갱신 → 상대 관계 (이동 사양서 1.2 ②)
            UpdateDelayedTarget();
            UpdateOpponentRelation();
            // 공용: 대시 게이지 −200/f
            DashGauge = Mathf.Max(0f, DashGauge - V.gaugeDecay);

            if (!_externalMode && !frozen) UpdateAutoTurn();
            UpdateCameraYaw();

            // 피격 정산 (전투 사양서 4.2-②-4): 호스트가 피격을 적용하고 전투 입력을 처리한다. 여기서 상태가 바뀔 수 있다
            Host.SettleHits(this);
            // 이번 틱에 맞아 히트스톱이 걸렸으면 이 틱부터 멈춘다
            frozen = Host.IsHitStopped;
            machineOn = StateMachine.StatesEnabled;

            HorizontalSpeed = 0f;
            MoveDirection = Forward;
            SpeedIsFinal = false;

            // 상태가 Aux에서 바뀌었을 수 있다
            state = StateMachine.CurrentState;
            vo = state as VoState;
            if (machineOn && !frozen)
            {
                if (vo != null)
                {
                    if (!_externalMode) vo.Main();
                }
                else if (state != null)
                {
                    // 게임 상태(런지·넉다운·착지 경직)는 그대로 돌린다 — 프레임·물리 스텝 일을 틱에서 한 번씩
                    state.Update();
                    if (StateMachine.CurrentState == state) state.FixedUpdate();
                }
            }
            // 외부 상태가 이번 틱에 시작·끝났을 수 있다
            bool external = Host.IsExternalMode;
            if (_externalMode && !external) LeaveExternal();
            _externalMode = external;

            if (!frozen)
            {
                if (_externalMode) IntegrateExternal();
                else IntegrateLocomotion(StateMachine.CurrentState as VoState);
            }

            Velocity = (_position - start) / Dt;
            Commit();
            Host.EndTick(this);

            if (debugLog && StateMachine.CurrentState is VoState now && now != _lastLoggedState)
            {
                _lastLoggedState = now;
                Debug.Log($"[VoMotor] {name} → {now.GetType().Name} (게이지 {DashGauge:0}, vy {Vy:0.000}u/f)", this);
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
            VoSpec v = V;
            VoInputFrame frame = Host.ReadInput();
            TurboNew = frame.TurboNew;
            JumpNew = frame.JumpNew;
            TurboHeld = frame.TurboHeld;
            JumpHeld = frame.JumpHeld;
            TurboFrames = TurboNew ? 0 : (TurboHeld ? Mathf.Min(TurboFrames + 1, 9999) : 9999);
            JumpHeldFrames = JumpHeld ? (JumpNew ? 0 : JumpHeldFrames + 1) : 0;
            JumpPressFrames = JumpNew ? 0 : Mathf.Min(JumpPressFrames + 1, 9999);

            Vector2 stick = frame.LeverStick;
            bool airborne = StateMachine.CurrentState is VoState s && s.IsAirState;
            // 급강하 입력은 공중일 때만 받는다 (우리 조작에선 같은 버튼이 땅에서 가드·패리라서, V9a)
            DiveNew = airborne && frame.DiveNew;
            DiveHeld = airborne && frame.DiveHeld;

            // 입력 축소: 상대 8.3m 안 × 0.7, 11.8m까지 선형으로 1 (걷기·점프 속력 배율)
            float distanceScale = 1f;
            if (HasOpponent)
            {
                distanceScale = OpponentDistanceU <= v.leverNearDistance
                    ? v.leverNearScale
                    : Mathf.Lerp(v.leverNearScale, 1f, Mathf.InverseLerp(v.leverNearDistance, v.leverFarDistance, OpponentDistanceU));
            }
            Lever.Tick(stick, distanceScale);

            // 선회 축 = 수동 선회 (사양서의 레버 차동). |s| ≤ 0.05 무시, 부호 유지 제곱
            float x = Mathf.Clamp(frame.TurnAxis, -1f, 1f);
            TurnCoefficient = Mathf.Abs(x) <= v.turnDeadzone ? 0f : Mathf.Sign(x) * x * x;
            MouseTurnDegrees = frame.ExtraTurnDegrees;

            // 대시 방향 잠금 (사양서 2.1): ① 터보를 새로 누른 틱의 레버 방향(서기·걷기·지상 대시·점프·근접 스텝).
            // 스위치(V8.1, 원본은 중립이면 잠금 없음): neutralTurboDashesForward면 중립은 앞(0) —
            // 근접 구역 서기의 중립 터보는 바로 앞 대시 + closeStepFromDashFrames 안 레버면 근접 스텝으로 바뀌게 표시(LockedNeutralClose, V8.2).
            // ② 서기·걷기에서 터보를 누른 뒤 15f 안(누르고 있는 중)에 레버가 들어옴 → "나중에 잠김"(근접 스텝 조건)
            LockedDirection = -1;
            LockedAfterPress = false;
            LockedNeutralClose = false;
            VoState current = StateMachine.CurrentState as VoState;
            if (current == null) return;
            if (TurboNew && current.LocksOnTurboPress)
            {
                if (Lever.Direction >= 0)
                {
                    LockedDirection = Lever.Direction;
                }
                else if (v.neutralTurboDashesForward)
                {
                    LockedDirection = 0;
                    LockedNeutralClose = current is VoStandState && InCloseZone;
                }
            }
            else if (!TurboNew && TurboHeld && TurboFrames <= v.dashLockWindow && current.LocksWithinWindow && Lever.Direction >= 0)
            {
                LockedDirection = Lever.Direction;
                LockedAfterPress = true;
            }
        }

        /// <summary>
        /// 지연 표적 갱신 (이동 사양서 1.2 ②-1). 상대의 같은 행동(상태 객체) 15f 이상이면 반씩 따라가고 아니면 상대 위치로 바로.
        /// 상대에게 이동기가 없으면 행동을 모르므로 null(늘 같은 행동)로 센다.
        /// </summary>
        private void UpdateDelayedTarget()
        {
            Transform opponent = Host.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy)
            {
                DelayedTarget.Reset();
                DelayedTargetBearing = _yaw;
                return;
            }
            VoSpec v = V;
            VoMotor other = Of(opponent.GetComponent<Rigidbody>());
            object action = other != null && other.IsActive && other.StateMachine != null ? other.StateMachine.CurrentState : null;
            Vector3 target = DelayedTarget.Update(OpponentPosition(opponent), action, v.delayedTargetEnabled, v.delayedTargetFrames, v.delayedTargetFollow);
            Vector3 offset = target - _position;
            offset.y = 0f;
            DelayedTargetBearing = offset.sqrMagnitude > 0.000001f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : _yaw;
        }

        private void UpdateOpponentRelation()
        {
            Transform opponent = Host.Opponent;
            HasOpponent = opponent != null && opponent.gameObject.activeInHierarchy;
            if (!HasOpponent)
            {
                OpponentDistanceU = float.MaxValue;
                InCloseZone = false;
                InMeleeZone = false;
                InMeleeZoneAnyHeight = false;
                InMeleeInner = false;
                return;
            }
            Vector3 target = OpponentPosition(opponent);
            Vector3 offset = target - _position;
            OpponentHeightDiffU = -offset.y / U;
            offset.y = 0f;
            OpponentDistanceU = offset.magnitude / U;
            OpponentBearing = offset.sqrMagnitude > 0.000001f ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : _yaw;
            OpponentAngleOff = Mathf.DeltaAngle(_yaw, OpponentBearing);
            InCloseZone = OpponentDistanceU < V.closeZoneDistance;
            // 근접 구역 (전투 사양서 4.2-②-2: 이 틱 위치로 계산)
            VoCombatSpec c = Host.CombatSpec;
            InMeleeZoneAnyHeight = VoMeleeZone.Evaluate(c, OpponentDistanceU, OpponentAngleOff, OpponentHeightDiffU, true, out bool inner);
            InMeleeZone = VoMeleeZone.Evaluate(c, OpponentDistanceU, OpponentAngleOff, OpponentHeightDiffU, false, out _);
            InMeleeInner = inner && InMeleeZone;
        }

        /// <summary>
        /// 상대 위치: 상대도 V8이면 그 이동기의 값(1P는 2P의 지난 틱, 2P는 1P의 이번 틱 — 사양서 1.1 비대칭), 아니면 리지드바디.
        /// </summary>
        public static Vector3 OpponentPosition(Transform opponent)
        {
            Rigidbody rb = opponent.GetComponent<Rigidbody>();
            VoMotor m = Of(rb);
            if (m != null && m.IsActive) return m.Position;
            return rb != null ? rb.position : opponent.position;
        }

        /// <summary>자동 선회 (사양서 2.3). 상태 메인보다 먼저.</summary>
        private void UpdateAutoTurn()
        {
            VoSpec v = V;
            MeleeTrackSaturated = false;
            _autoTurnFrames++;
            _guardTurning = false;
            switch (AutoTurn)
            {
                case AutoTurnMode.Default:
                {
                    // 호스트가 요청한 상대 쪽 자동 선회 (원본엔 없다 — 우리 가드 자동 선회, V9a).
                    // V9e: 정면 경계 ~ 보간 끝 각도 사이는 두 선회율 사이를 선형으로 (경계에서 속도가 뚝 바뀌지 않게)
                    if (Host.TryGetAutoTurnToOpponent(out float insideRate, out float outsideRate) && HasOpponent)
                    {
                        float guardRate = Mathf.Lerp(insideRate, outsideRate, OutsideBlend(Mathf.Abs(OpponentAngleOff)));
                        TurnToward(OpponentBearing, guardRate);
                        _guardTurning = true;
                    }
                    break;
                }
                case AutoTurnMode.JumpAim:
                    if (!HasOpponent || TurnToward(OpponentBearing, v.jumpAimRate)) SetAutoTurn(AutoTurnMode.Default);
                    break;
                case AutoTurnMode.SustainAim:
                    if (HasOpponent) TurnToward(OpponentBearing, v.dashAttackAimRate);
                    break;
                case AutoTurnMode.MeleeTrack:
                    // 안쪽 원에 들어왔거나 15f가 지났으면 끝 (이 틱은 돌지 않는다)
                    if (!HasOpponent || _autoTurnFrames > v.attackAimFrames || InMeleeInner)
                    {
                        SetAutoTurn(AutoTurnMode.Default);
                        break;
                    }
                    // 표적은 지연 표적 (원본). 스위치를 끄면 DelayedTargetBearing = 상대의 지금 방위
                    float before = Mathf.Abs(Mathf.DeltaAngle(_yaw, DelayedTargetBearing));
                    TurnToward(DelayedTargetBearing, _meleeTrackRate);
                    MeleeTrackSaturated = before > VoUnits.AngleUnitsToDegrees(_meleeTrackRate);
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
            float max = VoUnits.AngleUnitsToDegrees(rateUnits);
            AddYaw(Mathf.Clamp(diff, -max, max));
            return Mathf.Abs(Mathf.DeltaAngle(_yaw, bearing)) <= V.alignEpsilon;
        }

        /// <summary>
        /// 카메라 방위: 기본 모드는 몸에 강체로 붙고, 점프 조준·공중 대시 중엔 몸을 최대 22.5°/f로 쫓는다(공중 대시 복귀는 카메라가 기준이라 그대로).
        /// 선회 리드: 수동 선회 입력 쪽으로 1.41°/f씩 최대 8.4°, 입력이 없으면 같은 속도로 0.
        /// </summary>
        private void UpdateCameraYaw()
        {
            VoSpec v = V;
            switch (AutoTurn)
            {
                case AutoTurnMode.AirDashReturn:
                    break;
                case AutoTurnMode.JumpAim:
                {
                    float max = VoUnits.AngleUnitsToDegrees(v.cameraYawFollowRate);
                    CameraYaw = Mathf.Repeat(CameraYaw + Mathf.Clamp(Mathf.DeltaAngle(CameraYaw, _yaw), -max, max), 360f);
                    break;
                }
                default:
                    CameraYaw = _yaw;
                    break;
            }
            float step = VoUnits.AngleUnitsToDegrees(v.cameraLeadStep);
            float maxLead = VoUnits.AngleUnitsToDegrees(v.cameraLeadMax);
            float target = TurnCoefficient > 0f ? maxLead : TurnCoefficient < 0f ? -maxLead : 0f;
            if (Host.IsStunned) target = 0f;
            CameraLead = Mathf.MoveTowards(CameraLead, target, step);
        }

        // ---- 이동 적분 (사양서 1.4) ----

        private void IntegrateLocomotion(VoState state)
        {
            VoSpec v = V;
            float speed = HorizontalSpeed;
            // 근접 감속: 상대 200u 안이면 × 0.78 (근접 구동은 넘어가기 방지까지 끝낸 값을 주므로 다시 곱하지 않는다)
            if (!SpeedIsFinal)
            {
                if (HasOpponent && OpponentDistanceU < v.closeSlowDistance) speed *= v.closeSlowScale;
                if (MeleeTrackSaturated) speed *= v.attackAimSlowScale;
            }
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
                    if (state != null && StateMachine.StatesEnabled) StateMachine.V8Fall(v.ledgeFallVy, false);
                }
            }
            Host.AfterIntegrate(this);
        }

        /// <summary>외부 속도로 한 틱 (넉백·넉다운·런지·앵커 클로). 중력(리지드바디 useGravity일 때)·감속·벽·지면 포함.</summary>
        private void IntegrateExternal()
        {
            VoSpec v = V;
            float dt = Dt;
            Vector3 vel = _external;
            float gravity = v.gravity * U / (dt * dt);
            if (!IsGrounded && Rb.useGravity) vel.y -= gravity * dt;
            // 리지드바디 감속을 흉내 (PhysX: v × 1 / (1 + dt × damping))
            float damping = _skipExternalDamping ? 0f : Mathf.Max(0f, Rb.linearDamping);
            _skipExternalDamping = false;
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
            Host.AfterIntegrate(this);
        }
    }
}
