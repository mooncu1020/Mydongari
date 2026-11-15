using UnityEngine;

namespace MyDongari.Game.Bot
{
    /// <summary>봇 눈에 보이는 상대의 행동 (화면으로 알 수 있는 것만 — 상대 입력은 모른다).</summary>
    public enum SeenAction
    {
        None,
        Stand,
        Walk,
        CloseStep,
        Dash,
        DashAttack,
        Jump,
        AirDash,
        Dive,
        Melee,
        JumpMelee,
        Lunge,
        Landing,
        Guard,
        Stagger,
        GuardStagger,
        Down
    }

    /// <summary>한 틱에 본 상대 (BotSenses가 틱마다 찍어 두고, 반응 지연만큼 지난 것을 두뇌에 준다).</summary>
    public struct OpponentView
    {
        public bool Valid;
        /// <summary>본 틱 (VoTickDriver.TickCount).</summary>
        public int Tick;
        public Vector3 Position;
        /// <summary>지난 틱에서 이번 틱까지 움직인 속도(m/s).</summary>
        public Vector3 Velocity;
        public float Yaw;
        public SeenAction Action;
        /// <summary>이 행동을 이어 온 틱 수 (바뀐 틱 = 0).</summary>
        public int ActionFrames;
        public bool Airborne;

        /// <summary>공격 동작 중인지 (펀치·뛰어 베기·런지·대시 공격).</summary>
        public bool Attacking => Action == SeenAction.Melee || Action == SeenAction.JumpMelee ||
                                 Action == SeenAction.Lunge || Action == SeenAction.DashAttack;
    }

    /// <summary>봇 자기 상태 (지연 없음 — 사람도 자기 기체는 바로 안다).</summary>
    public struct SelfView
    {
        public bool Valid;
        public Vector3 Position;
        public float Yaw;
        /// <summary>1u(m).</summary>
        public float U;
        public bool Grounded;
        public bool Airborne;
        public bool Standing;
        public bool Dashing;
        /// <summary>대시를 시작한 뒤 지난 틱 (대시 중이 아니면 0).</summary>
        public int DashFrames;
        public bool DashAllowed;
        public bool Melee;
        public int ComboStep;
        public bool Lunging;
        public bool LungeReady;
        public bool Guarding;
        /// <summary>경직·가드 경직·히트스톱 — 입력이 먹지 않는 중.</summary>
        public bool Locked;
        public bool KnockedDown;
        public bool WaitingWake;
        public bool Landing;
    }
}
