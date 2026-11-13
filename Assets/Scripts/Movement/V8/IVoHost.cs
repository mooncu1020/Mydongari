using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 한 틱의 조작 입력 (사양서 2.1의 레버·터보를 우리 조작으로 바꾼 결과). 호스트(VoInputAdapter)가 채워 준다.
    /// 이동기는 장치를 모른다 — 레버 방향, 선회 축, 버튼의 새 입력·유지만 받는다.
    /// </summary>
    public struct VoInputFrame
    {
        /// <summary>레버(두 레버를 같은 방향으로 민 것): x = 오른쪽, y = 앞.</summary>
        public Vector2 LeverStick;
        /// <summary>레버 차동(선회) 입력 −1 ~ 1, 오른쪽 +. 데드존·부호 유지 제곱은 이동기가 한다.</summary>
        public float TurnAxis;
        /// <summary>선회율과 무관하게 그대로 더할 선회(°). 마우스 예비 조작.</summary>
        public float ExtraTurnDegrees;
        /// <summary>터보(대시) 새 입력 / 유지.</summary>
        public bool TurboNew;
        public bool TurboHeld;
        /// <summary>레버 벌리기(점프) 새 입력 / 유지.</summary>
        public bool JumpNew;
        public bool JumpHeld;
        /// <summary>레버 모으기(급강하) 새 입력. 공중일 때만 이동기가 받아들인다.</summary>
        public bool DiveNew;
    }

    /// <summary>
    /// 이동기(코어)가 바깥에 묻는 것 전부. 코어는 이 인터페이스만 알고, 구현(VoHostAdapter)이 우리 게임(전투·입력·카메라)을 붙인다.
    /// 원본에 없는 우리 규칙(스턴, 콤보 감속, 가드 자동 선회, 기체끼리 분리·몸 충돌 반동)은 전부 이 뒤에 있다.
    /// </summary>
    public interface IVoHost
    {
        // ---- 수치 ----
        /// <summary>이동기 수치 (원본 단위).</summary>
        BernardTuning.V8Values Spec { get; }
        /// <summary>이 값 이상 위를 향한 면(법선 y)만 땅, 그보다 가파르면 벽.</summary>
        float GroundMinNormalY { get; }

        // ---- 입력 ----
        /// <summary>조작 입력이 있는 기체인지. 없으면 이동기는 켜지지 않는다.</summary>
        bool HasInput { get; }
        /// <summary>새 입력 판정 기준을 지금으로 맞춘다 (이동기가 켜질 때·라운드 시작).</summary>
        void ResetInput();
        /// <summary>이번 틱 입력. 틱마다 한 번.</summary>
        VoInputFrame ReadInput();

        // ---- 게임 상태 ----
        /// <summary>스턴 중 (원본의 경직·금지 플래그 자리). 상태 전환·선회가 멈춘다.</summary>
        bool IsStunned { get; }
        /// <summary>걷기·점프 수평 속력에 곱하는 배율 (우리 콤보 감속). 원본은 1.</summary>
        float MoveSpeedScale { get; }
        /// <summary>이번 틱을 이동 상태 대신 외부 속도로 움직여야 하는지 (넉백 유지·외부 제어·런지·넉다운 — 사양서 1.4-1 "넉백 보간" 자리).</summary>
        bool IsExternalMode { get; }
        /// <summary>
        /// 기본 자동 선회 모드에서 몸을 상대 쪽으로 돌려 달라는 요청 (우리 가드 자동 선회). 원본엔 없다 — false면 아무것도 안 한다.
        /// 선회율(각 단위/f): 상대가 정면 안일 때 / 밖일 때.
        /// </summary>
        bool TryGetAutoTurnToOpponent(out float insideRateUnits, out float outsideRateUnits);

        // ---- 상대·충돌 ----
        /// <summary>상대 기체 (없으면 null).</summary>
        Transform Opponent { get; }
        /// <summary>내 기체(자식 포함)의 콜라이더인지 — 지면·벽 판정에서 뺀다.</summary>
        bool IsOwnCollider(Collider collider);
        /// <summary>틱 순서 (1 = 1P 먼저, 사양서 1.1). 0 이하면 맨 뒤.</summary>
        int PlayerIndex { get; }
        /// <summary>이동 적분이 끝난 직후. 원본에 없는 기체끼리 분리·몸 충돌 반동을 여기서 한다 (MechMotor.Nudge로 위치를 고침).</summary>
        void AfterIntegrate(MechMotor motor);

        // ---- 카메라 ----
        /// <summary>틱 맨 앞: 지난 틱 기체 상태로 이 기체의 카메라를 한 틱 갱신 (사양서 1.1-1).</summary>
        void TickCamera(MechMotor motor);
    }
}
