using UnityEngine;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 점프 (사양서 4.6, 7.1). 이륙 2f(vy 0) → vy = 3.25u/f, "점프 조준"(몸을 상대 쪽으로 최대 12.66°/f) → 상승(보조: vy −0.074, 0.001 이하면
    /// 다음 틱부터 하강, 천장이면 급강하 / 본: y += vy) → 하강(보조: y += vy → 착지 판정 → vy −0.074, 하한 −0.526 / 본: y += vy 한 번 더 — 실효 7.46m/s).
    /// 수평은 점프 속도 곡선 × 레버 벡터(기체 기준). 선회는 고정율 21°/s(○를 누르고 있으면 127°/s).
    /// V8.2: △ 단독은 사양서대로 — 상승 중 재점프(vy 3.25로 한 번), 하강 중 누른 지 7~14f째 호버.
    /// 급강하 = 공중(상승·하강)에서 L스틱 아래 + △. 상승 중 ○(+스틱, 중립이면 앞) = 공중 대시 8방향.
    /// 발판에서 떨어진 낙하(PrepareFall)도 이 상태의 하강 단계다.
    /// </summary>
    public class VoJumpState : VoState
    {
        private enum Phase { Takeoff, Rise, Fall }

        private Phase _phase;
        private int _frame;
        private bool _pendingFall;
        private bool _reinputUsed;
        private bool _airActionUsed;
        private bool _needsLandingLag;
        // 다음 Enter 설정
        private bool _prepareJump;
        private float _prepareVy;
        private bool _prepareLag;

        public VoJumpState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool IsGroundState => _phase == Phase.Takeoff;
        public override bool IsAirState => _phase != Phase.Takeoff;
        public override bool LocksOnTurboPress => true;
        public bool IsRising => _phase == Phase.Rise;
        public bool IsAiming => Motor != null && Motor.AutoTurn == MechMotor.AutoTurnMode.JumpAim;

        public void PrepareJump()
        {
            _prepareJump = true;
        }

        public void PrepareFall(float vy, bool landingLag)
        {
            _prepareJump = false;
            _prepareVy = vy;
            _prepareLag = landingLag;
        }

        /// <summary>공중에서 맞았다 — 착지하면 경직.</summary>
        public void MarkLandingLag()
        {
            _needsLandingLag = true;
        }

        public override void Enter()
        {
            _frame = 0;
            _pendingFall = false;
            _reinputUsed = false;
            _airActionUsed = false;
            if (_prepareJump)
            {
                _prepareJump = false;
                _phase = Phase.Takeoff;
                Motor.Vy = 0f;
                _needsLandingLag = true;
                StateMachine.AirState.RaiseJumped();
            }
            else
            {
                _phase = Phase.Fall;
                Motor.Vy = _prepareVy;
                _needsLandingLag = _prepareLag;
                Motor.AlignCamera();
            }
        }

        public override void Aux()
        {
            switch (_phase)
            {
                case Phase.Rise:
                    Motor.Vy -= V.gravity;
                    if (Motor.Vy <= V.apexVy)
                    {
                        Motor.Vy = 0f;
                        _pendingFall = true;
                    }
                    // 머리 위 천장 → 급강하
                    if (Motor.Vy > 0f && Motor.CheckCeiling(Motor.Vy * Motor.U))
                    {
                        StateMachine.V8Dive();
                    }
                    break;
                case Phase.Fall:
                    if (Motor.AddVyAndCheckLanding())
                    {
                        StateMachine.Land(_needsLandingLag);
                        return;
                    }
                    Motor.Vy = Mathf.Max(V.fallMinVy, Motor.Vy - V.gravity);
                    break;
            }
        }

        public override void Main()
        {
            int rate = Motor.TurboHeld ? 1 : 0;
            if (!IsStunned) Motor.ApplyFixedTurn(rate == 1 ? V.turnJumpTurbo : V.turnJump);

            switch (_phase)
            {
                case Phase.Takeoff:
                    _frame++;
                    if (_frame >= V.takeoffFrames)
                    {
                        _phase = Phase.Rise;
                        Motor.Vy = V.jumpVy;
                        Motor.SetAutoTurn(MechMotor.AutoTurnMode.JumpAim);
                    }
                    break;

                case Phase.Rise:
                    if (!IsStunned)
                    {
                        if (Motor.DiveNew)
                        {
                            StateMachine.V8Dive();
                            return;
                        }
                        if (Motor.JumpNew && !_reinputUsed)
                        {
                            // △ 재입력: 상승 중 한 번 vy를 다시 3.25로
                            _reinputUsed = true;
                            Motor.Vy = V.jumpVy;
                            _pendingFall = false;
                        }
                        else if (Motor.LockedDirection >= 0 && Motor.DashAllowed && !_airActionUsed && !BoostExhausted)
                        {
                            _airActionUsed = true;
                            StateMachine.V8AirDash(Motor.LockedDirection);
                            return;
                        }
                    }
                    Motor.AddVyAndCheckLanding();
                    // 정점 → 하강 전환은 이번 틱 끝에 (다음 틱부터 하강 처리)
                    if (_pendingFall)
                    {
                        _pendingFall = false;
                        _phase = Phase.Fall;
                    }
                    break;

                case Phase.Fall:
                    if (!IsStunned)
                    {
                        if (Motor.DiveNew)
                        {
                            StateMachine.V8Dive();
                            return;
                        }
                        // 호버: △를 누른 지 7~14f째 vy += (경과 − 7) × 0.1, 상한 −0.01
                        if (Motor.JumpHeld && Motor.JumpHeldFrames >= V.hoverStartFrame && Motor.JumpHeldFrames <= V.hoverEndFrame)
                        {
                            Motor.Vy = Mathf.Min(V.hoverMaxVy, Motor.Vy + (Motor.JumpHeldFrames - V.hoverStartFrame) * V.hoverGain);
                        }
                    }
                    if (V.fallDoubleIntegrate && Motor.AddVyAndCheckLanding())
                    {
                        StateMachine.Land(_needsLandingLag);
                        return;
                    }
                    break;
            }

            // 수평: 점프 속도 곡선 × 레버 (기체 기준). 이륙 중에도
            SetLeverMove(V.jumpSpeed);
        }
    }

    /// <summary>
    /// 공중 대시 (사양서 4.7): 점프 상승 중에만. 진입 순간 몸을 상대 방위로 맞추고 진로 = 상대 방위 + 오프셋.
    /// "공중 대시 복귀"가 몸을 카메라 방위(진입 전 몸 방향) 쪽으로 최대 14°/f로 되돌린다. 속력 = 공중 대시 곡선, 게이지 +206/f,
    /// 보조가 y += vy 한 번(상승 중 중력 × 0.5). 선회는 몸과 진로를 같이 0.57°/f. ○ 새 입력 또는 게이지 500 → 점프 하강, 착지 → 착지.
    /// </summary>
    public class VoAirDashState : VoState, IDashMove
    {
        private int _code;
        private float _offset;
        private float _speed;
        private Vector3 _heading;
        private DashExitReason _exitReason;

        public VoAirDashState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool IsGroundState => false;
        public override bool IsAirState => true;

        public void Prepare(int code)
        {
            _code = code;
        }

        public override void Enter()
        {
            _offset = VoLever.Offset(_code);
            float basis = Motor.HasOpponent ? Motor.OpponentBearing : Motor.Yaw;
            Motor.SetYaw(basis);
            _heading = Bearing(basis + _offset);
            _speed = V.airDashSpeed.Evaluate(_offset);
            _exitReason = DashExitReason.Other;
            Motor.SetAutoTurn(MechMotor.AutoTurnMode.AirDashReturn);
            StateMachine.DashState.RaiseStarted();
        }

        public override void Exit()
        {
            if (Motor.AutoTurn == MechMotor.AutoTurnMode.AirDashReturn) Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            StateMachine.DashState.RaiseEnded(_exitReason);
        }

        public override void Aux()
        {
            Motor.DashGauge += V.gaugeGain;
            float g = Motor.Vy > 0f ? V.gravity * V.airDashRisingGravityScale : V.gravity;
            Motor.Vy = Mathf.Max(V.fallMinVy, Motor.Vy - g);
            if (Motor.AddVyAndCheckLanding())
            {
                _exitReason = DashExitReason.Landed;
                StateMachine.Land(true);
            }
        }

        public override void Main()
        {
            if (IsStunned)
            {
                Fall(DashExitReason.Stun, Motor.Vy);
                return;
            }
            if (Motor.TurboNew)
            {
                Fall(DashExitReason.Canceled, Motor.Vy);
                return;
            }
            if (Motor.DashGauge >= V.gaugeMax || BoostExhausted)
            {
                Fall(BoostExhausted ? DashExitReason.Exhausted : DashExitReason.Timeout, V.airDashEndVy);
                return;
            }
            float turned = Motor.ApplyFixedTurn(V.turnAirDash);
            if (Mathf.Abs(turned) > 0f) _heading = Quaternion.Euler(0f, turned, 0f) * _heading;
            Motor.MoveDirection = _heading;
            Motor.HorizontalSpeed = _speed;
        }

        private void Fall(DashExitReason reason, float vy)
        {
            _exitReason = reason;
            StateMachine.V8Fall(Mathf.Min(vy, V.airDashEndVy), true);
        }

        // ---- IDashMove: 공중 대시 중 공격은 없음 (6단계 전) ----
        public Vector3 Heading => _heading;
        public bool IsAttacking => false;
        public bool IsTackleCharged => false;
        public float TackleChargePower => 0f;
        public bool IsInStartup => false;
        public bool IsAirborne => true;
        public bool RequestTackle() => false;
        public bool WouldHitWithin(float seconds) => false;
        public void EndByClash() => Fall(DashExitReason.Tackle, Motor.Vy);
        public void EndByKnockback() => Fall(DashExitReason.Other, Motor.Vy);
        public void EndByBump() => Fall(DashExitReason.Bump, Motor.Vy);
        public void EndByWall()
        {
        }
    }
}
