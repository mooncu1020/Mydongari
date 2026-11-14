using System;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 공격 버튼 입력 버퍼 (틱 단위). 공격은 버튼을 "누른 순간"에만 나가고(누르고 있기만 하면 아무것도 안 나감),
    /// 지금 못 나가는 공격(공격 중·런지 중·경직 등)은 bufferFrames틱 동안 기억했다가 가능해지는 틱에 발동한다.
    /// 무엇을 낼지는 발동 시점 상황으로 매번 다시 판단한다(누를 땐 근접 구역이었어도 발동 때 벗어났으면 런지).
    /// 플레이어(WeaponSystem)와 연습 봇(MeleeDummy)이 같이 쓴다.
    /// </summary>
    public class AttackInputBuffer
    {
        // 누른 뒤 지난 틱. 음수면 기억한 입력 없음
        private int _age = -1;

        public bool HasPress => _age >= 0;

        /// <summary>공격 버튼을 누른 틱.</summary>
        public void Press()
        {
            _age = 0;
        }

        public void Clear()
        {
            _age = -1;
        }

        /// <summary>
        /// 틱마다 호출. 버퍼 안이면 tryStart(지금 상황으로 판단해 공격을 시작, 못 하면 null)를 시도한다.
        /// 발동했으면 true와 그 공격 종류, 누른 틱보다 뒤에 발동했으면 fromBuffer = true.
        /// </summary>
        public bool Tick(int bufferFrames, Func<AttackType?> tryStart, out AttackType started, out bool fromBuffer)
        {
            started = AttackType.Punch;
            fromBuffer = false;
            if (_age < 0) return false;
            if (_age > bufferFrames)
            {
                Clear();
                return false;
            }

            AttackType? result = tryStart();
            if (result == null)
            {
                _age++;
                return false;
            }

            started = result.Value;
            fromBuffer = _age > 0;
            Clear();
            return true;
        }
    }
}
