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

            bool isSprinting = _mechController != null &&
                               _mechController.StateMachine != null &&
                               _mechController.StateMachine.IsSprinting;

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