using System;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Combat;
using MyDongari.Game.Tuning;

namespace MyDongari.Game.Knockdown
{
    /// <summary>넘어지는 종류 (전투 사양서 2.2).</summary>
    public enum DownKind
    {
        /// <summary>다운 (근접 다운): 수평 1.6u/f, 띄움 최소 24f.</summary>
        Down,
        /// <summary>날아감: 다운과 같고 수평 2.401u/f, 띄움 최소 32f.</summary>
        Blown,
        /// <summary>공중 다운: vy +1.901, 수평 2.0u/f, 착지까지(최대 200f), 착지 후 70f 누움(수평 0).</summary>
        AirDown,
        /// <summary>파일 드라이버: 날아감의 수평 궤적 대신 우리 10m 넉백 궤적(비행 8m 포물선 + 미끄러짐 2m). 누움·기상은 같은 규칙.</summary>
        PileDriver
    }

    /// <summary>넉다운 한 틱의 결과 (전투 허브가 보고 후속 처리를 한다).</summary>
    public enum KnockTickResult
    {
        None,
        /// <summary>기상 대기가 끝났고 공격 선입력이 있다 → 즉시 공격 + 무적 16f.</summary>
        WakeAttack,
        /// <summary>대시 방향 선입력 → 상대 방위 기준 대시.</summary>
        WakeDash,
        /// <summary>점프 선입력 → 점프.</summary>
        WakeJump,
        /// <summary>기상 모션까지 끝남 → 무적 64f.</summary>
        Recovered
    }

    /// <summary>
    /// 다운·날아감·공중 다운·파일 드라이버 넉다운 공용 처리 (전투 사양서 2.2·2.3). 전부 전투 틱으로 센다.
    /// 대기(파일 드라이버의 외형 타격 순간 맞춤) → 띄움(진입 vy, 중력 0.451, 낙하 상한 4.001, 수평 등속, 착지 + 최소 틱)
    /// → 누움(40f: 진입 vy +0.2, 수평 = 기준 × (120 − 경과)/120, 중력 절반, 착지하면 vy × −0.667로 튐)
    /// → 기상 대기(56f, 연타한 틱마다 1 더 줄어 최소 28f. 선입력: 공격은 남은 12f 미만일 때만, 점프는 언제든, 대시 방향은 끝나는 순간)
    /// → 선입력이 없으면 기상 모션 30f → 끝.
    /// 그동안 ExternalControlLock을 잡아 이동 로직(상태 머신·연습 봇)이 속도를 덮어쓰지 않게 하고, 속도는 틱마다 직접 넣는다(중력 끔).
    /// 다운 중(대기·띄움·누움·기상 대기·기상 모션)은 완전 무적이다. 히트스톱 중인 틱에는 중력·수평 이동이 멈춘다.
    /// 벽·다른 콜라이더에 부딪히면 수평 이동을 그 자리에서 멈추고 큰 화면 흔들림(추가 피해 없음).
    /// 단계 이름은 외형(MechVisualRig)이 쓰던 그대로다: Flight = 띄움, Slide = 누움, GetUp = 기상 대기 + 기상 모션.
    /// </summary>
    public class KnockdownController : MonoBehaviour
    {
        public enum KnockPhase { None, Pending, Flight, Slide, GetUp }

        [Header("디버그")]
        [Tooltip("넉다운이 끝날 때 수평 이동 거리·최고 높이·벽 충돌을 콘솔에 출력 (30fps 비교용)")]
        [SerializeField] private bool debugLog;

        private TuningRef _tuning;
        private BernardTuning.KnockdownValues K => _tuning.Get(this).Knockdown;
        private static VoCombatSpec C => StyleOverrides.Current.EffectiveCombat;

        public KnockPhase Phase { get; private set; } = KnockPhase.None;
        public DownKind Kind { get; private set; }
        public bool IsKnockedDown => Phase != KnockPhase.None;
        /// <summary>다운 중 완전 무적 (대기·띄움·누움·기상 대기·기상 모션).</summary>
        public bool IsInvulnerable => IsKnockedDown;
        /// <summary>기상 대기 중 (선입력·연타를 받는 구간).</summary>
        public bool IsWaitingWake => Phase == KnockPhase.GetUp && !_rising;
        /// <summary>
        /// 지금 단계 경과(초, 외형 포즈용). 기상(GetUp)은 대기 중엔 0(누운 자세)이고, 기상 모션 30f를 외형의 기상 시간(getUpTime)에 맞춰 늘린 값.
        /// </summary>
        public float PhaseTime
        {
            get
            {
                if (Phase != KnockPhase.GetUp) return CombatTime.Seconds(_frame);
                if (!_rising) return 0f;
                return K.getUpTime * Mathf.Clamp01(_frame / (float)Mathf.Max(1, C.wakeRiseFrames));
            }
        }
        /// <summary>날아가는 수평 방향(월드).</summary>
        public Vector3 Direction => _direction;

