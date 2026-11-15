using UnityEngine;

namespace MyDongari.Core.VirtualOn.States
{
    /// <summary>
    /// 지상 근접 (전투 사양서 1.2·1.3). 원본은 서기 상태 안의 무기 하위 상태인데, 여기서는 상태 하나로 뺐다(동작은 같다).
    /// 이동만 한다 — ① 파고들기(0 → 기준 속력) → ② 공격(기준 속력) → ③ 후딜(0), 진로는 기체 정면, 레버 이동·수동 선회 없음.
    /// 추적은 이동기의 자동 선회(근접 추적 모드)가 기체를 돌린다: 시작 틱에 상대 &lt; 100u일 때만, 다음 틱부터 15f, 안쪽 원 진입 즉시 끝.
    /// 점프·대시는 ① 전체, ② 판정 창 시작 전, ③ 전체에서 즉시 받는다(근접 취소).
    /// 무엇을 휘두르는지(변형·콤보 예약)와 명중·충격치는 호스트(게임)가 정한다 — 호스트는 Drive를 읽고 Queue로 다음 타를 예약한다.
    /// </summary>
    public class VoMeleeState : VoState
    {
        /// <summary>근접 구동 (단계·판정 창·추적 카운터).</summary>
        public VoMeleeDrive Drive { get; } = new VoMeleeDrive();

        private VoMeleeMove _move;
        private int _step;

        public VoMeleeState(IVoMachine sm) : base(sm)
        {
        }

        public override bool LocksOnTurboPress => Drive.CanCancel;
        public override bool LocksWithinWindow => Drive.CanCancel;

        public void Prepare(VoMeleeMove move, int step)
        {
            _move = move;
            _step = step;
        }

        public override void Enter()
        {
            Drive.Begin(_move, _step, Motor.HasOpponent, Motor.OpponentDistanceU, Motor.Host.CombatSpec);
            StartTrack();
        }

        /// <summary>마지막으로 나갈 때 휘두르기가 아직 진행 중이었는지 (= 점프·대시·경직 등으로 끊김). 끝까지 휘두르고 나가면 false. 로그용.</summary>
        public bool ExitedMidSwing { get; private set; }

        public override void Exit()
        {
            ExitedMidSwing = Drive.IsActive;
            Drive.Cancel();
            if (Motor.AutoTurn == VoMotor.AutoTurnMode.MeleeTrack) Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
        }

        private void StartTrack()
        {
            if (Drive.IsTracking) Motor.StartMeleeTrack(Drive.Move.trackRate);
            else Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
        }

        public override void Main()
        {
            if (IsStunned)
            {
                StateMachine.ChangeState(StateMachine.VoStand);
                return;
            }
            // 근접 취소: 잠금 없는 구간에서 점프·대시를 즉시 받는다
            if (Drive.CanCancel)
            {
                if (TryJump()) return;
                if (DashCondition())
                {
                    if (Motor.InCloseZone && Motor.LockedAfterPress) StateMachine.V8CloseStep(Motor.LockedDirection, false);
                    else StateMachine.V8GroundDash(Motor.LockedDirection, false, Motor.LockedNeutralClose);
                    return;
                }
            }

            float speed = Drive.Tick(Motor.Host.CombatSpec, V, Motor.HasOpponent, Motor.OpponentDistanceU, Motor.OpponentAngleOff,
                Motor.MeleeTrackSaturated, out bool finished);
            // 다음 타로 넘어갔으면 추적도 새로 (그 틱 거리 기준)
            if (Drive.SteppedThisTick) StartTrack();

            Motor.MoveDirection = Motor.Forward;
            Motor.HorizontalSpeed = speed;
            // 근접 감속·넘어가기 방지까지 끝난 값이라 이동 적분이 다시 곱하지 않게
            Motor.SpeedIsFinal = true;

            if (finished) StateMachine.ChangeState(Motor.Lever.IsNeutral ? (MechState)StateMachine.VoStand : StateMachine.VoWalk);
        }
    }
}
