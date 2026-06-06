using UnityEngine;
using UnityEngine.UI;
using MyDongari.Mech;
using MyDongari.Combat;
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

        [Header("참조")]
        [SerializeField] private HeatGauge heatGauge;
        [SerializeField] private BoostGauge boostGauge;
        [SerializeField] private Health playerHealth;
        [SerializeField] private LockOnSystem lockOnSystem;

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
    }
}