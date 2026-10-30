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
        [Tooltip("상대 체력 바(작게). 상대는 이 HUD 주인 기체의 MechIdentity.Opponent")]
        [SerializeField] private LinearGauge opponentHpBarFill;

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
        private Canvas _canvas;
        private MechIdentity _identity;

        private void Start()
        {
            // 화면 분할이라 메인 카메라가 아니라 "이 HUD 주인의" 카메라로 화면 좌표를 계산해야 한다
            _camera = viewCamera;
            if (lockOnSystem != null) _identity = lockOnSystem.GetComponent<MechIdentity>();
            if (_camera == null && _identity != null) _camera = _identity.ViewCamera;
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Update()
        {
            UpdateHeat();
            UpdateBoost();
            UpdateHP();
            UpdateOpponentHP();
            UpdateTackleCharge();
            UpdateLockOn();
        }

        private void UpdateHeat()
        {
            if (heatGaugeArc == null || heatGauge == null) return;
            heatGaugeArc.SetFill(heatGauge.CurrentHeat / heatGauge.MaxHeat);
            heatGaugeArc.SetColor(heatGauge.IsOverheated
                ? Color.red
                : new Color(1f, 0.6f, 0f));
        }

        private void UpdateBoost()
        {
            if (boostGaugeArc == null || boostGauge == null) return;
            boostGaugeArc.SetFill(boostGauge.CurrentBoost / boostGauge.MaxBoost);
            boostGaugeArc.SetColor(boostGauge.IsExhausted
                ? Color.red
                : new Color(0.3f, 0.7f, 1f));
        }

        private void UpdateHP()
        {
            if (hpBarFill == null || playerHealth == null) return;
            hpBarFill.SetFill(playerHealth.CurrentHealth / playerHealth.MaxHealth);
        }

        private void UpdateOpponentHP()
        {
            if (opponentHpBarFill == null || _identity == null) return;
            // 상대는 라운드·모드(더미/2인)에 따라 바뀔 수 있어 매 프레임 따라간다
            Health opponentHealth = _identity.Opponent != null ? _identity.Opponent.GetComponent<Health>() : null;
            opponentHpBarFill.SetFill(opponentHealth != null ? opponentHealth.CurrentHealth / opponentHealth.MaxHealth : 0f);
        }

        private void UpdateTackleCharge()
        {
            if (tackleBarFill == null || mechStateMachine == null) return;

            if (!mechStateMachine.IsDashing)
            {
                tackleBarFill.SetFill(0f);
                tackleBarFill.SetColor(new Color(1f, 0.9f, 0f, 0f));
                return;
            }

            DashState sprintState = mechStateMachine.DashState;
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

            Vector3 screenPos = _camera.WorldToScreenPoint(lockOnSystem.Target.position);
            // 카메라 뒤쪽 목표는 화면에 거꾸로 찍히므로 숨긴다
            if (screenPos.z < 0f)
            {
                lockOnMarker.gameObject.SetActive(false);
                return;
            }
            lockOnMarker.gameObject.SetActive(true);

            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                lockOnMarker.position = screenPos;
                return;
            }

            // 화면 분할용 Screen Space - Camera 캔버스: 스크린 좌표를 캔버스 평면 위 월드 좌표로 바꿔야 한다
            RectTransform canvasRect = (RectTransform)_canvas.transform;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    canvasRect, screenPos, _canvas.worldCamera, out Vector3 worldPos))
            {
                lockOnMarker.position = worldPos;
            }
        }
    }
}