        /// <summary>넉다운이 정해진 순간 (아직 대기 중일 수 있다).</summary>
        public event Action OnKnockdownStarted;
        /// <summary>날아오르는 순간.</summary>
        public event Action OnLaunched;
        /// <summary>착지 순간 (위치).</summary>
        public event Action<Vector3> OnLanded;
        /// <summary>벽·다른 콜라이더에 부딪혀 멈춘 순간 (부딪힌 지점).</summary>
        public event Action<Vector3> OnWallHit;
        /// <summary>기상 대기 시작.</summary>
        public event Action OnGetUpStarted;
        /// <summary>넉다운 끝. 인자: 수평 이동 거리(m), 최고 높이(m), 벽에 부딪혔는지.</summary>
        public event Action<float, float, bool> OnRecovered;

        private Rigidbody _rb;
        private MechController _controller;
        private MeleeSystem _melee;
        private ExternalControlLock _lock;
        private bool _holdingLock;
        private bool _gravityWasOn;
        private bool _gravityOff;
        private float _savedDamping;
        private bool _dampingOverridden;

        private Vector3 _direction;
        // 단계 경과(틱)
        private int _frame;
        private int _launchDelay;
        // 수직 속도(u/f, 위 +)와 중력(u/f²) — 파일 드라이버 포물선도 같은 단위로 바꿔 쓴다
        private float _vy;
        private float _gravity;
        // 수평 기준 속력(u/f)
        private float _baseSpeed;
        // 누움 단계 시작 속력(u/f) — 파일 드라이버 미끄러짐용
        private float _slideStartSpeed;
        private bool _landed;
        private bool _wallStopped;
        private Vector3 _startPosition;
        private float _startHeight;
        private float _maxHeight;
        // 기상
        private int _wake;
        private bool _rising;
        private bool _wakeAttack;
        private bool _wakeJump;

        public static KnockdownController GetOrAdd(GameObject mech)
        {
            KnockdownController knockdown = mech.GetComponent<KnockdownController>();
            return knockdown != null ? knockdown : mech.AddComponent<KnockdownController>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _controller = GetComponent<MechController>();
            _melee = GetComponent<MeleeSystem>();
        }

        private float U => _melee != null ? _melee.U : StyleOverrides.Current.Effective.fallbackBodyHeight / StyleOverrides.Current.Effective.bodyHeightUnits;
        /// <summary>u/f → m/s.</summary>
        private float SpeedScale => U * CombatTime.TickRate;

        /// <summary>
        /// 넉다운 시작. direction = 밀려나는 수평 방향(공격 방향), launchDelayFrames = 날아오르기 전 대기(틱).
        /// 이미 넉다운 중이면 처음부터 다시.
        /// </summary>
        public void Begin(DownKind kind, Vector3 direction, int launchDelayFrames)
        {
            if (_rb == null) return;
            direction.y = 0f;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            RestoreBody();
            Kind = kind;
            Phase = KnockPhase.Pending;
            _frame = 0;
            _launchDelay = Mathf.Max(0, launchDelayFrames);
            _wallStopped = false;
            _landed = false;
            _rising = false;
            _wakeAttack = false;
            _wakeJump = false;
            _startPosition = _rb.position;
            _startHeight = _rb.position.y;
            _maxHeight = 0f;

            if (_lock == null) _lock = GetComponent<ExternalControlLock>();
            if (_lock == null) _lock = gameObject.AddComponent<ExternalControlLock>();
            if (!_holdingLock)
            {
                _lock.Acquire();
                _holdingLock = true;
            }
            _gravityWasOn = _rb.useGravity;
            _rb.useGravity = false;
            _gravityOff = true;
            // 날아가는 쪽에 등을 대고(공격자를 보고) 날아간다
            _rb.MoveRotation(Quaternion.LookRotation(-_direction, Vector3.up));
            OnKnockdownStarted?.Invoke();
        }

        private void Update()
        {
            // 감속 요청은 MechController가 LateUpdate에서 모아 쓰므로 매 프레임 넣는다 (넉다운이 끝난 뒤 남는 속도까지 감속으로 줄지 않게)
            if (IsKnockedDown && _controller != null) _controller.RequestDamping(0f, priority: 100);
        }

