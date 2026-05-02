using UnityEngine;
using UnityEngine.InputSystem;

namespace MyDongari.Input
{
    public class InputReader : MonoBehaviour
    {
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool RunInput { get; private set; }
        public bool SprintInput { get; private set; }
        public bool SlideInput { get; private set; }
        public bool QuickTurnInput { get; private set; }
        public int SideStepDirection { get; private set; }

        private PlayerInput _playerInput;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _runAction;
        private InputAction _sprintAction;
        private InputAction _slideAction;
        private InputAction _quickTurnAction;

        private float _lastLeftTapTime = 0f;
        private float _lastRightTapTime = 0f;
        private const float DoubleTapWindow = 0.3f;

        private bool _quickTurnPending = false;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _moveAction = _playerInput.actions["Move"];
            _lookAction = _playerInput.actions["Look"];
            _runAction = _playerInput.actions["Run"];
            _sprintAction = _playerInput.actions["Sprint"];
            _slideAction = _playerInput.actions["Slide"];
            _quickTurnAction = _playerInput.actions["QuickTurn"];

            _quickTurnAction.performed += _ => _quickTurnPending = true;
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookInput = _lookAction.ReadValue<Vector2>();
            RunInput = _runAction.IsPressed();
            SprintInput = _sprintAction.IsPressed();
            SlideInput = _slideAction.WasPressedThisFrame();

            QuickTurnInput = _quickTurnPending;
            _quickTurnPending = false;

            SideStepDirection = 0;
            DetectSideStep();
        }

        private void DetectSideStep()
        {
            bool leftPressed = Keyboard.current.aKey.wasPressedThisFrame;
            bool rightPressed = Keyboard.current.dKey.wasPressedThisFrame;

            if (leftPressed)
            {
                if (Time.time - _lastLeftTapTime < DoubleTapWindow)
                {
                    SideStepDirection = -1;
                    _lastLeftTapTime = 0f;
                }
                else
                {
                    _lastLeftTapTime = Time.time;
                }
            }

            if (rightPressed)
            {
                if (Time.time - _lastRightTapTime < DoubleTapWindow)
                {
                    SideStepDirection = 1;
                    _lastRightTapTime = 0f;
                }
                else
                {
                    _lastRightTapTime = Time.time;
                }
            }
        }
    }
}