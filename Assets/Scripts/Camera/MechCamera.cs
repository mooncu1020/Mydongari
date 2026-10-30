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
    /// </summary>
    public class MechCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("백뷰 위치 (V3)")]
        [Tooltip("기체 뒤로 떨어지는 거리(m)")]
        [SerializeField] private float followDistance = 6f;
        [Tooltip("기체 루트 위로 올라가는 높이(m)")]
        [SerializeField] private float followHeight = 2.5f;
        [Tooltip("카메라가 바라보는 점: 기체 루트 위 높이(m)")]
        [SerializeField] private float lookHeight = 1.5f;
        [Tooltip("카메라가 바라보는 점: 기체 앞 거리(m). 클수록 시선이 수평에 가까워져 앞이 넓게 보인다")]
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
        [SerializeField] private float baseFov = 70f;
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
            if (_camera != null) _camera.fieldOfView = baseFov;
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
                _camera.fieldOfView = baseFov;
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

            if (InputTrace.ShouldLog(target, "6 MechCamera"))
            {
                InputTrace.Log("6 MechCamera", $"{name} target={target.name} reader={_inputReader.gameObject.name} " +
                    $"move={_inputReader.MoveInput} look={_inputReader.LookInput}");
            }

            // 몸의 수평 회전을 지수 감쇠로 따라간다 (보간된 트랜스폼이라 물리 스텝 계단이 안 보인다)
            float bodyYaw = target.eulerAngles.y;
            float k = 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime);
            _yaw = Mathf.LerpAngle(_yaw, bodyYaw, k);

            // 기준점은 기체 위치를 늦게 따라간다: 대시 중엔 느리게 따라가 기체가 옆·앞으로 처진다 (V3.1). 몸 방향은 대시 중 안 돈다
            bool dashing = _mechController != null && _mechController.StateMachine != null && _mechController.StateMachine.IsDashing;
            float wantedSpeed = dashing ? dashPositionFollowSpeed : positionFollowSpeed;
            _currentFollowSpeed = Mathf.MoveTowards(_currentFollowSpeed, wantedSpeed, positionFollowBlendRate * Time.deltaTime);
            float kp = 1f - Mathf.Exp(-_currentFollowSpeed * Time.deltaTime);
            _followPosition = Vector3.Lerp(_followPosition, target.position, kp);
            Vector3 anchor = _followPosition;

            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 lookPoint = anchor + Vector3.up * lookHeight + yawRotation * (Vector3.forward * lookAhead);
            Vector3 desired = anchor + Vector3.up * followHeight - yawRotation * (Vector3.forward * followDistance);
            Vector3 toLook = lookPoint - desired;

            transform.position = desired;
            transform.rotation = toLook.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toLook, Vector3.up) : yawRotation;
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

        private void UpdateFov()
        {
            if (_camera == null || _mechController == null) return;

            float speed = _mechController.Rb.linearVelocity.magnitude;
            float maxSpeed = _mechController.DashSpeed;
            float t = maxSpeed > 0.0001f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
            float targetFov = Mathf.Lerp(baseFov, sprintFov, t);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}
