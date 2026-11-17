using UnityEngine;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 기체 한 대의 히트스톱·근접 무적·경직(가드 경직) 카운터 — 순수 로직, 전부 틱 수 (전투 사양서 2.2·2.4).
    /// 히트스톱 중에는 경직 시간이 흐르지 않고 밀려남도 0이다. 무적 타이머는 히트스톱 중에도 준다.
    /// MeleeSystem이 틱마다 TickInvuln → (정산) → TickPush → … → EndTick 순서로 부른다.
    /// </summary>
    public class ReactionTimers
    {
        /// <summary>남은 히트스톱(틱).</summary>
        public int HitStop;
        /// <summary>남은 근접 무적(틱).</summary>
        public int MeleeInvuln;
        /// <summary>남은 경직·가드 경직(틱).</summary>
        public int PushFrames;
        public bool PushIsGuard;
        /// <summary>밀려나는 방향 (공격 방향, 수평).</summary>
        public Vector3 PushDirection;
        /// <summary>밀려나는 속력(u/f).</summary>
        public float PushSpeed;

        public bool IsHitStopped => HitStop > 0;
        public bool IsMeleeInvulnerable => MeleeInvuln > 0;
        public bool IsPushed => PushFrames > 0;
        public bool IsStaggered => PushFrames > 0 && !PushIsGuard;
        public bool IsGuardStaggered => PushFrames > 0 && PushIsGuard;
        /// <summary>이동·공격·스킬 입력을 받는지. 경직·가드 경직·히트스톱 중엔 전혀 처리하지 않는다(버퍼에도 안 남긴다).</summary>
        public bool AcceptsInput => !IsPushed && !IsHitStopped;

        public void BeginHitStop(int frames)
        {
            HitStop = Mathf.Max(HitStop, frames);
        }

        public void GrantInvuln(int frames)
        {
            MeleeInvuln = Mathf.Max(MeleeInvuln, frames);
        }

        public void BeginPush(int frames, float speed, Vector3 direction, bool guard)
        {
            direction.y = 0f;
            PushFrames = frames;
            PushIsGuard = guard;
            PushSpeed = speed;
            PushDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }

        /// <summary>틱마다: 무적 타이머는 히트스톱 중에도 준다.</summary>
        public void TickInvuln()
        {
            if (MeleeInvuln > 0) MeleeInvuln--;
        }

        /// <summary>
        /// 경직·가드 경직 한 틱. 이번 틱에 밀려나는 속력(u/f)을 돌려준다: 히트스톱이면 0(시간도 안 흐름),
        /// 아니면 PushSpeed × closeSlowScale(근접 감속), 마지막 틱은 0으로 멈춘다.
        /// </summary>
        public float TickPush(float closeSlowScale)
        {
            if (PushFrames <= 0 || IsHitStopped) return 0f;
            PushFrames--;
            return PushFrames > 0 ? PushSpeed * closeSlowScale : 0f;
        }

        /// <summary>틱 끝: 히트스톱 카운트. 이번 틱이 히트스톱이었으면 true.</summary>
        public bool EndTick()
        {
            if (HitStop <= 0) return false;
            HitStop--;
            return true;
        }

        public void Reset()
        {
            HitStop = 0;
            MeleeInvuln = 0;
            PushFrames = 0;
        }
    }
}
