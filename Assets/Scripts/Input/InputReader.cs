using UnityEngine;
using UnityEngine.InputSystem;

namespace MyDongari.Input
{
    public class InputReader : MonoBehaviour, ICombatInputSource
    {
        [Header("사이드스텝 (키보드 A/D 더블탭)")]
        [Tooltip("같은 방향키를 두 번 누르는 간격이 이 시간(초) 안이면 사이드스텝")]
        [SerializeField] private float doubleTapWindow = 0.3f;

        [Header("패드 왼쪽 스틱")]
        [Tooltip("이동 입력 크기가 이 값 이상이면 걷기 시작 (키보드는 항상 1이라 영향 없음)")]
        [SerializeField] private float moveDeadzone = 0.3f;
        [Tooltip("스틱 크기가 이 값 이상이면 달리기 (스펙: 80% 경계)")]
        [SerializeField] private float runThreshold = 0.8f;
        [Tooltip("튕기기(↑↑ 전력질주, ↓ 초신지선회, ←→ 사이드스텝, 중립 후 ↗↖ 슬라이딩) 판정 수치")]
        [SerializeField] private StickGestureSettings gesture = new StickGestureSettings();

        [Header("메뉴")]
        [Tooltip("메뉴 위아래 이동으로 인정하는 스틱·D패드 기울기")]
        [SerializeField] private float menuNavigateThreshold = 0.5f;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool RunInput { get; private set; }
        public bool SprintInput { get; private set; }
        public bool SlideInput { get; private set; }
        public bool QuickTurnInput { get; private set; }
        public bool Skill1Input { get; private set; }
        public bool Skill2Input { get; private set; }
        public int SideStepDirection { get; private set; }
        /// <summary>슬라이딩 방향(스틱 ↗ = 1, ↖ = -1, 키보드 = 0). 지금은 기록용, 슬라이드는 질주 방향으로 나간다.</summary>
        public int SlideDirection { get; private set; }

        /// <summary>이동 입력이 걷기를 시작할 만큼 있는지. 상태들의 이동 시작/정지 판정은 이걸 쓴다.</summary>
        public bool IsMoveInputActive => MoveInput.magnitude >= moveDeadzone;

        public bool AttackHeld { get; private set; }
        public bool AttackPressedThisFrame { get; private set; }
        public bool GuardHeld { get; private set; }
        public bool GuardPressedThisFrame { get; private set; }
        /// <summary>시작 버튼(패드 Start / 키보드 Enter). 입력 잠금 중에도 읽힌다 (재매치용).</summary>
        public bool StartPressed { get; private set; }
        /// <summary>메뉴 위아래: 이번 프레임에 새로 기울였으면 위 = 1, 아래 = -1. 이동 스틱·D패드·W/S·화살표. 잠금 중에도 읽힌다.</summary>
        public int MenuVertical { get; private set; }
        /// <summary>메뉴 결정: 패드 Start·A(South), 키보드 Enter. 잠금 중에도 읽힌다.</summary>
        public bool MenuConfirmPressed { get; private set; }
        public bool IsInputLocked => _inputLocked;

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
        private InputAction _moveStickAction;
        private InputAction _startAction;
        private InputAction _submitAction;
        private InputAction _menuNavigateAction;
        private int _menuHeldDirection;
        // 라운드 카운트다운·라운드 사이에 GameManager가 건다. 잠긴 동안 게임플레이 입력은 전부 0
        private bool _inputLocked = false;

        private readonly StickGestureDetector _gesture = new StickGestureDetector();

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
            // 패드 스틱만 바인딩된 액션. 키보드 WASD가 튕기기로 오인되지 않게 제스처는 이것만 본다
            _moveStickAction = _playerInput.actions["MoveStick"];
            _startAction = _playerInput.actions["Start"];
            _submitAction = _playerInput.actions["Submit"];
            _menuNavigateAction = _playerInput.actions["MenuNavigate"];

            _quickTurnAction.performed += _ => _quickTurnPending = true;
            _skill1Action.performed += _ => _skill1Pending = true;
            _skill2Action.performed += _ => _skill2Pending = true;
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookInput = _lookAction.ReadValue<Vector2>();

