using System.Collections.Generic;
using UnityEngine;
using MyDongari.Core;

namespace MyDongari.Input
{
    /// <summary>
    /// 임시 진단(L1): VERSUS 시작 직후 P2 이동 입력이 각 단계
    /// (PlayerInput → InputReader → 전투 입력 소스 → 상태머신 → MechController → 카메라)를 지나가는지 콘솔에 찍는다.
    /// GameManager가 Begin으로 시간창을 연다. 끄려면 GameManager의 Trace P2 Input 하나만 끄면 된다
    /// (시간창이 닫혀 있으면 각 단계는 시간 비교 한 번만 하고 끝난다).
    /// </summary>
    public static class InputTrace
    {
        private const int TracedPlayerIndex = 2;

        private static float _endTime = -1f;
        private static float _interval = 0.25f;
        private static readonly Dictionary<string, float> LastLogTime = new Dictionary<string, float>();

        // 도메인 리로드를 끈 플레이 모드에서도 지난 실행의 시간창이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _endTime = -1f;
            LastLogTime.Clear();
        }

        /// <summary>지금부터 duration초 동안, 단계마다 interval초 간격으로 찍는다 (실제 시간).</summary>
        public static void Begin(float duration, float interval)
        {
            _endTime = Time.unscaledTime + duration;
            _interval = interval;
            LastLogTime.Clear();
            Debug.Log($"[P2Trace] 시작: {duration:0.0}초 동안 {interval:0.00}초 간격");
        }

        /// <summary>이 기체·단계 로그를 지금 찍을지. P2 기체만, 시간창 안에서, 단계별 간격마다 한 번.</summary>
        public static bool ShouldLog(Component owner, string stage)
        {
            if (Time.unscaledTime > _endTime || owner == null) return false;
            MechIdentity id = owner.GetComponentInParent<MechIdentity>();
            if (id == null || id.PlayerIndex != TracedPlayerIndex) return false;

            float now = Time.unscaledTime;
            if (LastLogTime.TryGetValue(stage, out float last) && now - last < _interval) return false;
            LastLogTime[stage] = now;
            return true;
        }

        public static void Log(string stage, string message)
        {
            Debug.Log($"[P2Trace {Time.unscaledTime:0.00}] {stage}: {message}");
        }
    }
}
