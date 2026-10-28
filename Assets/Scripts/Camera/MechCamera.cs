using System.Collections;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Movement;

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
                _pitch = Mathf.Lerp(startPitch, 0f, elapsed / duration);
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
                _yaw = Mathf.Lerp(startYaw, targetYaw, elapsed / quickTurnDuration);
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

            // 전력질주 진입 판정 중·질주 중엔 진행 방향 뒤로 따라가고, 질주가 풀린 뒤 오른쪽 스틱이 중립으로 돌아올 때까지
            // 시점 입력을 막는다 (F2: 질주 조작용 오른쪽 스틱이 카메라를 돌려 화면이 튀던 문제)
            MechStateMachine stateMachine = _mechController != null ? _mechController.StateMachine : null;
            bool followVelocity = stateMachine != null && stateMachine.IsCameraFollowingVelocity;
            bool lookBlocked = stateMachine != null && stateMachine.IsLookInputBlocked;

            if (!_isQuickTurning)
            {
                if (_hardLockTarget != null)
                {
                    Vector3 dir = (_hardLockTarget.position - transform.position).normalized;
                    float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    float targetPitch = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;

                    float yawDiff = Mathf.Abs(Mathf.DeltaAngle(_yaw, targetYaw));
                    float pitchDiff = Mathf.Abs(targetPitch - _pitch);

                    if (yawDiff < 1f && pitchDiff < 1f)
                    {
                        _yaw = targetYaw;
                        _pitch = targetPitch;
                    }
                    else
                    {
                        _yaw = Mathf.LerpAngle(_yaw, targetYaw, _hardLockTrackSpeed * Time.deltaTime);
                        _pitch = Mathf.Lerp(_pitch, targetPitch, _hardLockTrackSpeed * Time.deltaTime);
                    }

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

            transform.position = target.position + offset;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            UpdateFov();
        }

        private void UpdateFov()
        {
            if (_camera == null || _mechController == null) return;

            float speed = _mechController.Rb.linearVelocity.magnitude;
            float maxSpeed = _mechController.MaxSprintSpeed;
            float t = Mathf.Clamp01(speed / maxSpeed);
            float targetFov = Mathf.Lerp(baseFov, sprintFov, t);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
        }
    }
}