        /// <summary>
        /// 기상 대기 중 이번 틱의 입력 (전투 허브가 넣는다). attack = 공격 새 입력, jump = 점프 새 입력, mash = 레버·버튼을 연타한 틱.
        /// 공격 선입력은 대기 타이머가 wakeAttackWindow(12) 미만일 때만 받는다.
        /// </summary>
        public void WakeInput(bool attack, bool jump, bool mash)
        {
            if (!IsWaitingWake) return;
            if (attack && _wake < C.wakeAttackWindow) _wakeAttack = true;
            if (jump) _wakeJump = true;
            if (mash && _wake > 0) _wake--;
        }

        /// <summary>
        /// 전투 틱마다 (전투 허브가 부른다). hitStopped = 히트스톱 중(중력·수평 이동 정지),
        /// dashHeld = 기상 대기가 끝나는 순간 대시 방향(터보 + 레버)이 들어와 있는지.
        /// </summary>
        public KnockTickResult CombatTick(bool hitStopped, bool dashHeld)
        {
            if (!IsKnockedDown || _rb == null) return KnockTickResult.None;
            if (_controller == null) OverrideDamping();
            VoCombatSpec c = C;
            if (hitStopped)
            {
                SetVelocity(0f, 0f);
                return KnockTickResult.None;
            }

            switch (Phase)
            {
                case KnockPhase.Pending:
                    SetVelocity(0f, 0f);
                    if (_frame >= _launchDelay) Launch(c);
                    else _frame++;
                    break;

                case KnockPhase.Flight:
                    TickFlight(c);
                    break;

                case KnockPhase.Slide:
                    TickLying(c);
                    break;

                case KnockPhase.GetUp:
                    SetVelocity(0f, 0f);
                    if (_rising)
                    {
                        _frame++;
                        if (_frame >= c.wakeRiseFrames)
                        {
                            Finish();
                            return KnockTickResult.Recovered;
                        }
                        break;
                    }
                    if (_wake > 0) _wake--;
                    if (_wake > 0) break;
                    // 대기 끝: 선입력에 따라 (전투 사양서 2.3 표 순서 — 공격, 대시, 점프, 없음)
                    if (_wakeAttack)
                    {
                        Finish();
                        return KnockTickResult.WakeAttack;
                    }
                    if (dashHeld)
                    {
                        Finish();
                        return KnockTickResult.WakeDash;
                    }
                    if (_wakeJump)
                    {
                        Finish();
                        return KnockTickResult.WakeJump;
                    }
                    _rising = true;
                    _frame = 0;
                    break;
            }
            return KnockTickResult.None;
        }

        private void Launch(VoCombatSpec c)
        {
            Phase = KnockPhase.Flight;
            _frame = 0;
            _landed = false;
            switch (Kind)
            {
                case DownKind.Blown:
                    _vy = c.downLaunchVy;
                    _gravity = c.downGravity;
                    _baseSpeed = c.blownSpeed;
                    break;
                case DownKind.AirDown:
                    _vy = c.airDownVy;
                    _gravity = c.downGravity;
                    _baseSpeed = c.airDownSpeed;
                    break;
                case DownKind.PileDriver:
                {
                    // 우리 넉백 궤적: flightFrames 동안 flightDistance를 등속으로, flightHeight까지 올랐다가 원래 높이로 (v0 = 4h/T, g = 8h/T²)
                    BernardTuning.KnockdownValues k = K;
                    float frames = Mathf.Max(3, k.flightFrames);
                    float height = Mathf.Max(0f, k.flightHeight) / U;
                    _vy = 4f * height / frames;
                    _gravity = 8f * height / (frames * frames);
                    _baseSpeed = Mathf.Max(0f, k.flightDistance) / U / frames;
                    break;
                }
                default:
                    _vy = c.downLaunchVy;
                    _gravity = c.downGravity;
                    _baseSpeed = c.downSpeed;
                    break;
            }
            OnLaunched?.Invoke();
        }

