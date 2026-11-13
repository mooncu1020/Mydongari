using MyDongari.Input;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    /// <summary>
    /// 우리 조작 → 이동기 입력 (사양서 2.1을 패드에 맞춘 것, 원본과 다른 부분):
    /// L스틱 = 두 레버를 같은 방향으로 민 것, R스틱 좌우 = 레버 차동(선회), ○ = 터보(하나뿐이라 "다른 터보"는 항상 떼어진 것),
    /// △ = 레버 벌리기(점프), ✕ = 레버 모으기(급강하 — 땅에서는 가드라 이동기가 공중일 때만 받는다), 마우스 = 예비 선회.
    /// 60Hz 틱은 화면 프레임과 어긋나므로 "이번 프레임에 눌렀나" 대신 누름 횟수가 바뀌었는지로 새 입력을 본다.
    /// </summary>
    public class VoInputAdapter
    {
        private readonly InputReader _input;
        private int _lastDashCount;
        private int _lastJumpCount;
        private int _lastGuardCount;
        private float _pendingMouseTurn;

        public VoInputAdapter(InputReader input)
        {
            _input = input;
        }

        public bool HasInput => _input != null;

        /// <summary>새 입력 판정 기준을 지금으로 (이동기가 켜질 때·라운드 시작).</summary>
        public void Reset()
        {
            if (_input == null) return;
            _lastDashCount = _input.DashPressCount;
            _lastJumpCount = _input.JumpPressCount;
            _lastGuardCount = _input.GuardPressCount;
        }

        /// <summary>화면 프레임마다: 마우스 이동량은 프레임마다 오므로 모아 두고 틱에서 쓴다.</summary>
        public void FrameUpdate(float mouseTurnSensitivity)
        {
            if (_input != null && !_input.IsLookFromGamepad)
            {
                _pendingMouseTurn += _input.LookInput.x * mouseTurnSensitivity;
            }
        }

        /// <summary>한 틱의 입력.</summary>
        public VoInputFrame Read()
        {
            var frame = new VoInputFrame();
            int dashCount = _input.DashPressCount;
            int jumpCount = _input.JumpPressCount;
            frame.TurboNew = dashCount != _lastDashCount;
            frame.JumpNew = jumpCount != _lastJumpCount;
            _lastDashCount = dashCount;
            _lastJumpCount = jumpCount;
            int guardCount = _input.GuardPressCount;
            frame.DiveNew = guardCount != _lastGuardCount;
            _lastGuardCount = guardCount;
            // 두 틱 사이에 눌렀다 뗀 탭도 "누름"으로 본다
            frame.TurboHeld = _input.DashHeld || frame.TurboNew;
            frame.JumpHeld = _input.JumpHeld || frame.JumpNew;

            frame.LeverStick = _input.MoveInput;
            // R스틱 좌우 = 수동 선회 (사양서의 레버 차동 대신). 마우스일 땐 선회 축 0, 모아 둔 이동량을 그대로 더한다
            frame.TurnAxis = _input.IsLookFromGamepad ? _input.LookInput.x : 0f;
            frame.ExtraTurnDegrees = _pendingMouseTurn;
            _pendingMouseTurn = 0f;
            return frame;
        }
    }
}
