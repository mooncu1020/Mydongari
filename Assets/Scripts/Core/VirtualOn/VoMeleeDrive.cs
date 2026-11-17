using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 근접 구동 (전투 사양서 1.2·1.3) — 순수 계산, 틱 단위. 한 번의 휘두르기를 ① 파고들기 → ② 공격 → ③ 후딜로 진행하고
    /// 틱마다 이번 틱의 전진 속력(u/f)을 낸다. 진행 방향은 항상 기체 정면이고(레버 무시), 추적은 기체를 돌려서 진로를 바꾼다(회전은 호출 쪽).
    /// 콤보: ② 단계에 다음 타가 예약돼 있으면 ② 끝에서 ③을 건너뛰고 다음 타의 ①로 간다.
    /// 지상 근접 상태(VoMeleeState), 점프 근접, 이동기가 없는 기체(연습 봇)가 같이 쓴다.
    /// </summary>
    public class VoMeleeDrive
    {
        public enum DrivePhase { None, Approach, Attack, Recovery }

        public VoMeleeMove Move { get; private set; }
        public DrivePhase Phase { get; private set; } = DrivePhase.None;
        /// <summary>지금 단계 안의 틱 번호 (0부터).</summary>
        public int Frame { get; private set; }
        /// <summary>몇 번째 타인지 (1부터). 콤보로 넘어갈 때마다 1씩 오른다.</summary>
        public int Step { get; private set; }
        /// <summary>추적 카운터. 0 = 추적 없음(시작 때 상대가 멀었거나 끝남) — 그러면 수평 이동도 없다.</summary>
        public int TrackCounter { get; private set; }
        public bool IsActive => Phase != DrivePhase.None;
        public bool IsTracking => TrackCounter > 0;
        /// <summary>방금 돈 틱이 판정 창 안이었는지 (명중 판정은 틱 끝에 이걸 본다).</summary>
        public bool HitActiveThisTick { get; private set; }
        /// <summary>방금 돈 틱에 다음 타로 넘어갔는지.</summary>
        public bool SteppedThisTick { get; private set; }

        private bool _hasQueued;
        private VoMeleeMove _queued;
        private bool _turnEnded;

        /// <summary>점프·대시로 취소할 수 있는 구간: ① 전체, ② 판정 창 시작 전, ③ 전체 (전투 사양서 1.2 "잠금 없음").</summary>
        public bool CanCancel =>
            Phase == DrivePhase.Approach || Phase == DrivePhase.Recovery || (Phase == DrivePhase.Attack && Frame < Move.hitStart);
        /// <summary>다음 타를 예약할 수 있는 구간 (② 단계).</summary>
        public bool CanQueue => Phase == DrivePhase.Attack && !_hasQueued;
        public bool HasQueued => _hasQueued;

        /// <summary>
        /// 휘두르기 시작. 시작 틱에 상대가 trackStartDistance(100u) 미만일 때만 추적 카운터가 시작된다 — 아니면 이 휘두르기는 제자리에서 나간다.
        /// </summary>
        public void Begin(VoMeleeMove move, int step, bool hasOpponent, float distanceU, VoCombatSpec c)
        {
            Move = move;
            Step = step;
            Frame = 0;
            _hasQueued = false;
            _turnEnded = false;
            HitActiveThisTick = false;
            SteppedThisTick = false;
            TrackCounter = hasOpponent && distanceU < c.trackStartDistance ? 1 : 0;
            Phase = move.approachFrames > 0 ? DrivePhase.Approach : DrivePhase.Attack;
        }

        /// <summary>② 단계에서 다음 타 예약. 예약했으면 true.</summary>
        public bool Queue(VoMeleeMove next)
        {
            if (!CanQueue) return false;
            _queued = next;
            _hasQueued = true;
            return true;
        }

        public void Cancel()
        {
            Phase = DrivePhase.None;
            TrackCounter = 0;
            _hasQueued = false;
            HitActiveThisTick = false;
            SteppedThisTick = false;
        }

        /// <summary>
        /// 이번 틱에 추적 회전을 해야 하는지: 카운터가 trackTurnFrames(15) 이하이고, 상대가 안쪽 원에 들어온 적이 없다.
        /// 시작 틱에는 돌지 않는다(다음 틱부터).
        /// </summary>
        public bool WantsTurn(VoCombatSpec c, bool inInnerCircle)
        {
            if (inInnerCircle) _turnEnded = true;
            return IsTracking && !_turnEnded && TrackCounter > 1 && TrackCounter <= c.trackTurnFrames + 1;
        }

        /// <summary>
        /// 한 틱. 이번 틱의 전진 속력(u/f, 보정 끝난 값)을 돌려준다. 히트스톱 중인 틱에는 부르지 않는다(단계가 멈춘다).
        /// 보정 순서 (전투 사양서 1.3): 근접 감속(거리 &lt; 200u면 × 0.78) → [히트스톱이면 0] → 카운터 32f 초과면 0·추적 종료
        /// → 거리 &gt; 20u이고 상대가 정면 ±22.5° 밖이면 × 0.5 → 정지 거리 이하면 0·추적 종료 → 이번 틱 이동량 ≤ 상대 거리 → 카운터가 없으면 0.
        /// turnSaturated = 이번 틱 추적 회전이 최대율에 걸렸는지(× 0.1). finished = 이 틱으로 휘두르기가 끝났는지.
        /// </summary>
        public float Tick(VoCombatSpec c, VoSpec v, bool hasOpponent, float distanceU, float angleOffDegrees, bool turnSaturated, out bool finished)
        {
            finished = false;
            SteppedThisTick = false;
            HitActiveThisTick = false;
            if (Phase == DrivePhase.None)
            {
                finished = true;
                return 0f;
            }

            // 단계별 기준 속력
            float speed;
            switch (Phase)
            {
                case DrivePhase.Approach:
                    speed = Move.baseSpeed * Frame / Mathf.Max(1, Move.approachFrames);
                    break;
                case DrivePhase.Attack:
                    speed = Move.baseSpeed;
                    HitActiveThisTick = Frame >= Move.hitStart && Frame < Move.hitEnd;
                    break;
                default:
                    speed = 0f;
                    break;
            }

            if (hasOpponent && distanceU < v.closeSlowDistance) speed *= v.closeSlowScale;
            if (turnSaturated) speed *= c.trackSaturatedScale;
            if (IsTracking && TrackCounter > c.trackMaxFrames)
            {
                speed = 0f;
                TrackCounter = 0;
            }
            if (hasOpponent)
            {
                if (distanceU > c.offAxisDistance && Mathf.Abs(Mathf.DeltaAngle(0f, angleOffDegrees)) > c.offAxisHalfAngle) speed *= c.offAxisScale;
                if (distanceU <= Move.stopDistance)
                {
                    speed = 0f;
                    TrackCounter = 0;
                }
                // 넘어가기 방지
                if (speed > distanceU) speed = distanceU;
            }
            if (!IsTracking) speed = 0f;
            else TrackCounter++;

            Advance(c, hasOpponent, distanceU, out finished);
            return speed;
        }

        private void Advance(VoCombatSpec c, bool hasOpponent, float distanceU, out bool finished)
        {
            finished = false;
            Frame++;
            switch (Phase)
            {
                case DrivePhase.Approach:
                    if (Frame >= Move.approachFrames)
                    {
                        Phase = DrivePhase.Attack;
                        Frame = 0;
                    }
                    break;
                case DrivePhase.Attack:
                    if (Frame < Move.attackFrames) break;
                    if (_hasQueued)
                    {
                        // 콤보: ③을 건너뛰고 다음 타의 ①로. 추적은 새로 시작한다
                        VoMeleeMove next = _queued;
                        int step = Step + 1;
                        Begin(next, step, hasOpponent, distanceU, c);
                        SteppedThisTick = true;
                    }
                    else
                    {
                        Phase = DrivePhase.Recovery;
                        Frame = 0;
                        if (Move.recoveryFrames <= 0)
                        {
                            Phase = DrivePhase.None;
                            finished = true;
                        }
                    }
                    break;
                case DrivePhase.Recovery:
                    if (Frame >= Move.recoveryFrames)
                    {
                        Phase = DrivePhase.None;
                        finished = true;
                    }
                    break;
            }
        }
    }
}
