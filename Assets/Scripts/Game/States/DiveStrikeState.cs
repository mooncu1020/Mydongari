using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Combat;

namespace MyDongari.Game.States
{
    /// <summary>
    /// 내려찍기 (우리 규칙 — 원본 점프 근접 대신): 점프 중(상승·정점·하강) 공격 버튼. 점프 한 번에 한 번.
    /// 예비(diveStrikeWindupFrames, 8f): 상승을 멈추고 공중에 멈칫, 몸을 상대 쪽으로 돌린다(원본 점프 근접 추적율 2560 단위/f = 844°/s).
    /// 내리꽂기: 예비가 끝난 순간 상대 위치를 향해 직선으로 diveStrikeSpeed(40m/s). 꽂히는 동안 유도는 diveStrikeHomingRate(1.41°/f)뿐이라
    /// 옆 대시로 피할 수 있다. 수평 이동은 diveStrikeMaxHorizontal(15m)까지 — 더 먼 상대는 그 방향으로 짧게 떨어진다. 땅에 닿으면 끝.
    /// 판정: 몸에서 진로 쪽으로 뻗은 주먹 캡슐을 지난 틱 → 이번 틱으로 쓸어 검사(틱 끝 CollectHits). 일반 근접 판정(가드·패리 가능, 맞히면 양쪽 히트스톱),
    /// 충격치·피해는 원본 점프 근접 값. 맞혔으면(막힘 포함) 일반 착지 12f, 빗나가면(패리·무적 포함) 착지 경직 diveStrikeMissLandingFrames(30f, 공격으로 취소 불가).
    /// 그동안 공중 대시·급강하·가드는 없다(이동 상태가 아니라 입력을 받지 않는다). 속도는 외부 속도 통로(MotionBridge)로 틱마다 넣는다.
    /// </summary>
    public class DiveStrikeState : GameState
    {
        public enum DivePhase { Windup, Dive }

        public DivePhase Phase { get; private set; }
        /// <summary>내리꽂는 방향(월드, 크기 1). 예비 중에는 아래.</summary>
        public Vector3 Direction => _direction;

        private int _frame;
        private Vector3 _direction = Vector3.down;
        private float _travelled;
        private bool _hitQueued;
        private bool _connected;
        private Vector3 _lastNear, _lastFar;
        private bool _hasLast;

        public DiveStrikeState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        private VoMotor Motor => StateMachine.Motor;
        private MeleeSystem Melee => StateMachine.MechController.MeleeSystem;
        private Rigidbody Rb => StateMachine.MechController.Rb;

        public override void Enter()
        {
            Phase = DivePhase.Windup;
            _frame = 0;
            _direction = Vector3.down;
            _travelled = 0f;
            _hitQueued = false;
            _connected = false;
            _hasLast = false;
            Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
            Hover();
        }

        public override void Update()
        {
            // 굳었거나(훅 경직) 다른 것이 나를 끌고 가면(앵커 클로) 내려찍기는 끊기고 그대로 떨어진다
            if (StateMachine.IsStunned || StateMachine.MechController.IsExternallyControlled)
            {
                MotionBridge.SetVelocity(Rb, Vector3.zero, undamped: true);
                StateMachine.V8Fall(0f, true);
            }
        }

        public override void FixedUpdate()
        {
            StyleOverrides style = StyleOverrides.Current;
            VoMotor motor = Motor;
            if (Phase == DivePhase.Windup)
            {
                // 예비: 공중에 멈칫 + 상대 쪽으로 선회
                Hover();
                if (motor.HasOpponent)
                {
                    float max = VoUnits.AngleUnitsToDegrees(style.EffectiveCombat.jumpMelee.trackRate);
                    motor.AddYaw(Mathf.Clamp(Mathf.DeltaAngle(motor.Yaw, motor.OpponentBearing), -max, max));
                }
                _frame++;
                if (_frame >= style.diveStrikeWindupFrames) BeginDive(style);
                return;
            }

            // 지난 틱 적분에서 땅에 닿았으면 끝
            if (motor.IsGrounded)
            {
                Land(style);
                return;
            }
            Transform opponent = motor.Host.Opponent;
            if (opponent != null && opponent.gameObject.activeInHierarchy)
            {
                _direction = DiveStrikeMath.Steer(_direction, motor.Position, VoMotor.OpponentPosition(opponent),
                    VoUnits.AngleUnitsToDegrees(style.diveStrikeHomingRate), RemainingHorizontal(style), style.diveStrikeMinAngle);
            }
            ApplyDiveVelocity(style);
        }

        public override void Exit()
        {
            _hasLast = false;
        }

        private float RemainingHorizontal(StyleOverrides style) => Mathf.Max(0f, style.diveStrikeMaxHorizontal - _travelled);

