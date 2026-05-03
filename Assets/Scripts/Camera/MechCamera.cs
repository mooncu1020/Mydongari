using System.Collections;
using UnityEngine;
using MyDongari.Input;

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

        private float _yaw = 0f;
        private float _pitch = 0f;
        private InputReader _inputReader;
        private bool _isQuickTurning = false;

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
            }
        }

        private void LateUpdate()
        {
            if (target == null || _inputReader == null) return;

            if (!_isQuickTurning)
            {
                _yaw += _inputReader.LookInput.x * mouseSensitivity;
            }

            _pitch -= _inputReader.LookInput.y * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, -verticalClamp, verticalClamp);

            transform.position = target.position + offset;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}