        /// <summary>띄움: 중력·낙하 상한, 수평 등속. 착지 + 최소 틱(다운 24, 날아감 32)이 지나면 누움. 공중 다운은 착지까지(최대 200f).</summary>
        private void TickFlight(VoCombatSpec c)
        {
            bool pile = Kind == DownKind.PileDriver;
            if (!_landed && _frame >= 2 && _vy <= 0f && IsOnGround())
            {
                _landed = true;
                _vy = 0f;
                OnLanded?.Invoke(_rb.position);
                ShakeVictimCamera(K.landShake, K.landShakeTime);
            }
            float horizontal = Horizontal(_baseSpeed, pile);
            // 이번 틱은 지금 vy로 움직이고, 중력은 그 뒤에 (진입 vy가 첫 틱에 그대로 쓰이게)
            SetVelocity(horizontal, _landed ? 0f : _vy);
            if (!_landed)
            {
                _vy -= _gravity;
                if (!pile) _vy = Mathf.Max(_vy, -c.downFallMaxVy);
            }
            _maxHeight = Mathf.Max(_maxHeight, _rb.position.y - _startHeight);
            _frame++;

            int min;
            switch (Kind)
            {
                case DownKind.Blown: min = c.blownLaunchMinFrames; break;
                case DownKind.AirDown: min = 0; break;
                case DownKind.PileDriver: min = Mathf.Max(3, K.flightFrames); break;
                default: min = c.downLaunchMinFrames; break;
            }
            bool timeout = Kind == DownKind.AirDown && _frame >= c.airDownMaxFrames;
            if ((_landed && _frame >= min) || timeout) StartLying(c);
        }

        private void StartLying(VoCombatSpec c)
        {
            if (!_landed)
            {
                _landed = true;
                OnLanded?.Invoke(_rb.position);
                ShakeVictimCamera(K.landShake, K.landShakeTime);
            }
            Phase = KnockPhase.Slide;
            _frame = 0;
            // 진입 시 작은 튐 (공중 다운은 그대로 누움)
            _vy = Kind == DownKind.AirDown ? 0f : c.lyingEntryVy;
            _landed = Kind == DownKind.AirDown;
            if (Kind == DownKind.PileDriver)
            {
                // 미끄러짐 slideDistance를 누움 틱 동안 선형으로 0까지
                float frames = Mathf.Max(1, c.lyingFrames);
                _slideStartSpeed = 2f * Mathf.Max(0f, K.slideDistance) / U / frames;
            }
        }

        /// <summary>누움: 수평 선형 감속, 중력 절반, 착지하면 튐(vy × −0.667, 작아지면 멈춤). 끝나면 기상 대기.</summary>
        private void TickLying(VoCombatSpec c)
        {
            int length = Kind == DownKind.AirDown ? c.airDownLyingFrames : c.lyingFrames;
            if (!_landed && _frame >= 2 && _vy <= 0f && IsOnGround())
            {
                // 착지: 튄다. 작아지면 멈춘다
                _vy = -_vy * c.lyingBounce;
                if (_vy < c.lyingBounceStopVy)
                {
                    _vy = 0f;
                    _landed = true;
                }
            }
            float horizontal;
            if (Kind == DownKind.AirDown) horizontal = 0f;
            else if (Kind == DownKind.PileDriver) horizontal = Horizontal(_slideStartSpeed * Mathf.Max(0f, 1f - _frame / (float)Mathf.Max(1, length)), true);
            else horizontal = Horizontal(_baseSpeed * Mathf.Max(0f, c.lyingDecelFrames - _frame) / Mathf.Max(1f, c.lyingDecelFrames), false);
            SetVelocity(horizontal, _landed ? 0f : _vy);
            if (!_landed) _vy = Mathf.Max(_vy - c.downGravity * c.lyingGravityScale, -c.downFallMaxVy);
            _frame++;
            if (_frame >= length) StartGetUp(c);
        }

        private void StartGetUp(VoCombatSpec c)
        {
            Phase = KnockPhase.GetUp;
            _frame = 0;
            _wake = c.wakeWaitFrames;
            _rising = false;
            _wakeAttack = false;
            _wakeJump = false;
            SetVelocity(0f, 0f);
            OnGetUpStarted?.Invoke();
        }

        /// <summary>
        /// 이번 틱 수평 속력(u/f): 벽에 막혔으면 0, 아니면 근접 감속(상대 200u 안 × 0.78 — 원본은 모든 이동에 건다)을 곱한 값.
        /// 파일 드라이버 궤적은 우리 값(미터로 정한 거리)이라 감속을 걸지 않는다.
        /// </summary>
        private float Horizontal(float speed, bool ownTrajectory)
        {
            if (_wallStopped || speed <= 0f) return 0f;
            if (!ownTrajectory && _melee != null) speed *= _melee.CloseSlowScale;
            float meters = speed * U;
            if (HitsObstacle(meters, K, out RaycastHit hit))
            {
                WallStop(hit, K);
                return 0f;
            }
            return speed;
        }

