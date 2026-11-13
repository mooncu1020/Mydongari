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
        [Tooltip("런지 쿨다운 호 (V9g, 예전 부스트 호 자리). 메뉴 Mydongari/Setup Lunge Cooldown Arc가 만들고 연결한다")]
        [SerializeField] private ArcGauge lungeCooldownArc;

        [Header("런지 쿨다운 호 (V9g)")]
        [Tooltip("쿨다운 중(차오르는 동안) 색")]
        [SerializeField] private Color lungeCoolingColor = new Color(0.3f, 0.7f, 1f, 0.45f);
        [Tooltip("다 차서 쓸 수 있을 때 색")]
        [SerializeField] private Color lungeReadyColor = new Color(0.3f, 0.7f, 1f, 1f);
        [Tooltip("다 찬 순간 반짝임 색")]
        [SerializeField] private Color lungeFlashColor = Color.white;
        [Tooltip("다 찬 순간 반짝이는 시간(초)")]
        [SerializeField] private float lungeFlashTime = 0.2f;
        [Tooltip("쿨다운 중 런지 입력을 눌렀을 때 호를 흔드는 시간(초)")]
        [SerializeField] private float lungeDeniedShakeTime = 0.2f;
        [Tooltip("흔드는 폭(캔버스 단위, 좌우)")]
        [SerializeField] private float lungeDeniedShakeAmplitude = 8f;
        [Tooltip("흔드는 빠르기(초당 왕복 수)")]
        [SerializeField] private float lungeDeniedShakeFrequency = 25f;
        [Tooltip("흔드는 동안 색 (\"아직 안 됨\")")]
        [SerializeField] private Color lungeDeniedColor = new Color(1f, 0.35f, 0.3f, 1f);

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
        [SerializeField] private Health playerHealth;
        [SerializeField] private LockOnSystem lockOnSystem;
        [SerializeField] private MechStateMachine mechStateMachine;
        [Tooltip("이 HUD가 올라가는 화면의 카메라. 비워 두면 lockOnSystem 기체의 카메라를 쓴다")]
        [SerializeField] private UnityCamera viewCamera;

        private UnityCamera _camera;
        private Canvas _canvas;
        private MechIdentity _identity;

        // 런지 쿨다운 호 (V9g). ThrusterLunge는 WeaponSystem이 런타임에 붙이기도 해서 Update에서 늦게 찾는다
        private ThrusterLunge _lunge;
        private Vector2 _lungeArcHome;
        private bool _lungeArcHomeSet;
        private bool _lungeWasCooling;
        // 실제 시간(unscaled) 기준 — 히트스톱 중에도 반짝임·흔들림이 보이게
        private float _lungeFlashUntil = -1f;
        private float _lungeShakeUntil = -1f;

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
            UpdateLungeCooldown();
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

        /// <summary>
        /// 런지 쿨다운 호 (V9g): 쿨다운 동안 0에서 차오르고, 다 찬 순간 잠깐 반짝인다.
        /// 쿨다운 중에 런지 입력이 버려지면(ThrusterLunge.OnCooldownDenied) 짧게 좌우로 흔든다.
        /// </summary>
        private void UpdateLungeCooldown()
        {
            if (lungeCooldownArc == null) return;
            if (_lunge == null)
            {
                if (lockOnSystem == null) return;
                _lunge = lockOnSystem.GetComponent<ThrusterLunge>();
                if (_lunge == null) return;
                _lunge.OnCooldownDenied += OnLungeCooldownDenied;
            }
            if (!_lungeArcHomeSet)
            {
                _lungeArcHome = lungeCooldownArc.rectTransform.anchoredPosition;
                _lungeArcHomeSet = true;
            }

            float now = Time.unscaledTime;
            bool cooling = _lunge.IsOnCooldown;
            float duration = _lunge.CooldownDuration;
            float fill = cooling && duration > 0f ? 1f - Mathf.Clamp01(_lunge.CooldownRemaining / duration) : 1f;
            // 다 차오른 순간만 반짝인다 — 라운드 리셋으로 쿨다운이 한 번에 풀린 경우(직전 채움이 낮음)는 빼고
            if (_lungeWasCooling && !cooling && lungeCooldownArc.fillAmount >= 0.9f) _lungeFlashUntil = now + lungeFlashTime;
            if (cooling) _lungeFlashUntil = -1f;
            _lungeWasCooling = cooling;

            bool shaking = cooling && now < _lungeShakeUntil;
            Color color = cooling ? lungeCoolingColor : lungeReadyColor;
            if (shaking) color = lungeDeniedColor;
            else if (now < _lungeFlashUntil) color = lungeFlashColor;

            lungeCooldownArc.SetFill(fill);
            lungeCooldownArc.SetColor(color);

            float offset = 0f;
            if (shaking && lungeDeniedShakeTime > 0f)
            {
                // 남은 시간에 비례해 잦아드는 좌우 흔들림
                float left = (_lungeShakeUntil - now) / lungeDeniedShakeTime;
                offset = Mathf.Sin(now * lungeDeniedShakeFrequency * Mathf.PI * 2f) * lungeDeniedShakeAmplitude * left;
            }
            lungeCooldownArc.rectTransform.anchoredPosition = _lungeArcHome + new Vector2(offset, 0f);
        }

        private void OnLungeCooldownDenied()
        {
            _lungeShakeUntil = Time.unscaledTime + lungeDeniedShakeTime;
        }

        private void OnDestroy()
        {
            if (_lunge != null) _lunge.OnCooldownDenied -= OnLungeCooldownDenied;
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

            IDashMove sprintState = mechStateMachine.IsDashing ? mechStateMachine.ActiveDash : null;
            if (sprintState == null)
            {
                tackleBarFill.SetFill(0f);
                tackleBarFill.SetColor(new Color(1f, 0.9f, 0f, 0f));
                return;
            }

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