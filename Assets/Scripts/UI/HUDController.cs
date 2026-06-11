using UnityEngine;
using UnityEngine.UI;
using MyDongari.Mech;
using MyDongari.Combat;
using MyDongari.Movement;
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

        private UnityCamera _camera;

        private void Start()
        {
            _camera = UnityCamera.main;
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
            if (lockOnMarker == null || lockOnSystem == null) return;

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