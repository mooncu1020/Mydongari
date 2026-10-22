using UnityEngine;
using UnityEngine.UI;
using MyDongari.Mech;
using MyDongari.Combat;
using MyDongari.Movement;
using MyDongari.Core;
using UnityCamera = UnityEngine.Camera;

namespace MyDongari.UI
{
    public class HUDController : MonoBehaviour
    {
        [Header("게이지")]
        [SerializeField] private ArcGauge heatGaugeArc;
        [SerializeField] private ArcGauge boostGaugeArc;

        [Header("HP")]
        [SerializeField] private LinearGauge hpBarFill;

        [Header("태클 차지")]
        [SerializeField] private LinearGauge tackleBarFill;

        [Header("락온")]
        [SerializeField] private RectTransform lockOnMarker;

        [Header("참조")]
        [SerializeField] private HeatGauge heatGauge;
        [SerializeField] private BoostGauge boostGauge;
        [SerializeField] private Health playerHealth;
        [SerializeField] private LockOnSystem lockOnSystem;
        [SerializeField] private MechStateMachine mechStateMachine;
        [Tooltip("이 HUD가 올라가는 화면의 카메라. 비워 두면 lockOnSystem 기체의 카메라를 쓴다")]
        [SerializeField] private UnityCamera viewCamera;

        private UnityCamera _camera;

        private void Start()
        {
            // 화면 분할이라 메인 카메라가 아니라 "이 HUD 주인의" 카메라로 화면 좌표를 계산해야 한다
            _camera = viewCamera;
            if (_camera == null && lockOnSystem != null)
            {
                MechIdentity identity = lockOnSystem.GetComponent<MechIdentity>();
                if (identity != null) _camera = identity.ViewCamera;
            }
        }

        private void Update()
        {
            UpdateHeat();
            UpdateBoost();
            UpdateHP();
            UpdateTackleCharge();
            UpdateLockOn();
        }

        private void UpdateHeat()
        {
            if (heatGaugeArc == null || heatGauge == null) return;
            heatGaugeArc.SetFill(heatGauge.CurrentHeat / 100f);
            heatGaugeArc.SetColor(heatGauge.IsOverheated
                ? Color.red
                : new Color(1f, 0.6f, 0f));
        }

        private void UpdateBoost()
        {
            if (boostGaugeArc == null || boostGauge == null) return;
            boostGaugeArc.SetFill(boostGauge.CurrentBoost / boostGauge.MaxBoost);
            boostGaugeArc.SetColor(boostGauge.IsOverheated
                ? Color.red
                : new Color(0.3f, 0.7f, 1f));
        }

        private void UpdateHP()
        {
            if (hpBarFill == null || playerHealth == null) return;
            hpBarFill.SetFill(playerHealth.CurrentHealth / 100f);
        }

        private void UpdateTackleCharge()
        {
            if (tackleBarFill == null || mechStateMachine == null) return;

            if (!mechStateMachine.IsSprinting)
            {
                tackleBarFill.SetFill(0f);
                tackleBarFill.SetColor(new Color(1f, 0.9f, 0f, 0f));
                return;
            }

            SprintState sprintState = mechStateMachine.SprintState;
            float chargeAmount = sprintState.TackleChargePower;
            Color col = sprintState.IsTackleCharged
                ? new Color(1f, 0.9f, 0f, 1f)
                : new Color(1f, 0.9f, 0f, 0.5f);
            tackleBarFill.SetColor(col);
            tackleBarFill.SetFill(chargeAmount);
        }

        private void UpdateLockOn()
        {
            if (lockOnMarker == null || lockOnSystem == null || _camera == null) return;

            if (lockOnSystem.Target == null)
            {
                lockOnMarker.gameObject.SetActive(false);
                return;
            }

            lockOnMarker.gameObject.SetActive(true);
            Vector3 screenPos = _camera.WorldToScreenPoint(lockOnSystem.Target.position);
            lockOnMarker.position = screenPos;
        }
    }
}