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

        private PlayerInput _playerInput;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _runAction;
        private InputAction _sprintAction;
        private InputAction _slideAction;
        private InputAction _quickTurnAction;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _moveAction = _playerInput.actions["Move"];
            _lookAction = _playerInput.actions["Look"];
            _runAction = _playerInput.actions["Run"];
            _sprintAction = _playerInput.actions["Sprint"];
            _slideAction = _playerInput.actions["Slide"];
            _quickTurnAction = _playerInput.actions["QuickTurn"];
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookInput = _lookAction.ReadValue<Vector2>();
            RunInput = _runAction.IsPressed();
            SprintInput = _sprintAction.IsPressed();
            SlideInput = _slideAction.WasPressedThisFrame();
            QuickTurnInput = _quickTurnAction.WasPressedThisFrame();
        }
    }
}