using System.Collections;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Movement;
using MyDongari.Visual;
using MyDongari.Mech;

namespace MyDongari.Camera
{
    public class MechCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 1.5f, 0f);

        [Header("시점 설정")]
        [SerializeField] private float mouseSensitivity = 2f;
        [Tooltip("패드 오른쪽 스틱 감도(초당 회전 각도). 스틱은 기울기 값이라 프레임레이트와 무관하게 deltaTime을 곱한다")]
        [SerializeField] private float stickSensitivity = 180f;
        [SerializeField] private float verticalClamp = 60f;

        [Header("퀵턴 설정")]
        [SerializeField] private float quickTurnDuration = 0.2f;

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

        private float _yaw = 0f;
        private float _pitch = 0f;
        private InputReader _inputReader;
        private MechController _mechController;
        private UnityEngine.Camera _camera;
        private bool _isQuickTurning = false;
        private Transform _hardLockTarget;
        private float _hardLockTrackSpeed;
        // 약한 흔들림 (발소리 등). 남은 시간이 줄수록 작아진다
        private float _shakeAmplitude;
        private float _shakeDuration;
        private float _shakeTimer;
        private MechVisualRig _rig;

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
        /// 기체 중심(콜라이더 안쪽이라 항상 안전)에서 원하는 카메라 위치까지 구를 쏴서, 정적 지형에 먼저 닿으면 그 앞으로 당긴다.
        /// 기체(리지드바디가 있는 콜라이더)와 트리거는 무시 — 상대가 카메라 앞을 지나가도 화면이 튀지 않게.
        /// </summary>
        private Vector3 ResolveCollision(Vector3 desired)
        {
            Vector3 origin = target.position;
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

        public void LockToForward()
        {
            StartCoroutine(LockToForwardRoutine());
        }

        private IEnumerator LockToForwardRoutine()
        {
            float elapsed = 0f;
            float duration = 0.2f;
            float startPitch = _pitch;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _pitch = Mathf.Lerp(startPitch, 0f, duration > 0f ? elapsed / duration : 1f);
                yield return null;
            }
            _pitch = 0f;
        }

        public void QuickTurn()
        {
            if (!_isQuickTurning)
                StartCoroutine(QuickTurnRoutine());
        }

        private IEnumerator QuickTurnRoutine()
        {
            _isQuickTurning = true;
            float startYaw = _yaw;
            float targetYaw = _yaw + 180f;
            float elapsed = 0f;

            while (elapsed < quickTurnDuration)
            {
                elapsed += Time.deltaTime;
                _yaw = Mathf.Lerp(startYaw, targetYaw, quickTurnDuration > 0f ? elapsed / quickTurnDuration : 1f);
                yield return null;
            }

            _yaw = targetYaw;
            _isQuickTurning = false;
        }

        public void TrackTarget(Transform lockTarget, float speed)
        {
            _hardLockTarget = lockTarget;
            _hardLockTrackSpeed = speed;
        }

        public void StopTracking()
        {
            _hardLockTarget = null;
        }

        /// <summary>
        /// 라운드 시작: 퀵턴·하드락 추적을 끊고 시점을 스폰 방향으로.
        /// 안 하면 Idle의 RotateToCamera가 기체를 이전 라운드 카메라 방향으로 다시 돌려버린다.
        /// </summary>
        public void ResetView(float yaw)
        {
            StopAllCoroutines();
            _isQuickTurning = false;
            _hardLockTarget = null;
            _shakeTimer = 0f;
            _kickTimer = 0f;
            _yaw = yaw;
            _pitch = 0f;
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
            }

            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera != null)
            {
                _camera.fieldOfView = baseFov;
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

            // 전력질주 진입 판정 중·질주 중엔 진행 방향 뒤로 따라가고, 질주가 풀린 뒤 오른쪽 스틱이 중립으로 돌아올 때까지
            // 시점 입력을 막는다 (F2: 질주 조작용 오른쪽 스틱이 카메라를 돌려 화면이 튀던 문제)
            MechStateMachine stateMachine = _mechController != null ? _mechController.StateMachine : null;
            bool followVelocity = stateMachine != null && stateMachine.IsCameraFollowingVelocity;
            bool lookBlocked = stateMachine != null && stateMachine.IsLookInputBlocked;

            // 이번 프레임 카메라 위치. 하드 락온 방향도 이 위치에서 잰다 (예전엔 지난 프레임 위치로 재서 내가 움직이는 프레임마다 방향이 튐)
            Vector3 pivot = target.position + offset;

            if (!_isQuickTurning)
            {
                if (_hardLockTarget != null)
                {
                    // 목표 = 상대 기체 루트(보간된 트랜스폼, 출렁이는 뼈 아님)
                    Vector3 dir = (_hardLockTarget.position - pivot).normalized;
                    float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    float targetPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

                    // 예전엔 1° 안이면 딱 붙이고 밖이면 따라가기를 번갈아 해서, 목표가 계단처럼 움직이면 화면이 떨렸다.
                    // 지수 감쇠 한 가지로만 따라간다 (속도 의미는 그대로: 작은 dt에서 hardTrackSpeed·dt와 같음)
                    float k = 1f - Mathf.Exp(-_hardLockTrackSpeed * Time.deltaTime);
                    _yaw = Mathf.LerpAngle(_yaw, targetYaw, k);
                    _pitch = Mathf.Lerp(_pitch, targetPitch, k);
                    _pitch = Mathf.Clamp(_pitch, -verticalClamp, verticalClamp);
                }
                else if (followVelocity)
                {
                    Vector3 velocity = _mechController.Rb.linearVelocity;
                    velocity.y = 0f;
                    if (velocity.sqrMagnitude > 0.1f)
                    {
                        float targetYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
                        float followSpeed = _mechController.Tuning.Movement.sprintCameraFollowSpeed;
                        _yaw = Mathf.LerpAngle(_yaw, targetYaw, followSpeed * Time.deltaTime);
                    }
                }
                else if (!lookBlocked)
                {
                    // 마우스 델타는 이미 프레임당 이동량이라 그대로, 스틱은 기울기라 시간 비례로
                    float lookScale = _inputReader.IsLookFromGamepad
                        ? stickSensitivity * Time.deltaTime
                        : mouseSensitivity;
                    _yaw += _inputReader.LookInput.x * lookScale;
                    _pitch -= _inputReader.LookInput.y * lookScale;
                    _pitch = Mathf.Clamp(_pitch, -verticalClamp, verticalClamp);
                }
                // 막혀 있고 따라가기도 아니면(질주 해제 후 스틱 중립 대기) 시점을 그대로 둔다
            }

            transform.position = pivot;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
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
            // 흔들림까지 더한 최종 위치가 지형을 뚫으면 앞으로 당긴다 (T4)
            if (collideWithWorld) transform.position = ResolveCollision(transform.position);

            UpdateFov();
        }

        private void UpdateFov()
        {
            if (_camera == null || _mechController == null) return;

            float speed = _mechController.Rb.linearVelocity.magnitude;
            float maxSpeed = _mechController.MaxSprintSpeed;
            float t = maxSpeed > 0.0001f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
            float targetFov = Mathf.Lerp(baseFov, sprintFov, t);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}