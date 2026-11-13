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
    /// 기체 뒤 카메라. 시점만 따로 돌리는 조작은 없다 — 방위는 이동기의 카메라 방위(기본 모드에선 몸에 강체로 붙음, 지연 없음) + 선회 리드.
    /// 사양서 5장: VoTickDriver가 틱 맨 앞에서 TickV8로 갱신(지난 틱 기체 상태 기준)하고, 그릴 때 두 틱 사이를 보간한다.
    /// 기체 뒤 거리 = 78u(키 × 3.54), 피치 8.4° 아래, 시선 표적 = 발 + 키/2 + 6u, 기준점(위치)은 매 틱 35%로 따라감(V8.3), 시선 표적 오프셋만 12%(V9c), 기체는 화면 가로 중앙 ±25% 안, 세로 FOV 35°.
    /// 수치는 VoSpec(원본) + StyleOverrides(위치 추종 35%·가로 상한 25%·FOV 35°는 우리 값). 여기 남은 수치는 우리 연출(흔들림·지형 충돌·화면 밖 화살표)뿐이다.
    /// (V3 고정 백뷰·속도 FOV는 CL3에서 지웠다.)
    /// </summary>
    public class MechCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("시선 높이")]
        [Tooltip("카메라 충돌 검사 시작점: 기체 루트 위 높이(m)")]
        [SerializeField] private float lookHeight = 1.5f;

        [Header("카메라 충돌 (T4)")]
        [Tooltip("카메라가 바위·테두리·벽 등 정적 지형을 뚫으면 기체 쪽으로 당긴다 (기체·리지드바디가 있는 콜라이더는 무시)")]
        [SerializeField] private bool collideWithWorld = true;
        [Tooltip("카메라를 감싸는 구 반지름(m). 근접 클리핑(0.3)보다 조금 작게")]
        [SerializeField] private float collisionRadius = 0.25f;
        [Tooltip("막힌 면에서 이만큼(m) 더 앞으로")]
        [SerializeField] private float collisionMargin = 0.05f;
        [Tooltip("충돌을 검사할 레이어")]
        [SerializeField] private LayerMask collisionLayers = ~0;

        [Header("화면 밖 상대 화살표")]
        [Tooltip("켜면 시작할 때 이 카메라에 OffscreenTargetArrow를 붙인다 (이미 있으면 그대로 쓴다)")]
        [SerializeField] private bool addOffscreenArrow = true;

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
        /// 라운드 시작: 흔들림을 끊고 다음 틱에 스폰 자리·방향으로 바로 맞춘다 (따라가기 감쇠 없이).
        /// </summary>
        public void ResetView(float yaw)
        {
            _shakeTimer = 0f;
            _kickTimer = 0f;
            _voRig.Reset();
            ApplyFov();
        }

        private void ApplyFov()
        {
            if (_camera != null) _camera.fieldOfView = StyleOverrides.Current.cameraFov;
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
            }

            _camera = GetComponent<UnityEngine.Camera>();
            ApplyFov();

            // 화면 분할 두 화면 모두에 나오도록 카메라마다 하나씩. 씬 배선 없이 코드로 붙인다
            if (addOffscreenArrow && GetComponent<OffscreenTargetArrow>() == null)
            {
                gameObject.AddComponent<OffscreenTargetArrow>();
            }
        }

        private void LateUpdate()
        {
            if (target == null || _inputReader == null || _mechController == null) return;
            LateUpdateV8();
        }

        /// <summary>흔들림·방향 흔들림을 더하고, 지형을 뚫으면 기체 쪽으로 당긴다.</summary>
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

        // ---- 이동기 카메라 (사양서 5장 계산은 VoCameraRig) ----
        private readonly VoCameraRig _voRig = new VoCameraRig();

        /// <summary>틱 맨 앞(VoTickDriver → 호스트): 지난 틱 기체 상태로 카메라를 한 틱 갱신한다.</summary>
        public void TickV8(MechMotor motor)
        {
            float aspect = _camera != null && _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            _voRig.Tick(motor, aspect, StyleOverrides.Current.cameraFov);
        }

        /// <summary>두 틱 사이를 보간해 그린다 (리지드바디 보간과 같은 비율). 그 위에 우리 흔들림·지형 충돌.</summary>
        private void LateUpdateV8()
        {
            if (!_voRig.IsReady) return;
            float alpha = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            _voRig.Evaluate(alpha, out Vector3 position, out Quaternion rotation);
            transform.position = position;
            transform.rotation = rotation;
            ApplyShakeAndCollision();
            if (_camera != null) _camera.fieldOfView = StyleOverrides.Current.cameraFov;
        }
    }
}
