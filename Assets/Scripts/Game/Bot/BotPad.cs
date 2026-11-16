using UnityEngine;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Input;

namespace MyDongari.Game.Bot
{
    /// <summary>봇 두뇌가 고르는 행동 (무엇을 할지). 어떻게 누를지는 BotPad가 정한다.</summary>
    public enum BotOrderKind
    {
        /// <summary>아무것도 안 누름.</summary>
        Neutral,
        /// <summary>계속 누르는 가드(✕).</summary>
        HoldGuard,
        /// <summary>가드를 새로 눌러 패리 창을 열고 잠깐 유지.</summary>
        Parry,
        /// <summary>상대 쪽으로 걷기 / 물러나기 / 옆걸음 (Dir: 1 오른쪽, -1 왼쪽).</summary>
        WalkToward,
        WalkAway,
        Strafe,
        /// <summary>앞 대시로 다가가기 (거리가 줄면 두뇌가 다른 행동으로 바꾼다).</summary>
        DashToward,
        /// <summary>옆 대시로 피하기 (Dir 방향).</summary>
        SideDash,
        /// <summary>앞 대시 → 대시 공격.</summary>
        DashAttack,
        /// <summary>근접 스텝(○ 먼저 → 스틱) 옆으로.</summary>
        CloseStep,
        /// <summary>원투쓰리 (Count = 몇 타까지).</summary>
        Combo,
        /// <summary>점프 → 떠오른 뒤 공격 = 내려찍기.</summary>
        DiveStrike,
        /// <summary>그냥 점프.</summary>
        Jump,
        /// <summary>근접 구역 밖 공격 = 런지.</summary>
        Lunge,
        /// <summary>기상 대기: 공격 연타 → 일어나며 공격.</summary>
        WakeAttack,
        /// <summary>기상 대기: 점프 연타 → 일어나며 점프.</summary>
        WakeJump,
        /// <summary>기상 대기: ○ + 옆 방향을 누른 채 가드 연타 → 일어나며 대시.</summary>
        WakeDash,
        /// <summary>기상 대기: 아무것도 안 함.</summary>
        WakeStill
    }

    /// <summary>두뇌의 주문 하나.</summary>
    public struct BotOrder
    {
        public BotOrderKind Kind;
        /// <summary>방향 (옆: 1 오른쪽 / -1 왼쪽).</summary>
        public int Dir;
        /// <summary>횟수 (콤보 타수 등).</summary>
        public int Count;

        public BotOrder(BotOrderKind kind, int dir = 1, int count = 0)
        {
            Kind = kind;
            Dir = dir;
            Count = count;
        }

        /// <summary>한 번 하고 끝나는 행동인지 (끝날 때까지 다음 주문을 받지 않는다). 나머지는 두뇌가 틱마다 다시 고른다.</summary>
        public bool IsMacro => Kind == BotOrderKind.Parry || Kind == BotOrderKind.SideDash || Kind == BotOrderKind.DashAttack ||
                               Kind == BotOrderKind.CloseStep || Kind == BotOrderKind.Combo || Kind == BotOrderKind.DiveStrike ||
                               Kind == BotOrderKind.Jump || Kind == BotOrderKind.Lunge;
    }

    /// <summary>
    /// 봇의 가상 패드 = 입력 생성 (행동 선택과 분리 — V4 팀전 봇도 이걸 쓴다).
    /// 두뇌가 준 주문을 틱마다 스틱·버튼으로 바꿔 InputReader(가상 패드)에 넣는다. 사람 패드와 같은 값만 만든다:
    /// L스틱(몸 기준 레버), R스틱 좌우(선회), ○·△·✕·공격의 누름과 누르고 있기. 판정·특혜는 없다 — 나머지는 전부 플레이어와 같은 코드가 처리한다.
    /// 선회는 두뇌가 준 표적 방위로 R스틱을 기울인다(사람이 상대를 화면 가운데로 돌리듯).
    /// </summary>
    public class BotPad
    {
        private readonly InputReader _reader;
        private readonly BotPadTiming _t;

        private BotOrder _macro;
        private bool _macroActive;
        private int _macroFrame;

        // 이번 틱에 만들 패드 값
        private Vector2 _stick;
        private float _turn;
        private bool _dash, _jump, _guard;

        public BotPad(InputReader reader, BotPadTiming timing)
        {
            _reader = reader;
            _t = timing;
        }

        /// <summary>한 번짜리 행동(콤보·대시 공격 등)을 진행 중인지.</summary>
        public bool Busy => _macroActive;
        /// <summary>진행 중인 행동.</summary>
        public BotOrderKind Current => _macroActive ? _macro.Kind : BotOrderKind.Neutral;

