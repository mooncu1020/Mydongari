using UnityEngine;
using MyDongari.Input;
using MyDongari.Movement;
using MyDongari.Movement.V8;
using MyDongari.Visual;
using MyDongari.Mech;
using MyDongari.UI;

namespace MyDongari.Camera
{
    /// <summary>
    /// 몸 뒤 고정 백뷰 (V3 "카메라와 시점"). 시점만 따로 돌리는 조작은 없다 — 카메라는 몸의 수평 회전을 부드럽게 따라간다.
    /// 몸을 돌리는 건 R스틱(MechStateMachine)과 하드락 몸통 회전(MechController)이 맡는다.
    /// V8 이동기(사양서 5장): VoTickDriver가 틱 맨 앞에서 TickV8로 갱신(지난 틱 기체 상태 기준)하고, 그릴 때 두 틱 사이를 보간한다.
    /// 기체 뒤 거리 = 78u(키 × 3.54), 피치 8.4° 아래, 시선 표적 = 발 + 키/2 + 6u, 기준점이 매 틱 12%씩 기체를 따라감, 세로 FOV 35°.
    /// 방위는 이동기의 카메라 방위(기본 모드에선 몸에 강체로 붙음) + 선회 리드. 수치는 BernardTuning → V8 이동기 → 카메라.
    /// 예전 이동기일 때는 V3 고정 백뷰(뒤 followDistance, 위 followHeight). V6d 화면 구도 계산은 V8에서 지웠다.
    /// </summary>
    public class MechCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("예전 이동기 백뷰 (V3) — V8 카메라 수치는 BernardTuning → V8 이동기")]
        [Tooltip("기체 뒤로 떨어지는 거리(m)")]
        [SerializeField] private float followDistance = 6f;
        [Tooltip("기체 루트 위로 올라가는 높이(m)")]
        [SerializeField] private float followHeight = 2.5f;
        [Tooltip("카메라가 바라보는 점: 기체 루트 위 높이(m). 카메라 충돌 검사 시작점도 여기")]
        [SerializeField] private float lookHeight = 1.5f;
        [Tooltip("카메라가 바라보는 점: 기체 앞 거리(m)")]
        [SerializeField] private float lookAhead = 8f;
        [Tooltip("몸의 수평 회전을 따라가는 빠르기(지수 감쇠, 1/초). 클수록 딱 붙고 작을수록 늦게 돈다")]
        [SerializeField] private float yawFollowSpeed = 8f;

        [Header("위치 따라가기 (V3.1 대시 처짐)")]
        [Tooltip("평소 카메라 기준점이 기체 위치를 따라가는 빠르기(지수 감쇠, 1/초). 클수록 딱 붙는다 (20m/s에서 처짐 ≈ 20 / 값 m)")]
        [SerializeField] private float positionFollowSpeed = 100f;
        [Tooltip("대시 중 따라가는 빠르기. 작을수록 기체가 화면 앞·옆으로 더 처져 속도감이 난다 (30m/s에서 처짐 ≈ 30 / 값 m)")]
        [SerializeField] private float dashPositionFollowSpeed = 12f;
        [Tooltip("두 빠르기 사이를 바꾸는 속도(초당 변화량). 대시가 끝날 때 처짐이 한 번에 튀어 돌아오지 않게")]
        [SerializeField] private float positionFollowBlendRate = 200f;

        [Header("카메라 충돌 (T4)")]
        [Tooltip("카메라가 바위·테두리·벽 등 정적 지형을 뚫으면 기체 쪽으로 당긴다 (기체·리지드바디가 있는 콜라이더는 무시)")]
        [SerializeField] private bool collideWithWorld = true;
        [Tooltip("카메라를 감싸는 구 반지름(m). 근접 클리핑(0.3)보다 조금 작게")]
        [SerializeField] private float collisionRadius = 0.25f;
        [Tooltip("막힌 면에서 이만큼(m) 더 앞으로")]
        [SerializeField] private float collisionMargin = 0.05f;
        [Tooltip("충돌을 검사할 레이어")]
        [SerializeField] private LayerMask collisionLayers = ~0;

        [Header("FOV 설정")]
        [Tooltip("기본 세로 화각(°). V6.1: 70 → 52 (템진 구도 — 피치 약 10°). 이름을 바꿔 씬에 저장된 예전 값 70을 안 읽는다")]
        [SerializeField] private float baseFieldOfView = 52f;
        [SerializeField] private float sprintFov = 90f;
        [SerializeField] private float fovLerpSpeed = 5f;

        [Header("화면 밖 상대 화살표")]
        [Tooltip("켜면 시작할 때 이 카메라에 OffscreenTargetArrow를 붙인다 (이미 있으면 그대로 쓴다)")]
        [SerializeField] private bool addOffscreenArrow = true;

        private float _yaw = 0f;
        // 카메라 기준점(기체 위치를 늦게 따라감)과 지금 따라가는 빠르기
        private Vector3 _followPosition;
        private float _currentFollowSpeed;
        private InputReader _inputReader;
        private MechController _mechController;
        private UnityEngine.Camera _camera;
        // 약한 흔들림 (발소리 등). 남은 시간이 줄수록 작아진다
        private float _shakeAmplitude;
        private float _shakeDuration;
        private float _shakeTimer;
        private MechVisualRig _rig;

        /// <summary>이 카메라가 비추는 기체.</summary>
        public Transform Target => target;

        /// <summary>약한 카메라 흔들림. 지금 흔들림보다 크면 덮어쓴다.</summary>
        public void AddShake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f) return;
            float current = _shakeDuration > 0f ? _shakeAmplitude * (_shakeTimer / _shakeDuration) : 0f;
            if (amplitude < current) return;
            _shakeAmplitude = amplitude;
            _shakeDuration = duration;
            _shakeTimer = duration;
        }

        // 방향 흔들림 (N1): 맞은 쪽 화면이 타격 방향으로 밀렸다가 줄어들며 출렁인다. 히트스톱 중에도 보이게 실제 시간
        private Vector3 _kickDirection;
        private float _kickAmplitude;
        private float _kickDuration;
        private float _kickTimer;
        private float _kickFrequency;

        /// <summary>worldDirection 쪽으로 밀렸다 돌아오는 흔들림. 지금 것보다 크면 덮어쓴다.</summary>
        public void AddDirectionalShake(Vector3 worldDirection, float amplitude, float duration, float frequency)
        {
            if (amplitude <= 0f || duration <= 0f || worldDirection.sqrMagnitude < 0.0001f) return;
            float current = _kickDuration > 0f ? _kickAmplitude * (_kickTimer / _kickDuration) : 0f;
            if (amplitude < current) return;
            _kickDirection = worldDirection.normalized;
            _kickAmplitude = amplitude;
            _kickDuration = duration;
            _kickTimer = duration;
            _kickFrequency = frequency;
        }

        private static readonly RaycastHit[] CollisionHits = new RaycastHit[16];

        /// <summary>
        /// 시선이 닿는 점(기체 위, 콜라이더 안쪽이거나 바로 위라 안전)에서 원하는 카메라 위치까지 구를 쏴서, 정적 지형에 먼저 닿으면 그 앞으로 당긴다.
        /// 기체(리지드바디가 있는 콜라이더)와 트리거는 무시 — 상대가 카메라 앞을 지나가도 화면이 튀지 않게.
        /// </summary>
        private Vector3 ResolveCollision(Vector3 origin, Vector3 desired)
        {
            Vector3 toCamera = desired - origin;
            float distance = toCamera.magnitude;
            if (distance < 0.001f) return desired;
            Vector3 direction = toCamera / distance;
            int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, CollisionHits, distance, collisionLayers, QueryTriggerInteraction.Ignore);
            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = CollisionHits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                // 시작부터 겹친 것(distance 0)은 기체가 이미 그 안에 있는 경우라 밀어낼 방향이 없다 — 무시
                if (hit.distance <= 0f) continue;
                if (hit.distance < nearest) nearest = hit.distance;
            }
            return nearest < distance ? origin + direction * Mathf.Max(0f, nearest - collisionMargin) : desired;
        }

        private void OnFootstep(bool left, float intensity)
        {
            if (_mechController == null) return;
            BernardTuning.VisualValues v = _mechController.Tuning.Visual;
            AddShake(v.footstepShake * intensity, v.footstepShakeTime);
        }

        private void OnDestroy()
        {
            if (_rig != null) _rig.OnFootstep -= OnFootstep;
        }

        /// <summary>
        /// 라운드 시작: 흔들림을 끊고 시점을 스폰 방향으로 바로 맞춘다 (따라가기 감쇠 없이).
        /// </summary>
        public void ResetView(float yaw)
        {
            _shakeTimer = 0f;
            _kickTimer = 0f;
            _yaw = yaw;
            if (target != null) _followPosition = target.position;
            _currentFollowSpeed = positionFollowSpeed;
            _v8Ready = false;
            if (_camera != null) _camera.fieldOfView = baseFieldOfView;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (target != null)
            {
                _inputReader = target.GetComponent<InputReader>();
                _mechController = target.GetComponent<MechController>();
                // 내 기체 발소리에만 흔들린다 (화면 분할이라 상대 발소리는 상대 카메라 몫)
                _rig = target.GetComponent<MechVisualRig>();
                if (_rig != null) _rig.OnFootstep += OnFootstep;
                _yaw = target.eulerAngles.y;
                _followPosition = target.position;
            }
            _currentFollowSpeed = positionFollowSpeed;

            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera != null)
            {
                _camera.fieldOfView = baseFieldOfView;
            }

            // 화면 분할 두 화면 모두에 나오도록 카메라마다 하나씩. 씬 배선 없이 코드로 붙인다
            if (addOffscreenArrow && GetComponent<OffscreenTargetArrow>() == null)
            {
                gameObject.AddComponent<OffscreenTargetArrow>();
            }
        }

        private void LateUpdate()
        {
            if (target == null || _inputReader == null) return;
            if (_mechController != null && _mechController.IsV8)
            {
                LateUpdateV8();
                return;
            }

            if (InputTrace.ShouldLog(target, "6 MechCamera"))
            {
                InputTrace.Log("6 MechCamera", $"{name} target={target.name} reader={_inputReader.gameObject.name} " +
                    $"move={_inputReader.MoveInput} look={_inputReader.LookInput}");
            }

            // 몸의 수평 회전을 지수 감쇠로 따라간다 (보간된 트랜스폼이라 물리 스텝 계단이 안 보인다)
            float bodyYaw = target.eulerAngles.y;
            float k = 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime);
            // V5 점프 락온: 몸이 0.2초에 걸쳐 상대 쪽으로 도는 동안 카메라는 몸에 바로 붙어 같이 돈다
            bool jumpTurning = _mechController != null && _mechController.StateMachine != null && _mechController.StateMachine.IsJumpTurning;
            _yaw = jumpTurning ? bodyYaw : Mathf.LerpAngle(_yaw, bodyYaw, k);

            // 기준점은 기체 위치를 늦게 따라간다: 대시 중엔 느리게 따라가 기체가 옆·앞으로 처진다 (V3.1). 몸 방향은 대시 중 안 돈다
            bool dashing = _mechController != null && _mechController.StateMachine != null && _mechController.StateMachine.IsDashing;
            float wantedSpeed = dashing ? dashPositionFollowSpeed : positionFollowSpeed;
            _currentFollowSpeed = Mathf.MoveTowards(_currentFollowSpeed, wantedSpeed, positionFollowBlendRate * Time.deltaTime);
            float kp = 1f - Mathf.Exp(-_currentFollowSpeed * Time.deltaTime);
            _followPosition = Vector3.Lerp(_followPosition, target.position, kp);
            Vector3 anchor = _followPosition;

            // V3 고정 백뷰 (예전 이동기)
            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 lookPoint = anchor + Vector3.up * lookHeight + yawRotation * (Vector3.forward * lookAhead);
            Vector3 desired = anchor + Vector3.up * followHeight - yawRotation * (Vector3.forward * followDistance);
            Vector3 toLook = lookPoint - desired;
            transform.position = desired;
            transform.rotation = toLook.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toLook, Vector3.up) : yawRotation;
            ApplyShakeAndCollision();
            UpdateFov();
        }

        /// <summary>흔들림·방향 흔들림을 더하고, 지형을 뚫으면 기체 쪽으로 당긴다 (두 이동기 공통).</summary>
        private void ApplyShakeAndCollision()
        {
            if (_shakeTimer > 0f)
            {
                transform.position += UnityEngine.Random.insideUnitSphere * (_shakeAmplitude * (_shakeTimer / _shakeDuration));
                _shakeTimer -= Time.deltaTime;
            }
            if (_kickTimer > 0f)
            {
                float elapsed = _kickDuration - _kickTimer;
                float decay = _kickTimer / _kickDuration;
                transform.position += _kickDirection * (_kickAmplitude * decay * Mathf.Cos(elapsed * _kickFrequency * 2f * Mathf.PI));
                _kickTimer -= Time.unscaledDeltaTime;
            }
            // 흔들림까지 더한 최종 위치가 지형을 뚫으면 기체 쪽으로 당긴다 (T4). 시작점은 기체 위 시선 높이
            if (collideWithWorld) transform.position = ResolveCollision(target.position + Vector3.up * lookHeight, transform.position);
        }

        // ---- V8 카메라 (사양서 5장) ----
        private bool _v8Ready;
        private Vector3 _v8Anchor;
        private Vector3 _v8PrevPosition, _v8Position;
        private Quaternion _v8PrevRotation = Quaternion.identity, _v8Rotation = Quaternion.identity;

        /// <summary>
        /// V8 틱 맨 앞(VoTickDriver): 지난 틱 기체 상태로 카메라를 한 틱 갱신한다. 기준점(시선 표적)은 매 틱 차이의 12%씩 기체를 따라가고,
        /// 방위는 이동기의 카메라 방위 + 선회 리드, 피치 8.4° 아래, 시선 표적에서 78u 뒤.
        /// </summary>
        public void TickV8(MechMotor motor)
        {
            if (motor == null) return;
            BernardTuning.V8Values v = motor.V;
            Vector3 targetPoint = new Vector3(motor.Position.x,
                motor.FeetY + motor.BodyHeight * v.cameraBaseHeightRatio + v.cameraTargetUp * motor.U,
                motor.Position.z);
            if (!_v8Ready)
            {
                _v8Anchor = targetPoint;
            }
            else
            {
                _v8Anchor += (targetPoint - _v8Anchor) * v.cameraFollow;
            }
            float yaw = motor.CameraYaw + motor.CameraLead;
            float pitch = BernardTuning.AngleUnitsToDegrees(v.cameraPitch);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = _v8Anchor - rotation * (Vector3.forward * (v.cameraDistance * motor.U));

            _v8PrevPosition = _v8Ready ? _v8Position : position;
            _v8PrevRotation = _v8Ready ? _v8Rotation : rotation;
            _v8Position = position;
            _v8Rotation = rotation;
            _v8Ready = true;
            _yaw = yaw;
        }

        /// <summary>두 틱 사이를 보간해 그린다 (리지드바디 보간과 같은 비율).</summary>
        private void LateUpdateV8()
        {
            if (!_v8Ready) return;
            float alpha = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            transform.position = Vector3.Lerp(_v8PrevPosition, _v8Position, alpha);
            transform.rotation = Quaternion.Slerp(_v8PrevRotation, _v8Rotation, alpha);
            ApplyShakeAndCollision();
            if (_camera != null) _camera.fieldOfView = _mechController.Tuning.V8.cameraFov;
        }

        private void UpdateFov()
        {
            if (_camera == null || _mechController == null) return;

            float speed = _mechController.Velocity.magnitude;
            float maxSpeed = _mechController.DashSpeed;
            float t = maxSpeed > 0.0001f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
            float targetFov = Mathf.Lerp(baseFieldOfView, sprintFov, t);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}
