using System.Collections.Generic;
using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 전투 틱에 참여하는 기체 하나 (전투 사양서 4.1). 구현은 게임 쪽(전투 허브)이 한다 — 코어는 순서만 정한다.
    /// 이동기가 켜진 기체는 이동기 틱 안에서(IVoHost.SettleHits·EndTick) 전투가 돌고, 이동기가 없는 기체(연습 봇)는 TickStandalone으로 돈다.
    /// </summary>
    public interface IVoTickUnit
    {
        /// <summary>틱 순서 (1 = 1P 먼저). 0 이하면 맨 뒤.</summary>
        int PlayerIndex { get; }
        /// <summary>이번 틱에 돌릴지 (꺼진 기체는 건너뛴다).</summary>
        bool TickEnabled { get; }
        /// <summary>이 기체의 이동기 (없으면 null).</summary>
        VoMotor Motor { get; }
        /// <summary>
        /// 틱 맨 앞, 어느 기체도 움직이기 전: 지난 틱 끝에 모인 피격 기록을 판단만 해 둔다(막음·패리·맞딜·반응 단계).
        /// 두 기체가 같은 시점의 상태로 판단하므로 1P·2P가 대칭이다. 적용은 각자 틱의 정산 단계에서.
        /// </summary>
        void DecideHits();
        /// <summary>이동기가 없는(또는 꺼진) 기체의 한 틱: 정산 → 전투 처리 → 틱 끝.</summary>
        void TickStandalone();
        /// <summary>모든 기체가 움직인 뒤: 내 공격 판정을 하고 맞았으면 상대의 피격 기록에 쌓는다 (반응은 다음 틱).</summary>
        void CollectHits();
    }

    /// <summary>
    /// 고정 틱 (이동 사양서 1.1, 전투 사양서 4.1). 이동기·전투 기체가 하나라도 있으면 물리 주기를 60Hz로 바꾸고, 물리 스텝마다
    /// 카메라 → 피격 판단(전원) → 1P 전체 → 2P 전체 → 근접 판정 모으기(전원) 순서로 돌린다.
    /// 원본은 1P 근접 판정 → 2P 갱신 → 2P 근접 판정이라 같은 틱 맞교환에서 1P가 이긴다. 우리는 두 기체의 판정을 모두 모은 뒤
    /// 다음 틱에 동시에 반영한다(편차 — PLAN_CLEANUP 3절). 위치는 여전히 1P가 2P의 지난 틱을, 2P가 1P의 이번 틱을 본다.
    /// 전투의 모든 시간은 이 틱 수로 센다 — 화면 프레임률과 무관하다. 처음 등록될 때 스스로 만들어진다(씬 배선 없음).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class VoTickDriver : MonoBehaviour
    {
        private static VoTickDriver _instance;
        private static readonly List<VoMotor> Motors = new List<VoMotor>();
        private static readonly List<IVoTickUnit> Units = new List<IVoTickUnit>();
        private static readonly List<VoMotor> Ticked = new List<VoMotor>();
        private static float _originalFixedDeltaTime = -1f;

        /// <summary>지금까지 돈 틱 수. 전투 시간의 기준.</summary>
        public static int TickCount { get; private set; }

        public static void Register(VoMotor motor)
        {
            if (motor == null) return;
            if (!Motors.Contains(motor)) Motors.Add(motor);
            EnsureInstance();
            ApplyTimestep(motor.V.tickRate);
        }

        public static void Unregister(VoMotor motor)
        {
            Motors.Remove(motor);
            RestoreTimestepIfEmpty();
        }

        /// <summary>전투 기체 등록. tickRate = 틱 주파수(Hz).</summary>
        public static void Register(IVoTickUnit unit, float tickRate)
        {
            if (unit == null) return;
            if (!Units.Contains(unit)) Units.Add(unit);
            EnsureInstance();
            ApplyTimestep(tickRate);
        }

        public static void Unregister(IVoTickUnit unit)
        {
            Units.Remove(unit);
            RestoreTimestepIfEmpty();
        }

        private static void RestoreTimestepIfEmpty()
        {
            if (Motors.Count == 0 && Units.Count == 0 && _originalFixedDeltaTime > 0f)
            {
                // 아무도 안 남으면(씬 종료 등) 원래 물리 주기로
                Time.fixedDeltaTime = _originalFixedDeltaTime;
                _originalFixedDeltaTime = -1f;
            }
        }

        private static void ApplyTimestep(float rate)
        {
            if (_originalFixedDeltaTime < 0f) _originalFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 1f / Mathf.Max(1f, rate);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("VoTickDriver");
            _instance = go.AddComponent<VoTickDriver>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static int Order(int playerIndex) => playerIndex > 0 ? playerIndex : 99;

        private static int PlayerOrder(VoMotor motor) => Order(motor.Host != null ? motor.Host.PlayerIndex : 0);

        private static bool IsLive(VoMotor motor) => motor != null && motor.isActiveAndEnabled && motor.IsActive;

        private void FixedUpdate()
        {
            Motors.RemoveAll(m => m == null);
            Units.RemoveAll(u => u == null || (u is Object o && o == null));
            if (Motors.Count == 0 && Units.Count == 0) return;
            Motors.Sort((a, b) => PlayerOrder(a).CompareTo(PlayerOrder(b)));
            Units.Sort((a, b) => Order(a.PlayerIndex).CompareTo(Order(b.PlayerIndex)));
            TickCount++;

            // 1) 카메라: 지난 틱 기체 상태로
            foreach (VoMotor motor in Motors)
            {
                if (!IsLive(motor)) continue;
                ErrorLog.Run(() => motor.Host.TickCamera(motor), "VoTickDriver.camera");
            }
            // 2) 피격 판단: 지난 틱 끝에 모인 기록을 두 기체가 같은 시점 상태로 (적용은 각자 틱에서)
            foreach (IVoTickUnit unit in Units)
            {
                if (unit.TickEnabled) ErrorLog.Run(unit.DecideHits, "VoTickDriver.decide");
            }
            // 3) 1P 전체 → 2P 전체
            Ticked.Clear();
            foreach (IVoTickUnit unit in Units)
            {
                if (!unit.TickEnabled) continue;
                VoMotor motor = unit.Motor;
                if (IsLive(motor))
                {
                    Ticked.Add(motor);
                    ErrorLog.Run(motor.Tick, "VoTickDriver.tick");
                }
                else
                {
                    ErrorLog.Run(unit.TickStandalone, "VoTickDriver.standalone");
                }
            }
            foreach (VoMotor motor in Motors)
            {
                // 전투 기체가 아닌 이동기 (전투 컴포넌트 없이 이동만 하는 기체)
                if (!IsLive(motor) || Ticked.Contains(motor)) continue;
                ErrorLog.Run(motor.Tick, "VoTickDriver.tick");
            }
            // 4) 근접 판정 모으기: 둘 다 움직인 뒤의 위치로
            foreach (IVoTickUnit unit in Units)
            {
                if (unit.TickEnabled) ErrorLog.Run(unit.CollectHits, "VoTickDriver.collect");
            }
        }
    }
}