        /// <summary>전부 떼고 진행 중인 행동도 버린다.</summary>
        public void Release()
        {
            _macroActive = false;
            _stick = Vector2.zero;
            _turn = 0f;
            _dash = _jump = _guard = false;
            Commit();
        }

        /// <summary>
        /// 한 틱. order = 두뇌의 이번 틱 주문(한 번짜리 행동이 진행 중이면 무시), aimBearing = 돌아볼 방위(°, null이면 선회 안 함),
        /// turnRateDegrees = 지금 정면 안 선회율(°/틱) — 넘치지 않게 R스틱을 덜 기울이는 데 쓴다.
        /// </summary>
        public void Tick(BotOrder order, in SelfView self, float? aimBearing, float turnRateDegrees)
        {
            _stick = Vector2.zero;
            _dash = _jump = _guard = false;
            _turn = aimBearing.HasValue && self.Valid ? TurnAxis(self.Yaw, aimBearing.Value, turnRateDegrees) : 0f;

            if (!_macroActive && order.IsMacro)
            {
                _macro = order;
                _macroActive = true;
                _macroFrame = 0;
            }
            if (_macroActive)
            {
                if (TickMacro(self)) _macroActive = false;
                else _macroFrame++;
            }
            else
            {
                Hold(order, self);
            }
            Commit();
        }

        /// <summary>R스틱 기울기: 표적까지 남은 각을 한 틱에 다 돌 만큼만 (부호 유지 제곱 선회 계수라 제곱근).</summary>
        private static float TurnAxis(float yaw, float bearing, float rateDegrees)
        {
            float off = Mathf.DeltaAngle(yaw, bearing);
            float need = Mathf.Abs(off) / Mathf.Max(0.01f, rateDegrees);
            return Mathf.Sign(off) * Mathf.Sqrt(Mathf.Clamp01(need));
        }

        /// <summary>계속 고르는 행동 (틱마다 두뇌가 다시 준다).</summary>
        private void Hold(BotOrder order, in SelfView self)
        {
            switch (order.Kind)
            {
                case BotOrderKind.HoldGuard:
                    // 땅에서만 가드 — 공중의 ✕는 급강하라 누르지 않는다
                    _guard = self.Grounded && !self.Airborne;
                    break;
                case BotOrderKind.WalkToward:
                    _stick = Vector2.up;
                    break;
                case BotOrderKind.WalkAway:
                    _stick = Vector2.down;
                    break;
                case BotOrderKind.Strafe:
                    _stick = new Vector2(Mathf.Sign(order.Dir), 0f);
                    break;
                case BotOrderKind.DashToward:
                    // 서 있으면 ○를 새로 누르고(스틱 앞), 대시 중이면 누르고 있기
                    _stick = Vector2.up;
                    _dash = true;
                    if (self.Standing && self.DashAllowed) _reader.ScriptPressDash();
                    break;
                case BotOrderKind.WakeAttack:
                    if (self.WaitingWake && Every(_t.wakeMashInterval)) _reader.ScriptPressAttack();
                    break;
                case BotOrderKind.WakeJump:
                    if (self.WaitingWake && Every(_t.wakeMashInterval)) _reader.ScriptPressJump();
                    break;
                case BotOrderKind.WakeDash:
                    // 일어나는 순간 ○ + 방향이 들어와 있으면 대시. 그동안 가드 연타로 기상을 앞당긴다
                    _dash = true;
                    _stick = new Vector2(Mathf.Sign(order.Dir), 0f);
                    if (self.WaitingWake && Every(_t.wakeMashInterval)) _reader.ScriptPressGuard();
                    break;
            }
        }

        private int _tick;

        private bool Every(int interval)
        {
            return interval <= 1 || _tick % interval == 0;
        }

