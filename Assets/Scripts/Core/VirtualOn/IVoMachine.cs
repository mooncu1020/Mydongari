using UnityEngine;
using MyDongari.Core.VirtualOn.States;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>대시 중 공격 한 틱의 결과.</summary>
    public enum VoDashAttackResult
    {
        /// <summary>공격 없음 — 대시가 평소대로 진행.</summary>
        None,
        /// <summary>공격 진행 중 — 대시는 진로·속력만 유지.</summary>
        Running,
        /// <summary>공격이 끝남(명중·빗나감) — 대시를 끝내야 한다.</summary>
        Finished
    }

    /// <summary>
    /// 지상 대시 지속 단계의 "무기 → 대시 공격" 자리 (사양서 4.4). 무엇이 나가는지(우리 대시 어택·차징 태클)는 호스트가 구현한다.
    /// </summary>
    public interface IVoDashAttack
    {
        bool IsAttacking { get; }
        bool IsTackleCharged { get; }
        float TackleChargePower { get; }
        void Reset();
        /// <summary>공격 중이 아니면 차징 시간을 센다 (대시 경과 틱).</summary>
        void TickCharge();
        /// <summary>공격 요청. 받아들이면 true.</summary>
        bool Request();
        /// <summary>한 틱. heading은 유도가 바꿀 수 있다. stop = 이번 틱 멈춤.</summary>
        VoDashAttackResult Tick(ref Vector3 heading, float speedMps, float snappedAngle, out bool stop);
        /// <summary>대시가 공격 도중에 끝났으면 판정 없이 취소.</summary>
        void CancelIfAttacking();
        /// <summary>맞딜로 소진.</summary>
        void ConsumeByClash();
    }

    /// <summary>
    /// 이동 상태가 상태 머신에 요구하는 것. 코어의 이동 상태(VoState)와 이동기는 이 인터페이스만 알고,
    /// 구현(MechStateMachine)이 우리 게임 상태(착지 경직·런지·넉다운)를 같이 돌린다.
    /// </summary>
    public interface IVoMachine
    {
        VoMotor Motor { get; }
        /// <summary>지금 상태 (이동 상태가 아닐 수도 있다 — 그때는 이동기가 Update·FixedUpdate를 틱마다 한 번씩 대신 부른다).</summary>
        MechState CurrentState { get; }
        /// <summary>상태가 돌고 있는지 (사망하면 꺼진다).</summary>
        bool StatesEnabled { get; }
        void ChangeState(MechState state);

        VoStandState VoStand { get; }
        VoWalkState VoWalk { get; }
        /// <summary>점프 시작. 못 하면 false.</summary>
        bool TryStartJumpV8();
        /// <summary>낙하 (발판 끝·공중 대시 끝·공중 피격). vy는 u/f.</summary>
        void V8Fall(float vy, bool landingLag);
        void V8GroundDash(int code, bool fromWalk, bool closeStepWindow);
        void V8CloseStep(int code, bool fromDash);
        void V8DashStop(Vector3 heading, float offset, bool airborne);
        void V8AirDash(int code);
        void V8Dive();
        /// <summary>착지. needsLag면 착지 단계(사양서 4.6)를 거친다.</summary>
        void Land(bool needsLag);

        bool IsDashing { get; }
        bool IsAirborne { get; }

        void NotifyDashStarted();
        void NotifyDashEnded(DashExitReason reason);
        void NotifyJumped();

        /// <summary>지상 대시가 쓸 대시 공격 진행기.</summary>
        IVoDashAttack CreateDashAttack();
    }
}
