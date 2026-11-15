using UnityEngine;

namespace MyDongari.Core.VirtualOn.States
{
    /// <summary>
    /// 점프 근접 (전투 사양서 1.7): 점프 입력 후 8f 이내에 근접 조건(높이차 창 무시)이 성립하면 들어오는 전용 공중 행동 상태.
    /// vy = 3.80u/f로 뛰고, 진로는 기체 정면, 추적 회전율 2560 단위/f(14.06°/f). 수평은 지상 근접과 같은 구동(VoMeleeDrive)을 쓴다.
    /// 원본 확정은 vy·추적율·입력 창뿐이다 — 기준 속력, 단계 길이, 판정 창, 이 상태의 중력은 [미상]이라 호스트가 준 값(VoCombatSpec.jumpMelee…)으로 돈다.
    /// ② 단계가 끝나면 일반 낙하(착지 단계 12f), 그 전에 착지하면 그 자리에서 착지 단계로.
    /// </summary>
    public class VoJumpMeleeState : VoState
    {
        public VoMeleeDrive Drive { get; } = new VoMeleeDrive();

        public VoJumpMeleeState(IVoMachine sm) : base(sm)
        {
        }

        public override bool IsGroundState => false;
        public override bool IsAirState => true;

        public override void Enter()
        {
            VoCombatSpec c = Motor.Host.CombatSpec;
            Motor.Vy = c.jumpMeleeVy;
            Drive.Begin(c.jumpMelee, 1, Motor.HasOpponent, Motor.OpponentDistanceU, c);
            if (Drive.IsTracking) Motor.StartMeleeTrack(Drive.Move.trackRate);
            else Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
            StateMachine.NotifyJumped();
        }

        /// <summary>마지막으로 나갈 때 휘두르기가 아직 진행 중이었는지 (끊겼는지). 로그용.</summary>
        public bool ExitedMidSwing { get; private set; }

        public override void Exit()
        {
            ExitedMidSwing = Drive.IsActive;
            Drive.Cancel();
            if (Motor.AutoTurn == VoMotor.AutoTurnMode.MeleeTrack) Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
        }

        public override void Aux()
        {
            // y += vy → 착지 판정 → 중력·낙하 상한 (점프 하강 단계와 같은 순서, 적분은 한 번)
            if (Motor.AddVyAndCheckLanding())
            {
                StateMachine.Land(true);
                return;
            }
            VoCombatSpec c = Motor.Host.CombatSpec;
            Motor.Vy = Mathf.Max(-c.jumpMeleeFallMaxVy, Motor.Vy - c.jumpMeleeGravity);
        }

        public override void Main()
        {
            float speed = Drive.Tick(Motor.Host.CombatSpec, V, Motor.HasOpponent, Motor.OpponentDistanceU, Motor.OpponentAngleOff,
                Motor.MeleeTrackSaturated, out bool finished);
            Motor.MoveDirection = Motor.Forward;
            Motor.HorizontalSpeed = speed;
            Motor.SpeedIsFinal = true;
            // 공격이 끝나면 일반 낙하 (착지하면 착지 단계)
            if (finished) StateMachine.V8Fall(Motor.Vy, true);
        }
    }
}
