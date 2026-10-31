using System;
using UnityEngine;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.Combat
{
    /// <summary>
    /// 넉다운 (R1): 파일 드라이버에 맞은 기체를 히트스톱이 끝난 뒤 공격자 상체 방향으로 날린다.
    /// 대기(히트스톱) → 비행(포물선) → 착지 미끄러짐 → 기상(무적) → 끝.
    /// 비행·미끄러짐은 물리 스텝마다 속도를 직접 넣어(중력 끄고 포물선 속도) 렌더 fps와 상관없이 같은 거리·높이가 나온다.
    /// 그동안 ExternalControlLock을 잡아 이동 로직(상태 머신·연습 봇)이 속도를 덮어쓰지 않게 하고, 감속(damping)은 0으로 요청한다.
    /// 벽·다른 콜라이더에 부딪히면 수평 이동을 그 자리에서 멈추고 큰 화면 흔들림(추가 피해 없음).
    /// 기체마다 하나 — MeleeSystem이 없으면 붙인다(GetOrAdd).
    /// </summary>
    public class KnockdownController : MonoBehaviour
    {
        public enum KnockPhase { None, Pending, Flight, Slide, GetUp }

        // 물리 스텝을 더한 시간의 부동소수점 오차로 스텝이 하나 더 도는 것을 막는 여유(초)
        private const float PhaseEpsilon = 0.0001f;

        [Header("디버그")]
        [Tooltip("넉다운이 끝날 때 수평 이동 거리·최고 높이·벽 충돌을 콘솔에 출력 (30fps 비교용)")]
        [SerializeField] private bool debugLog;

        private TuningRef _tuning;
        private BernardTuning.KnockdownValues K => _tuning.Get(this).Knockdown;

        public KnockPhase Phase { get; private set; } = KnockPhase.None;
        public bool IsKnockedDown => Phase != KnockPhase.None;
        /// <summary>기상 중 invulnerableTime 동안 판정 무시.</summary>
        public bool IsInvulnerable => Phase == KnockPhase.GetUp && _phaseTime < K.invulnerableTime;
        /// <summary>지금 단계 경과(초, 물리 시간).</summary>
        public float PhaseTime => _phaseTime;
        /// <summary>날아가는 수평 방향(월드).</summary>
        public Vector3 Direction => _direction;

        /// <summary>파일 드라이버에 맞아 넉다운이 정해진 순간 (아직 히트스톱 대기 중).</summary>
        public event Action OnKnockdownStarted;
        /// <summary>날아오르는 순간 (히트스톱 뒤).</summary>
        public event Action OnLaunched;
        /// <summary>착지 순간 (위치).</summary>
        public event Action<Vector3> OnLanded;
        /// <summary>벽·다른 콜라이더에 부딪혀 멈춘 순간 (부딪힌 지점).</summary>
        public event Action<Vector3> OnWallHit;
        /// <summary>기상 시작 (무적 시작).</summary>
        public event Action OnGetUpStarted;
        /// <summary>넉다운 끝. 인자: 수평 이동 거리(m), 최고 높이(m), 벽에 부딪혔는지.</summary>
        public event Action<float, float, bool> OnRecovered;

        private Rigidbody _rb;
        private MechController _controller;
        private ExternalControlLock _lock;
        private bool _holdingLock;
        private bool _gravityWasOn;
        private bool _gravityOff;
        private float _savedDamping;
        private bool _dampingOverridden;

        private Vector3 _direction;
        private float _phaseTime;
        private float _launchDelay;
        private float _horizontalSpeed;
        private float _verticalSpeed;
        private float _gravity;
        private float _slideStartSpeed;
        private bool _wallStopped;
        private Vector3 _startPosition;
        private float _startHeight;
        private float _maxHeight;

        public static KnockdownController GetOrAdd(GameObject mech)
        {
            KnockdownController knockdown = mech.GetComponent<KnockdownController>();
            return knockdown != null ? knockdown : mech.AddComponent<KnockdownController>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _controller = GetComponent<MechController>();
        }

        /// <summary>
        /// 넉다운 시작. direction = 날아갈 수평 방향(공격자 상체 정면), launchDelay = 날아오르기 전 대기(게임 시간 —
        /// 히트스톱 동안은 물리 스텝이 없어 그 뒤로 밀린다). 이미 넉다운 중이면 처음부터 다시.
        /// </summary>
        public void Begin(Vector3 direction, float launchDelay)
        {
            if (_rb == null) return;
            direction.y = 0f;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            RestoreBody();
            Phase = KnockPhase.Pending;
            _phaseTime = 0f;
            _launchDelay = Mathf.Max(0f, launchDelay);
            _wallStopped = false;
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
            // 날아가는 쪽에 등을 대고(공격자를 보고) 날아간다
            _rb.MoveRotation(Quaternion.LookRotation(-_direction, Vector3.up));
            OnKnockdownStarted?.Invoke();
        }

        private void Update()
        {
            // 감속 요청은 MechController가 LateUpdate에서 모아 쓰므로 매 프레임 넣는다 (넉다운 중 거리가 감속으로 줄지 않게)
            if (IsKnockedDown && _controller != null) _controller.RequestDamping(0f, priority: 100);
        }

        private void FixedUpdate()
        {
            if (!IsKnockedDown || _rb == null) return;
            BernardTuning.KnockdownValues k = K;
            float dt = Time.fixedDeltaTime;
            if (_controller == null) OverrideDamping();

            switch (Phase)
            {
                case KnockPhase.Pending:
                    // 히트스톱(timeScale 0) 동안은 이 함수가 아예 안 불리니 대기가 히트스톱 뒤로 밀린다
                    SetHorizontalVelocity(Vector3.zero);
                    _phaseTime += dt;
                    if (_phaseTime >= _launchDelay - PhaseEpsilon) Launch(k);
                    break;

                case KnockPhase.Flight:
                {
                    // 이번 스텝 가운데 시각의 수직 속도 → 스텝마다 포물선 위치가 정확히 맞는다 (fps 무관)
                    float vy = _verticalSpeed - _gravity * (_phaseTime + dt * 0.5f);
                    float horizontal = _wallStopped ? 0f : _horizontalSpeed;
                    if (!_wallStopped && HitsObstacle(horizontal * dt, k, out RaycastHit hit)) WallStop(hit, k);
                    horizontal = _wallStopped ? 0f : horizontal;
                    MotionBridge.SetVelocity(_rb, _direction * horizontal + Vector3.up * vy);
                    _phaseTime += dt;
                    _maxHeight = Mathf.Max(_maxHeight, _rb.position.y - _startHeight);
                    if (_phaseTime >= k.flightTime - PhaseEpsilon) Land(k);
                    break;
                }

                case KnockPhase.Slide:
                {
                    float speed = _wallStopped ? 0f : _slideStartSpeed * Mathf.Max(0f, 1f - (_phaseTime + dt * 0.5f) / Mathf.Max(0.01f, k.slideTime));
                    if (speed > 0f && HitsObstacle(speed * dt, k, out RaycastHit hit))
                    {
                        WallStop(hit, k);
                        speed = 0f;
                    }
                    SetHorizontalVelocity(_direction * speed);
                    _phaseTime += dt;
                    if (_phaseTime >= k.slideTime - PhaseEpsilon) StartGetUp();
                    break;
                }

                case KnockPhase.GetUp:
                    SetHorizontalVelocity(Vector3.zero);
                    _phaseTime += dt;
                    if (_phaseTime >= Mathf.Max(k.getUpTime, k.invulnerableTime) - PhaseEpsilon) Finish();
                    break;
            }
        }

        private void Launch(BernardTuning.KnockdownValues k)
        {
            Phase = KnockPhase.Flight;
            _phaseTime = 0f;
            float time = Mathf.Max(0.05f, k.flightTime);
            float height = Mathf.Max(0f, k.flightHeight);
            // 수평 등속, 수직은 time에 원래 높이로 돌아오는 포물선: v0 = 4h/T, g = 8h/T²
            _horizontalSpeed = Mathf.Max(0f, k.flightDistance) / time;
            _verticalSpeed = 4f * height / time;
            _gravity = 8f * height / (time * time);
            _gravityWasOn = _rb.useGravity;
            _rb.useGravity = false;
            _gravityOff = true;
            OnLaunched?.Invoke();
        }

        private void Land(BernardTuning.KnockdownValues k)
        {
            RestoreGravity();
            Vector3 velocity = MotionBridge.GetVelocity(_rb);
            MotionBridge.SetVelocity(_rb, new Vector3(velocity.x, 0f, velocity.z));
            Phase = KnockPhase.Slide;
            _phaseTime = 0f;
            // 미끄러짐: 선형으로 0까지 줄며 slideDistance만큼 (벽에 막혔으면 0)
            _slideStartSpeed = _wallStopped ? 0f : 2f * Mathf.Max(0f, k.slideDistance) / Mathf.Max(0.01f, k.slideTime);
            ShakeVictimCamera(k.landShake, k.landShakeTime);
            OnLanded?.Invoke(_rb.position);
        }

        private void StartGetUp()
        {
            Phase = KnockPhase.GetUp;
            _phaseTime = 0f;
            OnGetUpStarted?.Invoke();
        }

        private void Finish()
        {
            Vector3 travelled = _rb.position - _startPosition;
            travelled.y = 0f;
            bool wall = _wallStopped;
            float height = _maxHeight;
            RestoreBody();
            Phase = KnockPhase.None;
            if (debugLog)
            {
                Debug.Log($"[KnockdownController] {name}: 수평 {travelled.magnitude:0.00}m, 최고 높이 {height:0.00}m, 벽 {wall}, fixedDeltaTime {Time.fixedDeltaTime}", this);
            }
            OnRecovered?.Invoke(travelled.magnitude, height, wall);
        }

        /// <summary>라운드 리셋·꺼짐: 중력·감속·잠금을 되돌리고 조용히 끝낸다 (이벤트 없음).</summary>
        public void ResetForRound()
        {
            RestoreBody();
            Phase = KnockPhase.None;
            _phaseTime = 0f;
        }

        private void OnDisable() => ResetForRound();

        private void RestoreBody()
        {
            RestoreGravity();
            if (_dampingOverridden && _rb != null) _rb.linearDamping = _savedDamping;
            _dampingOverridden = false;
            if (_holdingLock && _lock != null) _lock.Release();
            _holdingLock = false;
        }

        private void RestoreGravity()
        {
            if (_gravityOff && _rb != null) _rb.useGravity = _gravityWasOn;
            _gravityOff = false;
        }

        /// <summary>MechController가 없는 기체(연습 봇)는 감속을 직접 0으로 (끝나면 되돌림).</summary>
        private void OverrideDamping()
        {
            if (_dampingOverridden) return;
            _savedDamping = _rb.linearDamping;
            _rb.linearDamping = 0f;
            _dampingOverridden = true;
        }

        private void SetHorizontalVelocity(Vector3 horizontal)
        {
            MotionBridge.SetHorizontalVelocity(_rb, horizontal);
        }

        /// <summary>이번 스텝에 distance만큼 수평으로 가면 벽·다른 콜라이더(자기 몸·트리거·바닥 제외)에 닿는지.</summary>
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
            SetHorizontalVelocity(Vector3.zero);
            ShakeVictimCamera(k.wallShake, k.wallShakeTime);
            OnWallHit?.Invoke(hit.point);
        }

        private void ShakeVictimCamera(float amplitude, float duration)
        {
            MechIdentity identity = GetComponent<MechIdentity>();
            MyDongari.Camera.MechCamera mechCamera = identity != null ? identity.MechCamera : null;
            if (mechCamera != null) mechCamera.AddShake(amplitude, duration);
        }
    }
}
