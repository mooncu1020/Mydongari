namespace MyDongari.Game.Input
{
    /// <summary>
    /// 전투 버튼(공격·가드) 입력 소스. 플레이어는 InputReader가 구현하고,
    /// 구현체가 없는 기체(더미 AI)는 가드·공격 입력이 항상 false로 취급된다.
    /// 전투는 60Hz 틱에서 돌아 화면 프레임과 어긋나므로, "새로 눌렀나"는 누름 횟수가 바뀌었는지로 본다.
    /// </summary>
    public interface ICombatInputSource
    {
        bool AttackHeld { get; }
        bool GuardHeld { get; }
        /// <summary>공격 버튼을 누른 횟수 (누를 때마다 1 증가).</summary>
        int AttackPressCount { get; }
        /// <summary>가드 버튼을 누른 횟수.</summary>
        int GuardPressCount { get; }
    }
}