        private void BeginDive(StyleOverrides style)
        {
            Phase = DivePhase.Dive;
            _frame = 0;
            VoMotor motor = Motor;
            Transform opponent = motor.Host.Opponent;
            // 상대가 없으면 정면 아래로
            Vector3 target = opponent != null && opponent.gameObject.activeInHierarchy
                ? VoMotor.OpponentPosition(opponent)
                : motor.Position + motor.Forward * style.diveStrikeMaxHorizontal + Vector3.down * 100f;
            _direction = DiveStrikeMath.Aim(motor.Position, target, style.diveStrikeMaxHorizontal, style.diveStrikeMinAngle);
            ApplyDiveVelocity(style);
        }

        /// <summary>이번 틱 속도: 진로 × 속력. 수평 이동 상한을 다 썼으면 수평은 0(그 자리에서 떨어진다). 몸은 진로의 수평 방향을 본다.</summary>
        private void ApplyDiveVelocity(StyleOverrides style)
        {
            VoMotor motor = Motor;
            float speed = Mathf.Max(0f, style.diveStrikeSpeed);
            Vector3 velocity = _direction * speed;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            float step = flat.magnitude * motor.Dt;
            float remaining = RemainingHorizontal(style);
            if (step > remaining)
            {
                flat = step > 0.0001f ? flat * (remaining / step) : Vector3.zero;
                step = remaining;
            }
            _travelled += step;
            if (flat.sqrMagnitude > 0.01f) motor.SetYaw(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg);
            MotionBridge.SetVelocity(Rb, new Vector3(flat.x, velocity.y + GravityCompensation(), flat.z), undamped: true);
        }

        /// <summary>공중에 멈춘다.</summary>
        private void Hover()
        {
            MotionBridge.SetVelocity(Rb, new Vector3(0f, GravityCompensation(), 0f), undamped: true);
        }

        /// <summary>외부 속도 적분은 공중에서 중력 한 틱 치를 먼저 빼므로(리지드바디 중력이 켜져 있으면) 그만큼 더해 둔다 — 속도를 정한 그대로 움직이게.</summary>
        private float GravityCompensation()
        {
            VoMotor motor = Motor;
            Rigidbody rb = Rb;
            if (rb == null || !rb.useGravity || motor.IsGrounded) return 0f;
            return motor.V.gravity * motor.SpeedScale;
        }

        private void Land(StyleOverrides style)
        {
            MotionBridge.SetVelocity(Rb, Vector3.zero, undamped: true);
            bool connected = _connected;
            int lag = connected ? Motor.V.landingFrames : style.diveStrikeMissLandingFrames;
            // 빗나간 착지 경직은 공격으로 취소할 수 없다 (그게 벌이다)
            StateMachine.LandFor(lag, attackCancel: connected);
            Melee.NotifyDiveStrikeLanded(connected, lag);
        }

        /// <summary>틱 끝 (모든 기체가 움직인 뒤): 주먹 캡슐을 지난 틱 → 이번 틱으로 쓸어 상대 피격 캡슐과 겹치면 상대 기록에 쌓는다. 한 번만.</summary>
        public void CollectHits(MeleeSystem melee, MechIdentity opponent)
        {
            if (Phase != DivePhase.Dive)
            {
                _hasLast = false;
                return;
            }
            StyleOverrides style = StyleOverrides.Current;
            StyleOverrides.AttackCapsule shape = style.diveStrikeCapsule;
            Vector3 origin = Motor.Position;
            Vector3 near = origin + _direction * shape.start;
            Vector3 far = origin + _direction * shape.range;
            Vector3 fromNear = _hasLast ? _lastNear : near;
            Vector3 fromFar = _hasLast ? _lastFar : far;
            _lastNear = near;
            _lastFar = far;
            _hasLast = true;
            if (_hitQueued || opponent == null || !opponent.gameObject.activeInHierarchy) return;
            if (MeleeSystem.SweepGapToOpponent(fromNear, fromFar, near, far, shape.radius, opponent.gameObject) > 0f) return;

            VoCombatSpec c = style.EffectiveCombat;
            // 일반 근접 판정(맞히면 때린 쪽도 히트스톱, 가드·패리 가능). 피해·충격치 = 원본 점프 근접 A (1100 → 다운)
            HitInfo info = melee.NewHit(AttackType.DiveStrike, HitKind.Melee, c.Damage(c.damageJumpMelee), opponent.gameObject);
            info.Impact = c.impactJumpMelee;
            // 바로 위에서 꽂으면 수평 방향이 없다 — 진로의 수평 성분, 그것도 없으면 내 정면으로 민다
            Vector3 flat = new Vector3(_direction.x, 0f, _direction.z);
            if (flat.sqrMagnitude > 0.01f) info.Direction = flat.normalized;
            MeleeSystem.QueueHit(opponent.gameObject, info);
            _hitQueued = true;
        }

        /// <summary>내 내려찍기의 결과가 상대 쪽에서 정해졌다 (다음 틱 정산). 맞았거나 막혔으면 "맞힘" — 패리·무적은 빗나감과 같다.</summary>
        public void NotifyResult(HitResult result)
        {
            if (result == HitResult.Hit || result == HitResult.Guarded || result == HitResult.GuardBroken) _connected = true;
        }
    }
}
