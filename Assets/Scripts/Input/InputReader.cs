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
        [Tooltip("이동 입력 크기가 이 값 이상이면 달리기 시작. 속도는 이 값 ~ 끝을 0 ~ 최대로 다시 맞춘다 (키보드는 항상 1)")]
        [SerializeField] private float moveDeadzone = 0.3f;
        [Tooltip("튕기기(←→ 사이드스텝) 판정 수치")]
        [SerializeField] private StickGestureSettings gesture = new StickGestureSettings();

        [Header("액션 맵")]
        [Tooltip("게임 조작용 액션 맵 이름. 시작할 때와 패드 배정 때마다 PlayerInput의 현재 맵을 이걸로 맞추고 켠다")]
        [SerializeField] private string gameplayActionMap = "Player";

        [Header("메뉴")]
        [Tooltip("메뉴 위아래 이동으로 인정하는 스틱·D패드 기울기")]
        [SerializeField] private float menuNavigateThreshold = 0.5f;

        [Header("연습 단축키")]
        [Tooltip("패드 Select를 이 시간(초) 이상 누르면 리셋, 그보다 짧게 누르고 떼면 봇 모드 순환")]
        [SerializeField] private float selectHoldTime = 0.6f;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        /// <summary>○ 대시 버튼(패드 B / 키보드 Space)을 누르고 있는지. 누르고 있는 동안 대시 유지.</summary>
        public bool DashHeld { get; private set; }
        /// <summary>○ 대시 버튼을 이번 프레임에 눌렀는지. 대시 시작·취소는 누른 순간만.</summary>
        public bool DashPressedThisFrame { get; private set; }
        /// <summary>△ 점프 버튼(패드 Y·△ / 키보드 F)을 이번 프레임에 눌렀는지.</summary>
        public bool JumpPressedThisFrame { get; private set; }
        public bool Skill1Input { get; private set; }
        public bool Skill2Input { get; private set; }
        public int SideStepDirection { get; private set; }
        /// <summary>패드 왼쪽 스틱 원래 값 (키보드 WASD 제외). 튕기기 판정용.</summary>
        public Vector2 MoveStickRaw { get; private set; }

        /// <summary>이동 입력이 달리기를 시작할 만큼 있는지. 상태들의 이동 시작/정지 판정은 이걸 쓴다.</summary>
        public bool IsMoveInputActive => MoveInput.magnitude >= moveDeadzone;
        /// <summary>이동 데드존. 달리기 속도는 이 값부터 0으로 시작한다.</summary>
        public float MoveDeadzone => moveDeadzone;

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

        // 연습 단축키. 쓸지 말지(연습 모드 Fight 중에만)는 PracticeHotkeys가 정한다
        /// <summary>봇 행동 모드 순환: 패드 Select 짧게(뗄 때) 또는 키보드 Tab.</summary>
        public bool BotCyclePressed { get; private set; }
        /// <summary>두 기체 즉시 리셋: 패드 Select 길게(누른 지 selectHoldTime 되는 순간 한 번) 또는 키보드 Backspace.</summary>
        public bool PracticeResetPressed { get; private set; }
        /// <summary>히트 프리셋: 키보드 1~4 → 0~3, 안 눌렀으면 -1.</summary>
        public int HeatPresetPressed { get; private set; } = -1;
        /// <summary>이번 프레임에 이 플레이어 장치로 무엇이든 입력했는지 (잠금 중에도 읽힌다). 무입력 타이틀 복귀용.</summary>
        public bool AnyInputThisFrame { get; private set; }
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
        private InputAction _dashAction;
        private InputAction _jumpAction;
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
        private InputAction _selectAction;
        private InputAction _botCycleAction;
        private InputAction _practiceResetAction;
        private readonly InputAction[] _heatPresetActions = new InputAction[4];
        private float _selectDownTime = -1f;
        private bool _selectHoldFired;
        // 라운드 카운트다운·라운드 사이에 GameManager가 건다. 잠긴 동안 게임플레이 입력은 전부 0
        private bool _inputLocked = false;

        private readonly StickGestureDetector _gesture = new StickGestureDetector();

        private float _lastLeftTapTime = 0f;
        private float _lastRightTapTime = 0f;

        private bool _skill1Pending = false;
        private bool _skill2Pending = false;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            _moveAction = _playerInput.actions["Move"];
            _lookAction = _playerInput.actions["Look"];
            // Run·Slide·QuickTurn·Sprint 액션은 .inputactions에 남아 있지만 V3에서 읽지 않는다 (걷기·슬라이딩·초신지선회·전력질주 삭제)
            _dashAction = _playerInput.actions["Dash"];
            _jumpAction = _playerInput.actions["Jump"];
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
            _selectAction = _playerInput.actions["Select"];
            _botCycleAction = _playerInput.actions["BotCycle"];
            _practiceResetAction = _playerInput.actions["PracticeReset"];
            for (int i = 0; i < _heatPresetActions.Length; i++)
            {
                _heatPresetActions[i] = _playerInput.actions["HeatPreset" + (i + 1)];
            }

            _skill1Action.performed += _ => _skill1Pending = true;
            _skill2Action.performed += _ => _skill2Pending = true;
        }

        private void Start()
        {
            // 모든 PlayerInput의 OnEnable이 끝난 뒤라 SwitchCurrentActionMap이 에러 없이 된다
            EnsureGameplayActionMap();
        }

        /// <summary>
        /// PlayerInput의 현재 액션 맵을 조작용 맵으로 맞추고 켠다.
        /// 액션 에셋이 프로젝트 전역 액션이라, 원본을 받은 PlayerInput(먼저 초기화된 쪽)은 전역 자동 활성화로 동작하지만
        /// 두 번째 PlayerInput은 복사본을 받고 Default Map이 비어 있으면 아무도 켜 주지 않아 입력이 전부 0이 된다 (L1).
        /// 씬 설정(Default Map)에 기대지 않고 코드에서 매번 맞춘다.
        /// </summary>
        public void EnsureGameplayActionMap()
        {
            if (_playerInput == null || !_playerInput.isActiveAndEnabled || _playerInput.actions == null) return;

            InputActionMap current = _playerInput.currentActionMap;
            if (current == null || current.name != gameplayActionMap)
            {
                if (_playerInput.actions.FindActionMap(gameplayActionMap) == null)
                {
                    Debug.LogError($"[InputReader] 액션 맵 '{gameplayActionMap}'이 '{_playerInput.actions.name}'에 없습니다", this);
                    return;
                }
                _playerInput.SwitchCurrentActionMap(gameplayActionMap);
            }
            else if (!current.enabled)
            {
                current.Enable();
            }
        }

        /// <summary>VERSUS 진입 로그용 한 줄: PlayerInput 번호·사용자·액션 맵(켜짐)·장치.</summary>
        public string DescribeBinding()
        {
            if (_playerInput == null) return "PlayerInput 없음";
            InputActionMap map = _playerInput.currentActionMap;
            string mapText = map != null ? $"{map.name}({(map.enabled ? "켜짐" : "꺼짐")})" : "없음";
            string userText = _playerInput.user.valid ? $"#{_playerInput.user.index}" : "없음";
            var devices = new System.Collections.Generic.List<string>();
            foreach (InputDevice device in _playerInput.devices) devices.Add(device.displayName);
            return $"PlayerInput #{_playerInput.playerIndex} (User {userText}, 오브젝트 {_playerInput.gameObject.name}), " +
                   $"액션 맵 {mapText}, Move 켜짐 {_moveAction.enabled}, 장치 [{string.Join(", ", devices)}]";
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookInput = _lookAction.ReadValue<Vector2>();
            if (InputTrace.ShouldLog(this, "1 PlayerInput"))
            {
                InputActionMap map = _playerInput.currentActionMap;
                InputTrace.Log("1 PlayerInput", $"{gameObject.name} map={(map != null ? map.name : "없음")} " +
                    $"mapEnabled={(map != null && map.enabled)} moveEnabled={_moveAction.enabled} raw={MoveInput} " +
                    $"projectWideAsset={ReferenceEquals(_playerInput.actions, InputSystem.actions)}");
            }
            AnyInputThisFrame = DetectAnyInput();

            // 히트스톱(timeScale 0) 중에도 입력 타이밍은 실제 시간으로 잰다
            Vector2 stick = _moveStickAction.ReadValue<Vector2>();
            _gesture.Tick(stick, Time.unscaledTime, gesture);
            MoveStickRaw = stick;

            DashHeld = _dashAction.IsPressed();
            DashPressedThisFrame = _dashAction.WasPressedThisFrame();
            JumpPressedThisFrame = _jumpAction.WasPressedThisFrame();

            AttackHeld = _attackAction.IsPressed();
            AttackPressedThisFrame = _attackAction.WasPressedThisFrame();
            GuardHeld = _guardAction.IsPressed();
            GuardPressedThisFrame = _guardAction.WasPressedThisFrame();

            Skill1Input = _skill1Pending;
            _skill1Pending = false;

            Skill2Input = _skill2Pending;
            _skill2Pending = false;

            SideStepDirection = _gesture.FlickSide;
            DetectSideStep();

            StartPressed = _startAction.WasPressedThisFrame();
            UpdateMenuInput();
            UpdatePracticeInput();
            if (_inputLocked) ClearGameplayInput();

            if (InputTrace.ShouldLog(this, "2 InputReader"))
            {
                InputTrace.Log("2 InputReader", $"{gameObject.name} MoveInput={MoveInput} active={IsMoveInputActive} " +
                    $"locked={_inputLocked}");
            }
        }

        private bool DetectAnyInput()
        {
            // 이 플레이어에게 배정된 장치로 들어온 액션만 본다 (페어링 덕분에 다른 사람 패드는 안 섞인다)
            foreach (InputAction action in _playerInput.actions)
            {
                if (action.enabled && action.IsPressed()) return true;
            }
            return false;
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

        private void UpdatePracticeInput()
        {
            BotCyclePressed = _botCycleAction.WasPressedThisFrame();
            PracticeResetPressed = _practiceResetAction.WasPressedThisFrame();

            // 패드 Select 하나로 두 기능: 짧게 = 순환(뗄 때 판정), 길게 = 리셋(시간이 차는 순간, 뗄 때는 순환 안 함)
            float now = Time.unscaledTime;
            if (_selectAction.WasPressedThisFrame())
            {
                _selectDownTime = now;
                _selectHoldFired = false;
            }
            if (_selectDownTime >= 0f)
            {
                if (_selectAction.IsPressed())
                {
                    if (!_selectHoldFired && now - _selectDownTime >= selectHoldTime)
                    {
                        _selectHoldFired = true;
                        PracticeResetPressed = true;
                    }
                }
                else
                {
                    if (!_selectHoldFired) BotCyclePressed = true;
                    _selectDownTime = -1f;
                }
            }

            HeatPresetPressed = -1;
            for (int i = 0; i < _heatPresetActions.Length; i++)
            {
                if (_heatPresetActions[i].WasPressedThisFrame()) HeatPresetPressed = i;
            }
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
            DashHeld = false;
            DashPressedThisFrame = false;
            JumpPressedThisFrame = false;
            MoveStickRaw = Vector2.zero;
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
