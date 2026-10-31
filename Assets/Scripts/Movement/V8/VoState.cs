using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// V8 이동 상태의 바탕. MechState의 Update/FixedUpdate 대신 VoTickDriver의 60Hz 틱에서 MechMotor가
    /// Aux(상태 보조: 중력·y 적분·게이지·정지 입력) → … → Main(수평 속력·방향, 전환)을 부른다 (사양서 1.2).
    /// </summary>
    public abstract class VoState : MechState
    {
        protected VoState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        protected MechMotor Motor => StateMachine.Motor;
        protected BernardTuning.V8Values V => Motor.V;
        protected float Dt => Motor.Dt;

        /// <summary>지상 상태(이동 적분이 y를 지면으로 덮어씀, 5u 넘게 뜨면 낙하).</summary>
        public virtual bool IsGroundState => true;
        /// <summary>공중 상태(급강하 입력을 받음, IsAirborne).</summary>
        public virtual bool IsAirState => false;
        /// <summary>○를 새로 누른 틱에 스틱 방향을 대시 방향으로 잠그는지 (사양서 2.1 규칙 1·3).</summary>
        public virtual bool LocksOnTurboPress => false;
        /// <summary>○를 누른 뒤 15f 안에 들어온 스틱도 잠그는지 (규칙 2, 서기·걷기).</summary>
        public virtual bool LocksWithinWindow => false;

        public virtual void Aux()
        {
        }

        public abstract void Main();

        /// <summary>지상 상태에서 발밑 지면이 5u 넘게 아래로 사라졌을 때 (이동 적분). 기본은 낙하.</summary>
        public virtual void OnLostGround()
        {
            StateMachine.V8Fall(V.ledgeFallVy, false);
        }

        public override void Enter()
        {
        }

        public override void Update()
        {
        }

        public override void FixedUpdate()
        {
        }

        public override void Exit()
        {
        }

        protected bool IsStunned => StateMachine.IsStunned;
        protected bool BoostExhausted => StateMachine.MechController.BoostGauge != null && StateMachine.MechController.BoostGauge.IsExhausted;

        /// <summary>대시 조건 (사양서 4장): 방향 잠금 + ○ 유지 + 급강하 입력 아님 + 게이지 허가 + 스턴·탈진 아님.</summary>
        protected bool DashCondition()
        {
            return Motor.LockedDirection >= 0 && Motor.TurboHeld && !Motor.DiveNew && Motor.DashAllowed && !IsStunned && !BoostExhausted;
        }

        /// <summary>점프 조건: △ 새 입력 + 스턴 아님 + 부스트(jumpCost) — 예전 규칙대로 부스트를 쓴다.</summary>
        protected bool TryJump()
        {
            if (!Motor.JumpNew || IsStunned) return false;
            return StateMachine.TryStartJumpV8();
        }

        /// <summary>걷기·점프 수평: 속력 곡선(레버 각) × 레버 벡터 크기, 방향 = 레버 벡터를 몸 정면으로 돌린 것 (기체 기준).</summary>
        protected void SetLeverMove(BernardTuning.DirectionalValues curve)
        {
            Vector2 lever = Motor.Lever.Vector;
            if (lever.sqrMagnitude < 0.000001f) return;
            float magnitude = V.scaleByLeverMagnitude ? lever.magnitude : 1f;
            float speed = curve.Evaluate(Motor.Lever.Angle) * magnitude;
            float combo = StateMachine.MechController.MeleeSystem != null ? StateMachine.MechController.MeleeSystem.MoveSpeedScale : 1f;
            Vector3 dir = Quaternion.Euler(0f, Motor.Yaw, 0f) * new Vector3(lever.x, 0f, lever.y);
            Motor.MoveDirection = dir.normalized;
            Motor.HorizontalSpeed = speed * combo;
        }

        /// <summary>방위 + 오프셋의 수평 방향.</summary>
        protected static Vector3 Bearing(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>뒤 계열(뒤·뒤대각) 방향인지.</summary>
        protected static bool IsBackward(float offset) => Mathf.Abs(Mathf.DeltaAngle(0f, offset)) >= 134f;
    }

    /// <summary>서기 (사양서 4.1).</summary>
    public class VoStandState : VoState
    {
        public VoStandState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool LocksOnTurboPress => true;
        public override bool LocksWithinWindow => true;

        public override void Main()
        {
            // 공격 사용의 사격 조준은 15f 동안 유지, 나머지 자동 선회는 서기에서 기본으로
            if (Motor.AutoTurn != MechMotor.AutoTurnMode.AttackAim) Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            if (IsStunned) return;
            Motor.ApplyManualTurn(false);

            if (TryJump()) return;
            if (DashCondition())
            {
                // ○를 먼저 누르고 스틱이 나중에 들어왔고 근접 구역이면 근접 스텝, 아니면 지상 대시(시작 모션부터)
                if (Motor.InCloseZone && Motor.LockedAfterPress) StateMachine.V8CloseStep(Motor.LockedDirection);
                else StateMachine.V8GroundDash(Motor.LockedDirection, false);
                return;
            }
            if (!Motor.Lever.IsNeutral) StateMachine.ChangeState(StateMachine.VoWalk);
        }
    }

    /// <summary>걷기 (사양서 4.2).</summary>
    public class VoWalkState : VoState
    {
        public VoWalkState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool LocksOnTurboPress => true;
        public override bool LocksWithinWindow => true;

        public override void Main()
        {
            if (Motor.AutoTurn != MechMotor.AutoTurnMode.AttackAim) Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            if (IsStunned)
            {
                StateMachine.ChangeState(StateMachine.VoStand);
                return;
            }
            Motor.ApplyManualTurn(false);

            if (TryJump()) return;
            // 걷기에서는 잠금만 보면 된다 (잠금이 ○ 입력 때만 일어남). 지속 단계부터 — 뒤 계열은 시작 모션부터
            if (Motor.LockedDirection >= 0 && Motor.DashAllowed && !BoostExhausted && !Motor.DiveNew)
            {
                StateMachine.V8GroundDash(Motor.LockedDirection, true);
                return;
            }
            if (Motor.Lever.IsNeutral)
            {
                StateMachine.ChangeState(StateMachine.VoStand);
                return;
            }
            SetLeverMove(V.walkSpeed);
        }
    }

    /// <summary>
    /// 근접 스텝 (사양서 4.3): 근접 구역 서기에서 ○를 먼저 누르고 0.25초(15f) 안에 스틱. 진로 = 상대 방위 + 방향 오프셋으로 진입 때 고정,
    /// 속력은 시작값 → 끝값을 sin(π/2 · t/길이)로 보간, 6f 시작 + 22f. 게이지 안 씀.
    /// 시작 6f 뒤 ○(+방향) = 지상 대시 지속 단계로, △ = 점프. 끝나면 서기.
    /// </summary>
    public class VoCloseStepState : VoState
    {
        private int _code;
        private float _offset;
        private Vector3 _heading;
        private int _frame;

        public VoCloseStepState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool LocksOnTurboPress => true;

        public void Prepare(int code)
        {
            _code = code;
        }

        public override void Enter()
        {
            _offset = VoLever.Offset(_code);
            float basis = Motor.HasOpponent ? Motor.OpponentBearing : Motor.Yaw;
            _heading = Bearing(basis + _offset);
            _frame = 0;
        }

        public override void Main()
        {
            if (IsStunned)
            {
                StateMachine.ChangeState(StateMachine.VoStand);
                return;
            }
            if (TryJump()) return;
            if (_frame >= V.closeStepStartFrames && Motor.TurboNew && Motor.LockedDirection >= 0 && Motor.DashAllowed && !BoostExhausted)
            {
                StateMachine.V8GroundDash(Motor.LockedDirection, true);
                return;
            }
            float length = Mathf.Max(1, V.closeStepFrames);
            float t = Mathf.Clamp01(_frame / length);
            float start = V.closeStepStartSpeed.Evaluate(_offset);
            float end = V.closeStepEndSpeed.Evaluate(_offset);
            Motor.MoveDirection = _heading;
            Motor.HorizontalSpeed = Mathf.Lerp(start, end, Mathf.Sin(Mathf.PI * 0.5f * t));
            _frame++;
            if (_frame >= V.closeStepFrames)
            {
                StateMachine.ChangeState(Motor.Lever.IsNeutral ? (MechState)StateMachine.VoStand : StateMachine.VoWalk);
            }
        }
    }

    /// <summary>
    /// 대시 정지 (사양서 4.5): 지상 대시 시작·지속 중 ○ 재입력. 종료 모션(16f) 동안 걷기 속도 곡선 속력으로 진로를 따라 미끄러진다(스틱 무시).
    /// 진입하면 카메라를 몸에 맞춤(정지 맞춤). △는 내내, 공격은 3f째부터(V6.1). 끝나면 스틱이 있으면 걷기, 없으면 서기, 공중이면 낙하.
    /// </summary>
    public class VoDashStopState : VoState
    {
        private Vector3 _heading;
        private float _offset;
        private int _frame;
        private bool _airborne;

        public VoDashStopState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool IsGroundState => !_airborne;
        public override bool IsAirState => _airborne;
        /// <summary>공격을 받는지 (3f째부터).</summary>
        public bool AllowsAttack => !_airborne && _frame >= V.dashEndAttackFrame;
        public bool Airborne => _airborne;

        public void Prepare(Vector3 heading, float offset, bool airborne)
        {
            _heading = heading;
            _offset = offset;
            _airborne = airborne;
        }

        public override void Enter()
        {
            _frame = 0;
            Motor.AlignCamera();
        }

        public override void OnLostGround()
        {
            _airborne = true;
            Motor.Vy = 0f;
        }

        public override void Aux()
        {
            if (!_airborne) return;
            Motor.Vy = Mathf.Max(V.dashAirMinVy, Motor.Vy - V.dashAirGravity);
            if (Motor.AddVyAndCheckLanding()) _airborne = false;
        }

        public override void Main()
        {
            if (IsStunned && !_airborne)
            {
                StateMachine.ChangeState(StateMachine.VoStand);
                return;
            }
            if (!_airborne && TryJump()) return;
            Motor.MoveDirection = _heading;
            Motor.HorizontalSpeed = V.walkSpeed.Evaluate(_offset);
            _frame++;
            if (_frame >= V.dashStopFrames)
            {
                if (_airborne) StateMachine.V8Fall(V.ledgeFallVy, false);
                else StateMachine.ChangeState(Motor.Lever.IsNeutral ? (MechState)StateMachine.VoStand : StateMachine.VoWalk);
            }
        }
    }

    /// <summary>
    /// 급강하 (사양서 4.8): 점프 상승 중 급강하 입력(임시: 공중 L스틱 아래 + ○) 또는 천장. vy = −1.75u/f,
    /// 보조가 vy > −2.751이면 −0.25씩 + y += vy, 본 처리가 한 번 더 y += vy(2중). 수평 속력 0, 수동 선회만.
    /// </summary>
    public class VoDiveState : VoState
    {
        public VoDiveState(MechStateMachine sm) : base(sm)
        {
        }

        public override bool IsGroundState => false;
        public override bool IsAirState => true;

        public override void Enter()
        {
            Motor.Vy = V.diveStartVy;
            Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
        }

        public override void Aux()
        {
            if (Motor.Vy > V.diveAccelUntilVy) Motor.Vy -= V.diveAccel;
            if (Motor.AddVyAndCheckLanding()) StateMachine.Land(true);
        }

        public override void Main()
        {
            if (!IsStunned) Motor.ApplyManualTurn(false);
            if (Motor.AddVyAndCheckLanding()) StateMachine.Land(true);
        }
    }
}
