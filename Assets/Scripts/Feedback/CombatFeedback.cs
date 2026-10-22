using System;
using System.Collections.Generic;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Feedback
{
    /// <summary>
    /// 상태 피드백: 머리 위 팝업(PARRY / GUARD BREAK / CLASH / STUN / OVERHEAT / EXHAUSTED), 기체 색 틴트, 효과음.
    /// 전투 코드는 이 컴포넌트를 모른다. 기존 이벤트(OnHitResolved, OnLungeStarted, OnGrabbed)를 구독하고,
    /// 이벤트가 없는 상태(과열·탈진·가드, 틴트용 스턴)는 공개 프로퍼티를 매 프레임 읽어 변화만 감지한다.
    /// STUN 팝업은 상대에게 맞아서 생긴 스턴만 (태클 후 자기 경직은 표시 안 함 — C4 결정).
    /// GameManager 오브젝트에 하나 (없으면 GameManager가 자동 부착).
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        [Header("팝업")]
        [SerializeField] private bool enablePopups = true;
        [SerializeField] private PopupStyle popupStyle = new PopupStyle();
        [Tooltip("기체 렌더러 꼭대기에서 이만큼(m) 위에 띄운다")]
        [SerializeField] private float popupHeadMargin = 0.5f;

        [Header("팝업 문구·색")]
        [SerializeField] private string parryLabel = "PARRY";
        [SerializeField] private Color parryColor = new Color(0.3f, 0.9f, 1f);
        [SerializeField] private string guardBreakLabel = "GUARD BREAK";
        [SerializeField] private Color guardBreakColor = new Color(1f, 0.5f, 0.1f);
        [SerializeField] private string clashLabel = "CLASH";
        [SerializeField] private Color clashColor = Color.white;
        [SerializeField] private string stunLabel = "STUN";
        [SerializeField] private Color stunColor = new Color(1f, 0.9f, 0.2f);
        [SerializeField] private string overheatLabel = "OVERHEAT";
        [SerializeField] private Color overheatColor = new Color(1f, 0.25f, 0.15f);
        [SerializeField] private string exhaustedLabel = "EXHAUSTED";
        [SerializeField] private Color exhaustedColor = new Color(0.6f, 0.7f, 0.85f);

        [Header("색 틴트 (우선순위: 스턴 > 과열 > 가드)")]
        [SerializeField] private bool enableTint = true;
        [SerializeField] private Color stunTint = new Color(1f, 0.9f, 0.2f);
        [SerializeField] private Color overheatTint = new Color(1f, 0.2f, 0.1f);
        [SerializeField] private Color guardTint = new Color(0.3f, 0.6f, 1f);
        [Tooltip("원래 색에서 틴트 색 쪽으로 섞는 비율 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float tintStrength = 0.5f;
        [Tooltip("과열 틴트 깜빡임 속도(초당 회). 0이면 깜빡이지 않음")]
        [SerializeField] private float overheatPulseRate = 3f;
        [Tooltip("과열 깜빡임의 가장 옅은 비율 (tintStrength 대비)")]
        [Range(0f, 1f)] [SerializeField] private float overheatPulseMin = 0.4f;

        [Header("효과음 (비어 있으면 무음)")]
        [SerializeField] private AudioClip punchHitClip;
        [SerializeField] private AudioClip guardBlockClip;
        [SerializeField] private AudioClip parryClip;
        [SerializeField] private AudioClip guardBreakClip;
        [SerializeField] private AudioClip clashClip;
        [SerializeField] private AudioClip lungeStartClip;
        [Tooltip("대시 어택·차징 태클이 그대로 맞았을 때")]
        [SerializeField] private AudioClip tackleHitClip;
        [Tooltip("런지가 그대로 맞았을 때")]
        [SerializeField] private AudioClip lungeHitClip;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;

        /// <summary>기체 하나의 피드백 상태 (이전 프레임 플래그로 "막 그렇게 됐다"를 감지).</summary>
        private class MechFeedback
        {
            public GameObject Mech;
            public MeleeSystem Melee;
            public HeatGauge Heat;
            public BoostGauge Boost;
            public MechTint Tint;
            // 처음 팝업을 띄울 때 잰다 (모드에 따라 시작할 때 꺼져 있는 기체는 렌더러 크기를 못 잰다)
            public float HeadHeight = -1f;
            public bool WasOverheated;
            public bool WasExhausted;
        }

        private readonly List<MechFeedback> _mechs = new List<MechFeedback>();
        private readonly List<Action> _unsubscribers = new List<Action>();
        private AudioSource _audio;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            // 화면 분할에서 두 플레이어가 같은 크기로 듣게 2D로 재생
            _audio.spatialBlend = 0f;
        }

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            // 모드(VERSUS/PRACTICE)에 따라 상대가 바뀌므로 P2·연습 봇 모두 등록 (꺼진 기체는 건너뛴다)
            foreach (GameObject mech in gm.AllMechs) Register(mech);
        }

        private void Register(GameObject mech)
        {
            if (mech == null) return;
            var fb = new MechFeedback
            {
                Mech = mech,
                Melee = mech.GetComponent<MeleeSystem>(),
                Heat = mech.GetComponent<HeatGauge>(),
                Boost = mech.GetComponent<BoostGauge>(),
                Tint = new MechTint(mech)
            };
            _mechs.Add(fb);

            if (fb.Melee != null)
            {
                Action<HitInfo, HitResult> onHit = (info, result) => ErrorLog.Run(() => OnHitResolved(fb, info, result), "CombatFeedback.onHit");
                fb.Melee.OnHitResolved += onHit;
                MeleeSystem melee = fb.Melee;
                _unsubscribers.Add(() => melee.OnHitResolved -= onHit);
            }

            ThrusterLunge lunge = mech.GetComponent<ThrusterLunge>();
            if (lunge != null)
            {
                Action onLunge = () => ErrorLog.Run(() => Play(lungeStartClip), "CombatFeedback.onLunge");
                lunge.OnLungeStarted += onLunge;
                _unsubscribers.Add(() => lunge.OnLungeStarted -= onLunge);
            }

            // 앵커 클로 훅 경직은 ReceiveHit을 거치지 않으므로 잡기 이벤트로 STUN을 띄운다
            AnchorClaw claw = mech.GetComponent<AnchorClaw>();
            if (claw != null)
            {
                MechTuning tuning = mech.GetComponent<MechTuning>();
                Action<GameObject> onGrab = target => ErrorLog.Run(() =>
                {
                    BernardTuning values = tuning != null ? tuning.Values : BernardTuning.Fallback;
                    if (values.AnchorClaw.hookImpactStun > 0f) OnGrabbed(target);
                }, "CombatFeedback.onGrab");
                claw.OnGrabbed += onGrab;
                _unsubscribers.Add(() => claw.OnGrabbed -= onGrab);
            }
        }

        private float MeasureHeadHeight(GameObject mech)
        {
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in mech.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            // 렌더러가 없으면(더미 등) 대략 기체 높이
            float top = found ? bounds.max.y - mech.transform.position.y : 3f;
            return top + popupHeadMargin;
        }

        /// <summary>피격자(fb) 쪽에서 판정이 끝났을 때. 패리·가드 깨짐은 피격자 머리 위, 맞딜은 양쪽.</summary>
        private void OnHitResolved(MechFeedback fb, HitInfo info, HitResult result)
        {
            switch (result)
            {
                case HitResult.Parried:
                    Popup(fb, parryLabel, parryColor);
                    Play(parryClip);
                    break;

                case HitResult.GuardBroken:
                    // 가드 깨짐도 스턴이지만 GUARD BREAK가 이미 말해 주므로 STUN은 생략
                    Popup(fb, guardBreakLabel, guardBreakColor);
                    Play(guardBreakClip);
                    break;

                case HitResult.Clash:
                    Popup(fb, clashLabel, clashColor);
                    MechFeedback attacker = Find(info.Attacker);
                    if (attacker != null) Popup(attacker, clashLabel, clashColor);
                    Play(clashClip);
                    break;

                case HitResult.Guarded:
                    Play(guardBlockClip);
                    break;

                case HitResult.Hit:
                    if (info.Type == AttackType.Punch) Play(punchHitClip);
                    else if (info.Type == AttackType.Tackle || info.Type == AttackType.DashAttack) Play(tackleHitClip);
                    else if (info.Type == AttackType.Lunge) Play(lungeHitClip);
                    // 그대로 맞아서 스턴이 걸린 경우만 STUN (런지·차징 태클 등). 자기 경직은 이 경로로 오지 않는다
                    if (info.StunDuration > 0f) Popup(fb, stunLabel, stunColor);
                    break;
            }
        }

        private void OnGrabbed(GameObject target)
        {
            MechFeedback victim = Find(target);
            if (victim != null) Popup(victim, stunLabel, stunColor);
        }

        private void Update()
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                UpdateLogic();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "CombatFeedback.Update");
            }
        }

        private void UpdateLogic()
        {
            foreach (MechFeedback fb in _mechs)
            {
                if (fb.Mech == null) continue;
                bool active = fb.Mech.activeInHierarchy;

                bool stunned = active && fb.Melee != null && fb.Melee.IsStunned;
                bool overheated = active && fb.Heat != null && fb.Heat.IsOverheated;
                bool exhausted = active && fb.Boost != null && fb.Boost.IsExhausted;
                bool guarding = active && fb.Melee != null && fb.Melee.IsGuarding;

                // 막 그렇게 된 순간만 팝업 (STUN은 피격 이벤트 쪽에서)
                if (overheated && !fb.WasOverheated) Popup(fb, overheatLabel, overheatColor);
                if (exhausted && !fb.WasExhausted) Popup(fb, exhaustedLabel, exhaustedColor);
                fb.WasOverheated = overheated;
                fb.WasExhausted = exhausted;

                UpdateTint(fb, stunned, overheated, guarding);
            }
        }

        private void UpdateTint(MechFeedback fb, bool stunned, bool overheated, bool guarding)
        {
            if (!enableTint)
            {
                fb.Tint.Clear();
                return;
            }

            if (stunned)
            {
                fb.Tint.Apply(stunTint, tintStrength);
            }
            else if (overheated)
            {
                float pulse = overheatPulseRate > 0f
                    ? Mathf.Lerp(overheatPulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * overheatPulseRate * 2f * Mathf.PI))
                    : 1f;
                fb.Tint.Apply(overheatTint, tintStrength * pulse);
            }
            else if (guarding)
            {
                fb.Tint.Apply(guardTint, tintStrength);
            }
            else
            {
                fb.Tint.Clear();
            }
        }

        private void Popup(MechFeedback fb, string label, Color color)
        {
            if (!enablePopups || fb.Mech == null || !fb.Mech.activeInHierarchy) return;
            if (fb.HeadHeight < 0f) fb.HeadHeight = MeasureHeadHeight(fb.Mech);
            StatusPopup.Spawn(fb.Mech.transform, fb.HeadHeight, label, color, popupStyle);
        }

        private void Play(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, sfxVolume);
        }

        private MechFeedback Find(GameObject mech)
        {
            if (mech == null) return null;
            foreach (MechFeedback fb in _mechs)
            {
                if (fb.Mech == mech) return fb;
            }
            return null;
        }

        private void OnDisable()
        {
            // 꺼지거나 씬이 끝날 때 틴트가 남지 않게 원래 색으로
            foreach (MechFeedback fb in _mechs) fb.Tint.Clear();
        }

        private void OnDestroy()
        {
            foreach (Action unsubscribe in _unsubscribers) unsubscribe();
            _unsubscribers.Clear();
        }
    }
}
