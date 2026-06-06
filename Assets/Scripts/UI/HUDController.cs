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
        [SerializeField] private Image heatGaugeFill;
        [SerializeField] private Image boostGaugeFill;

        [Header("HP")]
        [SerializeField] private Slider hpSlider;

        [Header("락온")]
        [SerializeField] private RectTransform lockOnMarker;

        [Header("태클 차지")]
        [SerializeField] private Image tackleChargeIndicator;

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
            UpdateLockOn();
            UpdateTackleCharge();
        }

        private void UpdateHeat()
        {
            if (heatGaugeFill == null || heatGauge == null) return;
            heatGaugeFill.fillAmount = heatGauge.CurrentHeat / 100f;
            heatGaugeFill.color = heatGauge.IsOverheated
                ? Color.red
                : new Color(1f, 0.6f, 0f);
        }

        private void UpdateBoost()
        {
            if (boostGaugeFill == null || boostGauge == null) return;
            boostGaugeFill.fillAmount = boostGauge.CurrentBoost / boostGauge.MaxBoost;
            boostGaugeFill.color = boostGauge.IsOverheated
                ? Color.red
                : new Color(0.3f, 0.7f, 1f);
        }

        private void UpdateHP()
        {
            if (hpSlider == null || playerHealth == null) return;
            hpSlider.value = playerHealth.CurrentHealth / 100f;
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

        private void UpdateTackleCharge()
        {
            if (tackleChargeIndicator == null || mechStateMachine == null) return;

            if (!mechStateMachine.IsSprinting)
            {
                tackleChargeIndicator.fillAmount = 0f;
                Color c = tackleChargeIndicator.color;
                c.a = 0f;
                tackleChargeIndicator.color = c;
                return;
            }

            SprintState sprintState = mechStateMachine.SprintState;
            float chargeAmount = sprintState.TackleChargePower;

            Color col = tackleChargeIndicator.color;
            col.a = sprintState.IsTackleCharged ? 1f : 0.5f;
            tackleChargeIndicator.color = col;
            tackleChargeIndicator.fillAmount = chargeAmount;
        }
    }
}