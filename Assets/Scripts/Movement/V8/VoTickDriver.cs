using System.Collections.Generic;
using UnityEngine;
using MyDongari.Core;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// V8 고정 틱 (사양서 1.1). V8 이동기가 하나라도 켜져 있으면 물리 주기를 60Hz로 바꾸고, 물리 스텝마다
    /// 카메라 → 1P 전체 → 2P 전체 순서로 돌린다. 1P는 2P의 지난 틱 위치를, 2P는 1P의 이번 틱 위치를 본다.
    /// 실행 순서를 늦게 둬서 같은 스텝에 전투 코드(런지·넉다운·앵커 클로)가 넣은 외부 속도가 이번 틱에 반영된다.
    /// 히트스톱(timeScale 0)이면 물리 스텝이 안 와서 틱도 멈춘다. 처음 켜질 때 스스로 만들어진다(씬 배선 없음).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class VoTickDriver : MonoBehaviour
    {
        private static VoTickDriver _instance;
        private static readonly List<MechMotor> Motors = new List<MechMotor>();
        private static float _originalFixedDeltaTime = -1f;

        public static void Register(MechMotor motor)
        {
            if (motor == null) return;
            if (!Motors.Contains(motor)) Motors.Add(motor);
            EnsureInstance();
            ApplyTimestep(motor.V.tickRate);
        }

        public static void Unregister(MechMotor motor)
        {
            Motors.Remove(motor);
            if (Motors.Count == 0 && _originalFixedDeltaTime > 0f)
            {
                // 다 꺼지면(예전 이동기로 돌림) 원래 물리 주기로
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

        private static int PlayerOrder(MechMotor motor)
        {
            MechIdentity id = motor.Controller != null ? motor.Controller.Identity : null;
            return id != null && id.PlayerIndex > 0 ? id.PlayerIndex : 99;
        }

        private void FixedUpdate()
        {
            Motors.RemoveAll(m => m == null);
            if (Motors.Count == 0) return;
            Motors.Sort((a, b) => PlayerOrder(a).CompareTo(PlayerOrder(b)));

            // 1) 카메라: 지난 틱 기체 상태로
            foreach (MechMotor motor in Motors)
            {
                if (!motor.isActiveAndEnabled || !motor.IsActive) continue;
                ErrorLog.Run(() =>
                {
                    MyDongari.Camera.MechCamera cam = motor.Controller.MechCamera;
                    if (cam == null && motor.Controller.Identity != null) cam = motor.Controller.Identity.MechCamera;
                    if (cam != null) cam.TickV8(motor);
                }, "VoTickDriver.camera");
            }
            // 2) 1P 전체 → 2P 전체
            foreach (MechMotor motor in Motors)
            {
                if (!motor.isActiveAndEnabled || !motor.IsActive) continue;
                ErrorLog.Run(motor.Tick, "VoTickDriver.tick");
            }
        }
    }
}
