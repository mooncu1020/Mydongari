using UnityEngine;
using UnityEngine.InputSystem;

namespace MyDongari.Input
{
    public class InputReader : MonoBehaviour, ICombatInputSource
    {
        [Header("사이드스텝 (키보드 A/D 더블탭)")]
        [Tooltip("같은 방향키를 두 번 누르는 간격이 이 시간(초) 안이면 사이드스텝")]
        [SerializeField] private float doubleTapWindow = 0.3f;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool RunInput { get; private set; }
        public bool SprintInput { get; private set; }
        public bool SlideInput { get; private set; }
        public bool QuickTurnInput { get; private set; }
        public bool Skill1Input { get; private set; }
        public bool Skill2Input { get; private set; }
        public int SideStepDirection { get; private set; }

        public bool AttackHeld { get; private set; }
        public bool GuardHeld { get; private set; }
        public bool GuardPressedThisFrame { get; private set; }

        /// <summary>
        /// Look 입력이 스틱(패드)에서 오는지. 스틱은 "기울기"라 deltaTime을 곱해야 하고
        /// 마우스는 이미 "이동량"이라 곱하면 안 되기 때문에 카메라가 이걸 보고 분기한다.
        /// </summary>
        public bool IsLookFromGamepad
        {
            get
            {
                InputControl control = _lookAction.activeControl;
                if (control != null) return !(control.device is Pointer);
                return _playerInput.currentControlScheme != KeyboardMouseScheme;
            }
        }

        private const string KeyboardMouseScheme = "Keyboard&Mouse";

        private PlayerInput _playerInput;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _runAction;
        private InputAction _sprintAction;
        private InputAction _slideAction;
        private InputAction _quickTurnAction;
        private InputAction _skill1Action;
        private InputAction _skill2Action;
        private InputAction _attackAction;
        private InputAction _guardAction;
        private InputAction _stepLeftAction;
        private InputAction _stepRightAction;

        private float _lastLeftTapTime = 0f;
        private float _lastRightTapTime = 0f;

        private bool _quickTurnPending = false;
        private bool _skill1Pending = false;
        private bool _skill2Pending = false;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _moveAction = _playerInput.actions["Move"];
            _lookAction = _playerInput.actions["Look"];
            _runAction = _playerInput.actions["Run"];
            _sprintAction = _playerInput.actions["Sprint"];
            _slideAction = _playerInput.actions["Slide"];
            _quickTurnAction = _playerInput.actions["QuickTurn"];
            _skill1Action = _playerInput.actions["Skill1"];
            _skill2Action = _playerInput.actions["Skill2"];
            _attackAction = _playerInput.actions["Attack"];
            _guardAction = _playerInput.actions["Guard"];
            _stepLeftAction = _playerInput.actions["StepLeft"];
            _stepRightAction = _playerInput.actions["StepRight"];

            _quickTurnAction.performed += _ => _quickTurnPending = true;
            _skill1Action.performed += _ => _skill1Pending = true;
            _skill2Action.performed += _ => _skill2Pending = true;
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookInput = _lookAction.ReadValue<Vector2>();
            RunInput = _runAction.IsPressed();
            SprintInput = _sprintAction.IsPressed();
            SlideInput = _slideAction.WasPressedThisFrame();

            AttackHeld = _attackAction.IsPressed();
            GuardHeld = _guardAction.IsPressed();
            GuardPressedThisFrame = _guardAction.WasPressedThisFrame();

            QuickTurnInput = _quickTurnPending;
            _quickTurnPending = false;

            Skill1Input = _skill1Pending;
            _skill1Pending = false;

            Skill2Input = _skill2Pending;
            _skill2Pending = false;

            SideStepDirection = 0;
            DetectSideStep();
        }

        private void DetectSideStep()
        {
            // 키보드 A/D 더블탭. 장치를 직접 읽지 않고 액션을 거쳐야
            // 2인 플레이에서 각 PlayerInput에 페어링된 장치 입력만 들어온다.
            bool leftPressed = _stepLeftAction.WasPressedThisFrame();
            bool rightPressed = _stepRightAction.WasPressedThisFrame();

            if (leftPressed)
            {
                if (Time.time - _lastLeftTapTime < doubleTapWindow)
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
                if (Time.time - _lastRightTapTime < doubleTapWindow)
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