            // 히트스톱(timeScale 0) 중에도 입력 타이밍은 실제 시간으로 잰다
            Vector2 stick = _moveStickAction.ReadValue<Vector2>();
            _gesture.Tick(stick, Time.unscaledTime, gesture);

            // 키보드 경로(Shift/Space/Ctrl/R/A·D 더블탭)와 패드 스틱 경로를 같은 프로퍼티로 합친다
            RunInput = _runAction.IsPressed() || stick.magnitude >= runThreshold;
            SprintInput = _sprintAction.IsPressed() || _gesture.SprintHeld;
            SlideInput = _slideAction.WasPressedThisFrame() || _gesture.SlideTriggered;
            SlideDirection = _gesture.SlideTriggered ? _gesture.SlideDirection : 0;

            AttackHeld = _attackAction.IsPressed();
            AttackPressedThisFrame = _attackAction.WasPressedThisFrame();
            GuardHeld = _guardAction.IsPressed();
            GuardPressedThisFrame = _guardAction.WasPressedThisFrame();

            QuickTurnInput = _quickTurnPending || _gesture.FlickDown;
            _quickTurnPending = false;

            Skill1Input = _skill1Pending;
            _skill1Pending = false;

            Skill2Input = _skill2Pending;
            _skill2Pending = false;

            SideStepDirection = _gesture.FlickSide;
            DetectSideStep();

            StartPressed = _startAction.WasPressedThisFrame();
            UpdateMenuInput();
            if (_inputLocked) ClearGameplayInput();
        }

        private void UpdateMenuInput()
        {
            // 잠금으로 MoveInput이 0이 되기 전에 원래 값을 읽는다 (메뉴는 잠금 중에 쓰인다)
            float y = _moveAction.ReadValue<Vector2>().y + _menuNavigateAction.ReadValue<Vector2>().y;
            int direction = y > menuNavigateThreshold ? 1 : y < -menuNavigateThreshold ? -1 : 0;
            // 기울인 순간 한 번만 (계속 밀고 있으면 한 칸만 움직인다)
            MenuVertical = direction != 0 && direction != _menuHeldDirection ? direction : 0;
            _menuHeldDirection = direction;

            MenuConfirmPressed = StartPressed || _submitAction.WasPressedThisFrame();
        }

        /// <summary>이번 프레임 게임 입력을 비운다 (연습 리셋 단축키가 다른 입력과 겹쳐도 같은 프레임에 같이 나가지 않게).</summary>
        public void ClearFrameInput()
        {
            ClearGameplayInput();
        }

        public void SetInputLocked(bool locked)
        {
            _inputLocked = locked;
            if (locked) ClearGameplayInput();
        }

        private void ClearGameplayInput()
        {
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            RunInput = false;
            SprintInput = false;
            SlideInput = false;
            SlideDirection = 0;
            QuickTurnInput = false;
            Skill1Input = false;
            Skill2Input = false;
            SideStepDirection = 0;
            AttackHeld = false;
            AttackPressedThisFrame = false;
            GuardHeld = false;
            GuardPressedThisFrame = false;
            // 잠금 중 스틱 조작이 풀린 직후 제스처로 튀어나오지 않게 판정기도 비운다
            _gesture.Reset();
        }

        private void DetectSideStep()
        {
            // 키보드 A/D 더블탭. 장치를 직접 읽지 않고 액션을 거쳐야
            // 2인 플레이에서 각 PlayerInput에 페어링된 장치 입력만 들어온다.
            bool leftPressed = _stepLeftAction.WasPressedThisFrame();
            bool rightPressed = _stepRightAction.WasPressedThisFrame();

            if (leftPressed)
            {
                if (Time.unscaledTime - _lastLeftTapTime < doubleTapWindow)
                {
                    SideStepDirection = -1;
                    _lastLeftTapTime = 0f;
                }
                else
                {
                    _lastLeftTapTime = Time.unscaledTime;
                }
            }

            if (rightPressed)
            {
                if (Time.unscaledTime - _lastRightTapTime < doubleTapWindow)
                {
                    SideStepDirection = 1;
                    _lastRightTapTime = 0f;
                }
                else
                {
                    _lastRightTapTime = Time.unscaledTime;
                }
            }
        }
    }
}
