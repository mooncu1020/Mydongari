using UnityEngine;

namespace MyDongari.Movement
{
    /// <summary>
    /// 대시 상태가 전투·연출에 보여 주는 것 (V8). 예전 DashState와 V8 지상·공중 대시가 같이 구현한다.
    /// 다른 코드는 MechStateMachine.ActiveDash로 지금 대시를 받아 쓴다.
    /// </summary>
    public interface IDashMove
    {
        /// <summary>지금 돌진 방향(수평, 크기 1). 외형이 몸을 트는 방향, 태클 판정 부채꼴 기준.</summary>
        Vector3 Heading { get; }
        /// <summary>대시 어택·태클이 발생 중인지.</summary>
        bool IsAttacking { get; }
        /// <summary>차징 태클이 됐는지.</summary>
        bool IsTackleCharged { get; }
        /// <summary>차징 0 ~ 1 (태클 피해·넉백 배율, HUD 바).</summary>
        float TackleChargePower { get; }
        /// <summary>시작 단계라 공격·점프를 못 받는지.</summary>
        bool IsInStartup { get; }
        /// <summary>공중 대시인지.</summary>
        bool IsAirborne { get; }
        /// <summary>공격 버튼으로 대시 어택·태클 요청. 받아들이면 true.</summary>
        bool RequestTackle();
        /// <summary>맞딜 판정용: seconds 안에 상대를 맞힐 상황인지.</summary>
        bool WouldHitWithin(float seconds);
        /// <summary>대시 공격끼리 맞딜로 끝남.</summary>
        void EndByClash();
        /// <summary>넉백을 받아 끝남.</summary>
        void EndByKnockback();
        /// <summary>몸끼리 부딪혀 끝남.</summary>
        void EndByBump();
        /// <summary>벽에 막혀 끝남.</summary>
        void EndByWall();
    }
}
