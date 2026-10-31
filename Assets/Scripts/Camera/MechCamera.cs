using UnityEngine;
using MyDongari.Input;
using MyDongari.Movement;
using MyDongari.Visual;
using MyDongari.Mech;
using MyDongari.UI;

namespace MyDongari.Camera
{
    /// <summary>
    /// 몸 뒤 고정 백뷰 (V3 "카메라와 시점"). 시점만 따로 돌리는 조작은 없다 — 카메라는 몸의 수평 회전을 부드럽게 따라간다.
    /// 몸을 돌리는 건 R스틱(MechStateMachine)과 하드락 몸통 회전(MechController)이 맡는다.
    /// 위치 (V6d): 거리 숫자 대신 화면 구도(템진 기준)로 정한다 — 서 있는 기체가 화면 높이의 screenHeightFraction(45%),
    /// 머리가 headScreenY(세로 가운데), 평지 지평선이 horizonScreenY(위에서 32%), 기체는 가로 가운데.
    /// 기본 FOV(baseFieldOfView, 세로 화각)로 피치·거리·높이를 계산한다. Unity의 FOV는 세로 기준이라 화면 비율(화면 분할 반쪽 포함)이 달라도
    /// 세로 구도는 같고, 가로로 보이는 폭만 달라진다. 대시 중 FOV가 넓어져도 카메라 위치는 그대로(기본 FOV 기준).
    /// </summary>
    public class MechCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("화면 구도 (V6d: 템진 기준 — 위치·피치는 이 값과 baseFieldOfView로 계산)")]
        [Tooltip("서 있는 기체 키(m, 발바닥 ~ 머리 꼭대기). 버나드 2.6m. 모델 실제 키와 다르면 구도가 그 비율만큼 어긋난다 (debugLog로 렌더러 높이 확인)")]
        [SerializeField] private float mechHeight = 2.6f;
        [Tooltip("서 있는 기체가 차지하는 화면 높이 비율 (0.45 = 45%)")]
        [Range(0.05f, 0.95f)] [SerializeField] private float screenHeightFraction = 0.45f;
        [Tooltip("머리 꼭대기의 화면 세로 위치 (위 0 ~ 아래 1). 0.5 = 세로 가운데. 발은 이 값 + 화면 높이 비율")]
        [Range(0f, 1f)] [SerializeField] private float headScreenY = 0.5f;
        [Tooltip("평지 지평선의 화면 세로 위치 (위 0 ~ 아래 1). 0.32 = 위에서 32%. 이 값이 내려다보는 각도(피치)를 정한다")]
        [Range(0.01f, 0.99f)] [SerializeField] private float horizonScreenY = 0.32f;
        [Tooltip("카메라 충돌 검사 시작점: 기체 루트 위 높이(m)")]
        [SerializeField] private float lookHeight = 1.5f;
        [Tooltip("켜면 시작할 때 계산한 피치·거리·높이와 렌더러로 잰 기체 키를 콘솔에 한 번 찍는다")]
        [SerializeField] private bool debugLog = false;
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
        // 기체 루트 기준 발바닥 높이(m). 시작할 때 콜라이더 바닥으로 잰다 (캡슐 높이 2, 중심 = 루트라 보통 -1)
        private float _footOffset = -1f;
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
                Collider body = target.GetComponent<Collider>();
                if (body != null) _footOffset = body.bounds.min.y - target.position.y;
            }
            _currentFollowSpeed = positionFollowSpeed;

            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera != null)
            {
                _camera.fieldOfView = baseFieldOfView;
            }

            if (debugLog) LogComposition();

            // 화면 분할 두 화면 모두에 나오도록 카메라마다 하나씩. 씬 배선 없이 코드로 붙인다
            if (addOffscreenArrow && GetComponent<OffscreenTargetArrow>() == null)
            {
                gameObject.AddComponent<OffscreenTargetArrow>();
            }
        }

        private void LateUpdate()
        {
            if (target == null || _inputReader == null) return;

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

            // V6d 구도: 발바닥 기준 높이·뒤 거리·피치 (기본 FOV로 계산 — 대시 FOV 변화에 위치가 출렁이지 않게)
            ComputeComposition(out float pitch, out float distance, out float height);
            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 feet = anchor + Vector3.up * _footOffset;
            transform.position = feet + Vector3.up * height - yawRotation * (Vector3.forward * distance);
            transform.rotation = Quaternion.Euler(pitch, _yaw, 0f);
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

            UpdateFov();
        }

        /// <summary>
        /// 화면 구도에서 카메라 피치(°, 아래로 +)·수평 거리(m)·발바닥 위 높이(m)를 계산한다.
        /// 세로 화각 반각을 h라 하면, 고도각 α(위로 +)인 점의 화면 세로 좌표(가운데 0, 위 끝 +1)는 tan(α + 피치) / tan h.
        /// 지평선(α = 0)이 horizonScreenY에 오게 피치를 정하고, 머리·발이 정한 위치에 오는 고도각에서 거리·높이를 푼다:
        /// tan α머리 = (키 − 높이) / 거리, tan α발 = −높이 / 거리.
        /// </summary>
        private void ComputeComposition(out float pitch, out float distance, out float height)
        {
            float tanHalf = Mathf.Tan(Mathf.Clamp(baseFieldOfView, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            float headNdc = 1f - 2f * headScreenY;
            float feetNdc = headNdc - 2f * screenHeightFraction;
            float horizonNdc = 1f - 2f * horizonScreenY;
            float pitchRad = Mathf.Atan(horizonNdc * tanHalf);
            float tanHead = Mathf.Tan(Mathf.Atan(headNdc * tanHalf) - pitchRad);
            float tanFeet = Mathf.Tan(Mathf.Atan(feetNdc * tanHalf) - pitchRad);
            float span = tanHead - tanFeet;
            // 비율이 말이 안 되면(머리가 발보다 아래) 거리 0이 되지 않게 최소 간격
            distance = mechHeight / Mathf.Max(0.01f, span);
            height = -distance * tanFeet;
            pitch = pitchRad * Mathf.Rad2Deg;
        }

        private void LogComposition()
        {
            ComputeComposition(out float pitch, out float distance, out float height);
            float measured = -1f;
            Renderer[] renderers = target != null ? target.GetComponentsInChildren<Renderer>() : new Renderer[0];
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                measured = Mathf.Max(measured, r.bounds.max.y - (target.position.y + _footOffset));
            }
            Debug.Log($"[MechCamera] {name} 구도: FOV {baseFieldOfView:0.#}° → 피치 {pitch:0.00}° 아래, 뒤 거리 {distance:0.000}m, " +
                      $"발바닥 위 높이 {height:0.000}m (루트 위 {height + _footOffset:0.000}m), 기체 키 설정 {mechHeight:0.##}m / 렌더러로 잰 키 {measured:0.00}m", this);
        }

        private void UpdateFov()
        {
            if (_camera == null || _mechController == null) return;

            float speed = _mechController.Rb.linearVelocity.magnitude;
            float maxSpeed = _mechController.DashSpeed;
            float t = maxSpeed > 0.0001f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
            float targetFov = Mathf.Lerp(baseFieldOfView, sprintFov, t);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}