        /// <summary>한 번짜리 행동의 한 틱. 끝났으면 true.</summary>
        private bool TickMacro(in SelfView self)
        {
            int f = _macroFrame;
            BotPadTiming t = _t;
            switch (_macro.Kind)
            {
                case BotOrderKind.Parry:
                    // 새로 누른 틱부터 패리 창. 그 뒤 잠깐 가드 유지
                    if (f == 0) _reader.ScriptPressGuard();
                    _guard = true;
                    return f >= t.parryHoldFrames;

                case BotOrderKind.SideDash:
                {
                    Vector2 side = new Vector2(Mathf.Sign(_macro.Dir), 0f);
                    if (f == 0)
                    {
                        if (!self.Standing || !self.DashAllowed) return true;
                        _stick = side;
                        _dash = true;
                        _reader.ScriptPressDash();
                        return false;
                    }
                    _stick = side;
                    _dash = f < t.sideDashHoldFrames;
                    return f >= t.sideDashHoldFrames;
                }

                case BotOrderKind.DashAttack:
                    if (f == 0)
                    {
                        if (!self.Standing || !self.DashAllowed) return true;
                        _reader.ScriptPressDash();
                    }
                    _stick = Vector2.up;
                    _dash = true;
                    if (f == t.dashAttackPressFrame) _reader.ScriptPressAttack();
                    return f >= t.dashAttackPressFrame + t.afterAttackFrames;

                case BotOrderKind.CloseStep:
                    // ○ 먼저(스틱 중립) → 다음 틱 스틱 = 근접 스텝 (근접 구역 서기)
                    if (f == 0)
                    {
                        if (!self.Standing || !self.DashAllowed) return true;
                        _reader.ScriptPressDash();
                        _dash = true;
                        return false;
                    }
                    _dash = f < t.closeStepHoldFrames;
                    _stick = new Vector2(Mathf.Sign(_macro.Dir), 0f);
                    return f >= t.closeStepHoldFrames;

                case BotOrderKind.Combo:
                {
                    // 첫 타는 누르고, 휘두르는 동안 몇 틱마다 다시 눌러 다음 타를 예약한다(② 단계에서만 받는다 — 사람의 연타와 같다)
                    int want = Mathf.Clamp(_macro.Count, 1, 3);
                    if (f == 0)
                    {
                        _reader.ScriptPressAttack();
                        return false;
                    }
                    if (f > t.comboTimeoutFrames) return true;
                    if (!self.Melee) return f > t.comboStartGraceFrames;
                    if (self.ComboStep < want && f % t.comboPressInterval == 0) _reader.ScriptPressAttack();
                    return false;
                }

                case BotOrderKind.DiveStrike:
                    if (f == 0)
                    {
                        if (!self.Standing) return true;
                        _reader.ScriptPressJump();
                        _jump = true;
                        return false;
                    }
                    _jump = f < t.diveStrikeAttackFrame;
                    if (f == t.diveStrikeAttackFrame) _reader.ScriptPressAttack();
                    // 착지할 때까지 아무것도 안 누른다
                    return f > t.diveStrikeAttackFrame && self.Grounded && !self.Airborne;

                case BotOrderKind.Jump:
                    if (f == 0)
                    {
                        if (!self.Standing) return true;
                        _reader.ScriptPressJump();
                    }
                    _jump = f < t.jumpHoldFrames;
                    return f > 2 && self.Grounded && !self.Airborne;

                case BotOrderKind.Lunge:
                    if (f == 0) _reader.ScriptPressAttack();
                    return f >= t.afterAttackFrames || (f > 2 && !self.Lunging);
            }
            return true;
        }

        private void Commit()
        {
            _tick++;
            if (_reader == null) return;
            _reader.ScriptState(_stick, _dash, _jump, _guard);
            _reader.ScriptLook(new Vector2(_turn, 0f));
        }
    }

    /// <summary>가상 패드의 누르는 타이밍 (전부 틱). MeleeDummy 인스펙터에서 고친다.</summary>
    [System.Serializable]
    public class BotPadTiming
    {
        [Tooltip("패리: 가드를 새로 누른 뒤 유지하는 틱")]
        public int parryHoldFrames = 20;
        [Tooltip("옆 대시로 피하기: ○를 누르고 있는 틱 (짧으면 대시 정지가 빨리 온다)")]
        public int sideDashHoldFrames = 24;
        [Tooltip("대시 공격: 대시를 시작한 뒤 공격을 누르는 틱 (대시 시작 단계가 끝난 뒤여야 나간다)")]
        public int dashAttackPressFrame = 20;
        [Tooltip("공격을 누른 뒤 다음 주문을 받기까지 기다리는 틱")]
        public int afterAttackFrames = 30;
        [Tooltip("근접 스텝: ○를 누르고 있는 틱")]
        public int closeStepHoldFrames = 20;
        [Tooltip("콤보: 휘두르는 동안 공격을 다시 누르는 간격(틱) — 4틱 선입력 안에 ② 단계를 잡는다")]
        public int comboPressInterval = 3;
        [Tooltip("콤보: 첫 타가 안 나갔다고 보고 그만두는 틱")]
        public int comboStartGraceFrames = 8;
        [Tooltip("콤보: 이 틱이 지나면 무조건 끝")]
        public int comboTimeoutFrames = 150;
        [Tooltip("내려찍기: 점프를 누른 뒤 공격을 누르는 틱 (점프 중 아무 때나 된다 — 늦을수록 높은 데서 꽂는다)")]
        public int diveStrikeAttackFrame = 16;
        [Tooltip("그냥 점프: △를 누르고 있는 틱 (길면 더 높이)")]
        public int jumpHoldFrames = 10;
        [Tooltip("기상 대기: 연타 간격(틱)")]
        public int wakeMashInterval = 4;
    }
}
