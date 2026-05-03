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
                if (!isSprinting)
                {
                    _yaw += _inputReader.LookInput.x * mouseSensitivity;
                    _pitch -= _inputReader.LookInput.y * mouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch, -verticalClamp, verticalClamp);
                }
                else if (_mechController != null)
                {
                    Vector3 velocity = _mechController.Rb.linearVelocity;
                    velocity.y = 0f;
                    if (velocity.sqrMagnitude > 0.1f)
                    {
                        float targetYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
                        _yaw = Mathf.LerpAngle(_yaw, targetYaw, 5f * Time.deltaTime);
                    }
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