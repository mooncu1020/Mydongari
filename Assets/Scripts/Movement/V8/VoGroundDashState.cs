using UnityEngine;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 지상 대시 (사양서 4.4). 진로 = 몸 정면 + 방향 오프셋으로 진입 때 고정, 속력은 방향별 대시 곡선(첫 틱부터 일정).
    /// 단계: 시작(앞·옆 16f, 뒤 12f) → 지속(대시 게이지가 500에 닿을 때까지, 약 84f) → 종료(16f, v × (1 − t/17)).
    /// 걷기에서 들어오면 시작을 건너뛴다(뒤 계열은 시작부터).
    /// 보조: 시작·지속 중 ○ 새 입력 = 대시 정지(본 처리 전에), 지속·공격 중 몸 선회(대시 선회율 — 진로는 그대로), 게이지 +206/f,
    /// 발판 끝을 지나 공중이면 vy −0.081/f(하한 −1.801)로 떨어지다 착지.
    /// 지속: 공격 = 대시 어택·차징 태클(DashAttackRunner), △ = 점프. 종료: △ 언제든, 공격 3f째부터.
    /// 우리 규칙: 부스트 초당 소모(BoostGauge)·0%면 종료 단계로, 상대 몸 충돌 반동(MechMotor).
    /// V8.2: 근접 구역 서기의 스틱 중립 ○로 시작한 앞 대시는 시작 후 closeStepFromDashFrames(15f) 안에 스틱이 들어오면
    /// 그 순간 근접 스텝으로 바뀐다(방향은 그 순간 상대 기준으로 고정).
    /// </summary>
    public class VoGroundDashState : VoState, IDashMove
    {
        private enum Phase { Start, Sustain, End }

        private int _code;
        private bool _fromWalk;
        private bool _closeStepWindow;
        private int _age;
        private Phase _phase;
        private int _frame;
        private int _startFrames;
        private float _offset;
        private float _speed;
        private Vector3 _heading;
        private bool _airborne;
        private DashExitReason _exitReason;
        private readonly DashAttackRunner _attack;

        public VoGroundDashState(MechStateMachine sm) : base(sm)
        {
            _attack = new DashAttackRunner(sm);
        }

        // ---- 다른 코드가 보는 것 ----
        /// <summary>시작·지속 단계 (IsDashing). 종료 단계는 IsDashStopping.</summary>
        public bool IsDashingPhase => _phase != Phase.End;
        public bool IsEndPhase => _phase == Phase.End;
        /// <summary>종료 단계에서 공격을 받는지 (3f째부터).</summary>
        public bool EndAllowsAttack => _phase == Phase.End && !_airborne && _frame >= V.dashEndAttackFrame;
        public Vector3 Heading => _heading;
        public bool IsAttacking => _attack.IsAttacking;
        public bool IsTackleCharged => _attack.IsTackleCharged;
        public float TackleChargePower => _attack.TackleChargePower;
        public bool IsInStartup => _phase == Phase.Start;
        public bool IsAirborne => _airborne;
        public override bool IsGroundState => !_airborne;
        public override bool IsAirState => _airborne;
        public override bool LocksOnTurboPress => true;

        public void Prepare(int code, bool fromWalk, bool closeStepWindow)
        {
            _code = code;
            _fromWalk = fromWalk;
            _closeStepWindow = closeStepWindow;
        }

        public override void Enter()
        {
            _offset = VoLever.Offset(_code);
            _heading = Bearing(Motor.Yaw + _offset);
            _speed = V.dashSpeed.Evaluate(_offset);
            bool back = IsBackward(_offset);
            _startFrames = back ? V.dashStartFramesBack : V.dashStartFrames;
            _phase = _fromWalk && !back ? Phase.Sustain : Phase.Start;
            _frame = 0;
            _age = 0;
            _airborne = false;
            _exitReason = DashExitReason.Other;
            _attack.Reset();
            Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            StateMachine.DashState.RaiseStarted();
        }

        public override void Exit()
        {
            _attack.CancelIfAttacking();
            if (Motor.AutoTurn == MechMotor.AutoTurnMode.SustainAim) Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            StateMachine.DashState.RaiseEnded(_exitReason);
        }

        public override void OnLostGround()
        {
            _airborne = true;
            Motor.Vy = 0f;
        }

        public override void Aux()
        {
            // 1) 시작·지속 + 공격 없음 + ○ 새 입력 → 대시 정지 (본 처리보다 먼저)
            if (_phase != Phase.End && !_attack.IsAttacking && Motor.TurboNew)
            {
                _exitReason = DashExitReason.Canceled;
                StateMachine.V8DashStop(_heading, _offset, _airborne);
                return;
            }
            // 2) 몸 선회: 지속·공격 중 (진로는 고정)
            if (!IsStunned && (_phase == Phase.Sustain || _attack.IsAttacking)) Motor.ApplyManualTurn(true);
            // 3) 게이지
            if (_phase == Phase.Sustain || _attack.IsAttacking) Motor.DashGauge += V.gaugeGain;
            // 4) 공중이면 약한 중력으로 떨어지다 착지
            if (_airborne)
            {
                Motor.Vy = Mathf.Max(V.dashAirMinVy, Motor.Vy - V.dashAirGravity);
                if (Motor.AddVyAndCheckLanding()) _airborne = false;
            }
        }

        public override void Main()
        {
            if (IsStunned)
            {
                Leave(DashExitReason.Stun, StateMachine.VoStand);
                return;
            }

            // 근접 구역 중립 ○ 앞 대시: 0.25초 안에 스틱이 오면 그 순간 근접 스텝으로
            _age++;
            if (_closeStepWindow && _age <= V.closeStepFromDashFrames && !_airborne && !_attack.IsAttacking && Motor.Lever.Direction >= 0)
            {
                _exitReason = DashExitReason.CloseStep;
                StateMachine.V8CloseStep(Motor.Lever.Direction, true);
                return;
            }

            if (_attack.IsAttacking)
            {
                DashAttackRunner.Result r = _attack.Tick(Dt, ref _heading, _speed * Motor.SpeedScale, _offset, out bool stop);
                if (r == DashAttackRunner.Result.Finished)
                {
                    Leave(DashExitReason.Tackle, StateMachine.VoStand);
                    return;
                }
                if (r != DashAttackRunner.Result.None)
                {
                    Motor.MoveDirection = _heading;
                    Motor.HorizontalSpeed = stop ? 0f : _speed;
                    return;
                }
            }

            switch (_phase)
            {
                case Phase.Start:
                    _frame++;
                    if (_frame >= _startFrames)
                    {
                        _phase = Phase.Sustain;
                        _frame = 0;
                    }
                    break;

                case Phase.Sustain:
                    // 우리 부스트가 0%면 종료 단계로 (예전 규칙)
                    if (Motor.DashGauge >= V.gaugeMax || BoostExhausted)
                    {
                        _exitReason = BoostExhausted ? DashExitReason.Exhausted : DashExitReason.Timeout;
                        _phase = Phase.End;
                        _frame = 0;
                        break;
                    }
                    if (!_airborne && Motor.JumpNew)
                    {
                        _exitReason = DashExitReason.Jump;
                        if (TryJump()) return;
                        _exitReason = DashExitReason.Other;
                    }
                    _attack.TickCharge(Dt);
                    // 공격 버튼(WeaponSystem → RequestTackle) 또는 차징 접촉 → 이번 틱에 시작
                    DashAttackRunner.Result started = _attack.Tick(Dt, ref _heading, _speed * Motor.SpeedScale, _offset, out _);
                    if (started == DashAttackRunner.Result.Running)
                    {
                        Motor.MoveDirection = _heading;
                        Motor.HorizontalSpeed = _speed;
                        return;
                    }
                    break;

                case Phase.End:
                    if (!_airborne && Motor.JumpNew)
                    {
                        _exitReason = DashExitReason.Jump;
                        if (TryJump()) return;
                    }
                    _frame++;
                    if (_frame >= V.dashEndFrames && Motor.DashGauge < V.gaugeAllow)
                    {
                        if (_airborne) StateMachine.V8Fall(V.ledgeFallVy, false);
                        else StateMachine.ChangeState(Motor.Lever.IsNeutral ? (MechState)StateMachine.VoStand : StateMachine.VoWalk);
                        return;
                    }
                    Motor.MoveDirection = _heading;
                    Motor.HorizontalSpeed = _speed * Mathf.Max(0f, 1f - _frame / Mathf.Max(1f, V.dashEndDivisor));
                    return;
            }

            Motor.MoveDirection = _heading;
            Motor.HorizontalSpeed = _speed;
        }

        private void Leave(DashExitReason reason, MechState next)
        {
            _exitReason = reason;
            if (_airborne && (next == StateMachine.VoStand || next == StateMachine.VoWalk))
            {
                StateMachine.V8Fall(Motor.Vy, true);
                return;
            }
            StateMachine.ChangeState(next);
        }

        // ---- IDashMove ----

        public bool RequestTackle()
        {
            // 지속 단계에서만 (시작 단계·종료 단계·공중 불가) — 버퍼가 다시 시도한다
            if (_phase != Phase.Sustain || _airborne) return false;
            return _attack.Request();
        }

        public bool WouldHitWithin(float seconds) => _attack.WouldHitWithin(seconds, _heading, _speed * Motor.SpeedScale);

        public void EndByClash()
        {
            _attack.ConsumeByClash();
            Leave(DashExitReason.Tackle, StateMachine.VoStand);
        }

        public void EndByKnockback()
        {
            Leave(DashExitReason.Other, StateMachine.VoStand);
        }

        public void EndByBump()
        {
            Leave(DashExitReason.Bump, StateMachine.VoStand);
        }

        public void EndByWall()
        {
            // 사양서: 벽은 미끄러짐·멈춤만, 대시는 끝나지 않는다
        }
    }
}
