using System;
using UnityEngine;

namespace MyDongari.Combat
{
    /// <summary>
    /// 공격 버튼 입력 버퍼. 공격은 버튼을 "누른 순간"에만 나가고(누르고 있기만 하면 아무것도 안 나감),
    /// 지금 못 나가는 공격(쿨다운·발생 시간·런지 중·스턴 등)은 bufferTime 동안 기억했다가 가능해지는 순간 발동한다.
    /// 무엇을 낼지는 발동 시점 상황으로 매번 다시 판단한다(누를 땐 펀치 거리였어도 발동 때 멀어졌으면 런지).
    /// 플레이어(WeaponSystem)와 연습 봇(MeleeDummy)이 같이 쓴다.
    /// </summary>
    public class AttackInputBuffer
    {
        // 게임 시간(scaled) 기준: 히트스톱 동안은 쿨다운·스턴처럼 버퍼도 같이 멈춘다
        private float _pressTime = -1f;
        private int _pressFrame = -1;

        public bool HasPress => _pressTime >= 0f;

        /// <summary>공격 버튼을 누른 순간.</summary>
        public void Press()
        {
            _pressTime = Time.time;
            _pressFrame = Time.frameCount;
        }

        public void Clear()
        {
            _pressTime = -1f;
        }

        /// <summary>
        /// 매 프레임 호출. 버퍼 시간 안이면 tryStart(지금 상황으로 판단해 공격을 시작, 못 하면 null)를 시도한다.
        /// 발동했으면 true와 그 공격 종류, 누른 프레임보다 뒤에 발동했으면 fromBuffer = true.
        /// </summary>
        public bool Tick(float bufferTime, Func<AttackType?> tryStart, out AttackType started, out bool fromBuffer)
        {
            started = AttackType.Punch;
            fromBuffer = false;
            if (_pressTime < 0f) return false;
            if (Time.time - _pressTime > bufferTime)
            {
                Clear();
                return false;
            }

            AttackType? result = tryStart();
            if (result == null) return false;

            started = result.Value;
            fromBuffer = Time.frameCount != _pressFrame;
            Clear();
            return true;
        }
    }
}
