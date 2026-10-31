namespace MyDongari.Input
{
    /// <summary>
    /// 전투 버튼(공격·가드) 입력 소스. 플레이어는 InputReader가 구현하고,
    /// 구현체가 없는 기체(더미 AI)는 가드·공격 입력이 항상 false로 취급된다.
    /// </summary>
    public interface ICombatInputSource
    {
        bool AttackHeld { get; }
        bool GuardHeld { get; }
        bool GuardPressedThisFrame { get; }
    }
}
