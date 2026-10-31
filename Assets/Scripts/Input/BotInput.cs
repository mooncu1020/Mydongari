using UnityEngine;

namespace MyDongari.Input
{
    /// <summary>
    /// 연습 봇의 전투 버튼 입력. 플레이어의 InputReader와 같은 ICombatInputSource라서
    /// MeleeSystem은 봇인지 사람인지 모르고 같은 가드·패리 규칙을 적용한다.
    /// 봇 두뇌(MeleeDummy)가 SetGuard로 버튼을 누르고 뗀다.
    /// </summary>
    public class BotInput : MonoBehaviour, ICombatInputSource
    {
        public bool AttackHeld { get; private set; }
        public bool GuardHeld { get; private set; }
        /// <summary>가드를 새로 누른 그 프레임에만 true (패리 창은 새로 누른 가드에만 열린다).</summary>
        public bool GuardPressedThisFrame => GuardHeld && _guardPressFrame == Time.frameCount;
        /// <summary>공격 버튼을 누른 그 프레임에만 true. 봇도 플레이어처럼 "누른 순간"에만 공격한다.</summary>
        public bool AttackPressedThisFrame => _attackPressFrame == Time.frameCount;

        private int _guardPressFrame = -1;
        private int _attackPressFrame = -1;

        /// <summary>공격 버튼을 한 번 누른다 (누르고 있기 없음).</summary>
        public void PressAttack()
        {
            _attackPressFrame = Time.frameCount;
        }

        public void SetGuard(bool held)
        {
            if (held && !GuardHeld) _guardPressFrame = Time.frameCount;
            GuardHeld = held;
        }

        public void SetAttack(bool held)
        {
            AttackHeld = held;
        }

        /// <summary>버튼을 전부 뗀 상태로.</summary>
        public void Clear()
        {
            GuardHeld = false;
            AttackHeld = false;
            _guardPressFrame = -1;
            _attackPressFrame = -1;
        }
    }
}
