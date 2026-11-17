namespace MyDongari.Core.VirtualOn
{
    /// <summary>대시가 끝난 이유 (로그용).</summary>
    public enum DashExitReason
    {
        Other,
        Canceled,
        Stun,
        Tackle,
        Bump,
        Wall,
        Landed,
        /// <summary>지속 단계(1.4초)를 다 써서 끝 단계로.</summary>
        Timeout,
        /// <summary>지속 단계에서 △ 점프로 이어짐.</summary>
        Jump,
        /// <summary>V8.2: 근접 구역 중립 ○ 앞 대시가 0.25초 안 스틱으로 근접 스텝이 됨.</summary>
        CloseStep
    }
}