        private void Finish()
        {
            Vector3 travelled = _rb.position - _startPosition;
            travelled.y = 0f;
            bool wall = _wallStopped;
            float height = _maxHeight;
            SetVelocity(0f, 0f);
            RestoreBody();
            Phase = KnockPhase.None;
            if (debugLog)
            {
                Debug.Log($"[KnockdownController] {name}: {Kind} 수평 {travelled.magnitude:0.00}m, 최고 높이 {height:0.00}m, 벽 {wall}", this);
            }
            OnRecovered?.Invoke(travelled.magnitude, height, wall);
        }

        /// <summary>라운드 리셋·꺼짐: 중력·감속·잠금을 되돌리고 조용히 끝낸다 (이벤트 없음).</summary>
        public void ResetForRound()
        {
            RestoreBody();
            Phase = KnockPhase.None;
            _frame = 0;
        }

        private void OnDisable() => ResetForRound();

        private void RestoreBody()
        {
            if (_gravityOff && _rb != null) _rb.useGravity = _gravityWasOn;
            _gravityOff = false;
            if (_dampingOverridden && _rb != null) _rb.linearDamping = _savedDamping;
            _dampingOverridden = false;
            if (_holdingLock && _lock != null) _lock.Release();
            _holdingLock = false;
        }

        /// <summary>MechController가 없는 기체(연습 봇)는 감속을 직접 0으로 (끝나면 되돌림).</summary>
        private void OverrideDamping()
        {
            if (_dampingOverridden) return;
            _savedDamping = _rb.linearDamping;
            _rb.linearDamping = 0f;
            _dampingOverridden = true;
        }

        /// <summary>이번 틱 속도를 넣는다 (수평·수직 u/f). 감속 없이 그대로 적분되게.</summary>
        private void SetVelocity(float horizontal, float vertical)
        {
            float scale = SpeedScale;
            MotionBridge.SetVelocity(_rb, _direction * (horizontal * scale) + Vector3.up * (vertical * scale), undamped: true);
        }

        /// <summary>발밑에 땅이 있는지. 이동기가 있으면 이동기의 접지(지난 틱 적분 결과), 없으면(연습 봇) 발밑으로 짧게 쏜 레이.</summary>
        private bool IsOnGround()
        {
            VoMotor motor = VoMotor.Of(_rb);
            if (motor != null && motor.IsActive) return motor.IsGrounded;
            Collider body = GetComponent<Collider>();
            float feet = body != null ? body.bounds.min.y : _rb.position.y;
            float fall = Mathf.Max(0f, -_vy) * U;
            Vector3 origin = new Vector3(_rb.position.x, feet + 0.3f, _rb.position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.3f + 0.08f + fall, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.attachedRigidbody == _rb || hit.collider.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        /// <summary>이번 틱에 distance만큼 수평으로 가면 벽·다른 콜라이더(자기 몸·트리거·바닥 제외)에 닿는지.</summary>
        private bool HitsObstacle(float distance, BernardTuning.KnockdownValues k, out RaycastHit nearest)
        {
            nearest = default;
            if (distance <= 0f) return false;
            float radius = BodyRadius() * k.wallCheckRadiusScale;
            RaycastHit[] hits = Physics.SphereCastAll(_rb.position, radius, _direction, distance + k.wallCheckSkin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.attachedRigidbody == _rb || hit.collider.transform.IsChildOf(transform)) continue;
                // 바닥·완만한 경사는 벽이 아니다
                if (Mathf.Abs(hit.normal.y) > 0.7f) continue;
                // 시작부터 겹쳐 있으면(distance 0) 이미 붙어 있는 것 — 날아가는 쪽으로 막고 있을 때만
                if (hit.distance <= 0f && Vector3.Dot(hit.collider.bounds.center - _rb.position, _direction) <= 0f) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return best < float.MaxValue;
        }

        private float BodyRadius()
        {
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) return capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            Collider col = GetComponent<Collider>();
            return col != null ? Mathf.Min(col.bounds.extents.x, col.bounds.extents.z) : 0.5f;
        }

        private void WallStop(RaycastHit hit, BernardTuning.KnockdownValues k)
        {
            _wallStopped = true;
            ShakeVictimCamera(k.wallShake, k.wallShakeTime);
            OnWallHit?.Invoke(hit.point);
        }

        private void ShakeVictimCamera(float amplitude, float duration)
        {
            MechIdentity identity = GetComponent<MechIdentity>();
            MechCamera mechCamera = identity != null ? identity.MechCamera : null;
            if (mechCamera != null) mechCamera.AddShake(amplitude, duration);
        }
    }
}
