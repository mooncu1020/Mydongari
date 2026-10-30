using System;
using System.Collections.Generic;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Visual;

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
        [Tooltip("앵커 클로가 손을 떠날 때")]
        [SerializeField] private AudioClip anchorFireClip;
        [Tooltip("앵커 클로가 박혔을 때")]
        [SerializeField] private AudioClip anchorHitClip;
        [Tooltip("앵커 클로가 빗나가 감겨 돌아올 때")]
        [SerializeField] private AudioClip anchorMissClip;
        [Tooltip("발이 땅에 닿을 때 (하체 뼈가 있는 모델의 걸음·제자리 회전 스텝)")]
        [SerializeField] private AudioClip footstepClip;
        [Tooltip("발소리 크기 (sfxVolume에 곱함. 걸음 세기도 곱해진다)")]
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.6f;
        [Tooltip("임팩트 슬램이 그대로 맞았을 때")]
        [SerializeField] private AudioClip slamHitClip;
        [Tooltip("파일 드라이버가 맞았을 때 (bernard는 어퍼컷이 닿는 외형 타격 순간)")]
        [SerializeField] private AudioClip pileDriverHitClip;
        [Tooltip("먼지·착지: 런지 앞발 박기, 앵커 클로 버팀, 달리기 도약 착지")]
        [SerializeField] private AudioClip dustClip;
        [Range(0f, 1f)] [SerializeField] private float dustVolume = 0.7f;
        [Tooltip("스러스터 분사: 달리기 도약에서 박차는 순간")]
        [SerializeField] private AudioClip thrusterBurstClip;
        [Range(0f, 1f)] [SerializeField] private float thrusterBurstVolume = 0.8f;
        [Tooltip("호버 활주 루프: 전력질주 동안만 기체마다 반복 재생")]
        [SerializeField] private AudioClip hoverLoopClip;
        [Range(0f, 1f)] [SerializeField] private float hoverLoopVolume = 0.6f;
        [Tooltip("호버 루프가 켜지고 꺼질 때 소리 크기를 바꾸는 시간(초, 실제 시간)")]
        [SerializeField] private float hoverLoopFadeTime = 0.15f;
        [Tooltip("스러스터 디딤걸음(3~12m/s) 착지 발소리")]
        [SerializeField] private AudioClip strideStepClip;
        [Range(0f, 1f)] [SerializeField] private float strideStepVolume = 0.8f;
        [Tooltip("디딤걸음에서 발이 지면을 긁으며 미끄러지는 동안 반복 재생 (긁는 속도만큼 크게)")]
        [SerializeField] private AudioClip strideScrapeLoopClip;
        [Range(0f, 1f)] [SerializeField] private float strideScrapeVolume = 0.6f;
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
            // 흰색 번쩍임이 끝나는 실제 시각 (N1, 히트스톱 중에도 보이게 실제 시간)
            public float FlashUntil = -1f;
            public Color FlashColor = Color.white;
            public float FlashStrength;
            public float FlashEmission;
            // 호버 활주 루프 (O3). 기체마다 하나, 처음 필요할 때 만든다
            public MechStateMachine StateMachine;
            public AudioSource HoverLoop;
            // 디딤걸음 긁는 소리 루프 (P2)
            public MechVisualRig Rig;
            public AudioSource ScrapeLoop;
            // 넉다운 미끄러짐 먼지 (R2)
            public KnockdownController Knockdown;
            public float NextSlideDust;
            // 대시 중 발밑 긁힘 불꽃 (V3.1)
            public float NextDashScrape;
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
                Tint = new MechTint(mech),
                StateMachine = mech.GetComponent<MechStateMachine>(),
                Rig = mech.GetComponent<MechVisualRig>()
            };
            _mechs.Add(fb);

            if (fb.Melee != null)
            {
                Action<HitInfo, HitResult> onHit = (info, result) => ErrorLog.Run(() => OnHitResolved(fb, info, result), "CombatFeedback.onHit");
                fb.Melee.OnHitResolved += onHit;
                MeleeSystem melee = fb.Melee;
                _unsubscribers.Add(() => melee.OnHitResolved -= onHit);
            }

            if (fb.StateMachine != null)
            {
                DashState dash = fb.StateMachine.DashState;
                Action onDash = () => ErrorLog.Run(() => DashBurst(fb), "CombatFeedback.onDash");
                dash.OnDashStarted += onDash;
                _unsubscribers.Add(() => dash.OnDashStarted -= onDash);
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
                    Play(anchorHitClip);
                    BernardTuning values = tuning != null ? tuning.Values : BernardTuning.Fallback;
                    if (values.AnchorClaw.hookImpactStun > 0f) OnGrabbed(target);
                }, "CombatFeedback.onGrab");
                Action onFire = () => ErrorLog.Run(() => Play(anchorFireClip), "CombatFeedback.onClawFire");
                Action onMiss = () => ErrorLog.Run(() => Play(anchorMissClip), "CombatFeedback.onClawMiss");
                claw.OnGrabbed += onGrab;
                claw.OnFired += onFire;
                claw.OnMissed += onMiss;
                _unsubscribers.Add(() =>
                {
                    claw.OnGrabbed -= onGrab;
                    claw.OnFired -= onFire;
                    claw.OnMissed -= onMiss;
                });
            }

            RegisterFootsteps(mech);
            RegisterMotionImpacts(mech);
            RegisterKnockdown(fb);
        }

        /// <summary>넉다운 착지(큰 먼지 + 먼지 소리)·벽 충돌(먼지 + 소리). 미끄러지는 동안 먼지는 UpdateLogic에서.</summary>
        private void RegisterKnockdown(MechFeedback fb)
        {
            fb.Knockdown = fb.Mech.GetComponent<KnockdownController>();
            if (fb.Knockdown == null) return;
            GameObject mech = fb.Mech;
            Action<Vector3> onLand = position => ErrorLog.Run(() => KnockdownDust(mech, FootPoint(mech), 1f, playSound: true), "CombatFeedback.onKnockdownLand");
            Action<Vector3> onWall = point => ErrorLog.Run(() => KnockdownDust(mech, point, 0.8f, playSound: true), "CombatFeedback.onKnockdownWall");
            KnockdownController knockdown = fb.Knockdown;
            knockdown.OnLanded += onLand;
            knockdown.OnWallHit += onWall;
            _unsubscribers.Add(() =>
            {
                knockdown.OnLanded -= onLand;
                knockdown.OnWallHit -= onWall;
            });
        }

        private void KnockdownDust(GameObject mech, Vector3 position, float strength, bool playSound)
        {
            BernardTuning.HitFeelValues feel = TuningOf(mech).HitFeel;
            HitSparkFx.SpawnDust(position, strength, DustStyleOf(feel));
            if (playSound) Play(dustClip, dustVolume * Mathf.Clamp01(strength));
        }

        /// <summary>기체 콜라이더 바닥 (등·엉덩이가 땅에 닿는 높이).</summary>
        private static Vector3 FootPoint(GameObject mech)
        {
            Collider body = mech.GetComponent<Collider>();
            Vector3 p = mech.transform.position;
            if (body != null) p.y = body.bounds.min.y;
            return p;
        }

        /// <summary>리그의 모션 타격·착지 순간(런지 앞발 박기·앵커 클로 버팀 → 먼지, 파일 드라이버 외형 타격 → 불꽃).</summary>
        private void RegisterMotionImpacts(GameObject mech)
        {
            MechVisualRig rig = mech.GetComponent<MechVisualRig>();
            if (rig == null) return;
            Action<string, Vector3, float> onImpact = (kind, position, strength) => ErrorLog.Run(
                () => OnMotionImpact(mech, kind, position, strength), "CombatFeedback.onMotionImpact");
            rig.OnMotionImpact += onImpact;
            // 달리기 도약에서 박차는 순간 스러스터 분사 소리 (O3)
            Action<Vector3> onBurst = position => ErrorLog.Run(() => Play(thrusterBurstClip, thrusterBurstVolume), "CombatFeedback.onThrusterBurst");
            rig.OnThrusterBurst += onBurst;
            _unsubscribers.Add(() =>
            {
                rig.OnMotionImpact -= onImpact;
                rig.OnThrusterBurst -= onBurst;
            });
        }

        private void OnMotionImpact(GameObject mech, string kind, Vector3 position, float strength)
        {
            BernardTuning.HitFeelValues feel = TuningOf(mech).HitFeel;
            if (kind == "PileDriver")
            {
                // 판정 순간엔 생략했던 타격 이펙트·소리를 외형 타격 순간에 (세기 강)
                HitSparkFx.SpawnHit(position, Vector3.up, feel.heavy.effectScale, SparkStyle(feel, feel.sparkColor));
                Play(pileDriverHitClip);
                return;
            }
            // 디딤걸음 착지는 전용 발소리 (먼지 소리 대신)
            if (kind == "StrideLand") Play(strideStepClip, strideStepVolume);
            else Play(dustClip, dustVolume * Mathf.Clamp01(strength));
            HitSparkFx.SpawnDust(position, strength, DustStyleOf(feel));
        }

        /// <summary>먼지 모양 (착지·미끄러짐·런지·앵커 공통, 진공 포물선 — T3).</summary>
        private static HitSparkFx.DustStyle DustStyleOf(BernardTuning.HitFeelValues feel)
        {
            return new HitSparkFx.DustStyle
            {
                Color = feel.dustColor,
                Size = feel.dustSize,
                Count = feel.dustCount,
                Speed = feel.dustSpeed,
                Lifetime = feel.dustLifetime,
                Gravity = feel.dustGravity,
                ConeAngle = feel.dustConeAngle,
                Collide = feel.dustCollision
            };
        }

        private static BernardTuning TuningOf(GameObject mech)
        {
            MechTuning tuning = mech != null ? mech.GetComponent<MechTuning>() : null;
            return tuning != null ? tuning.Values : BernardTuning.Fallback;
        }

        private static HitSparkFx.Style SparkStyle(BernardTuning.HitFeelValues feel, Color sparkColor)
        {
            return new HitSparkFx.Style
            {
                Spark = sparkColor,
                Flash = feel.flashFxColor,
                SparkSize = feel.sparkSize,
                SparkCount = feel.sparkCount,
                SparkSpeed = feel.sparkSpeed,
                FlashSize = feel.flashFxSize,
                FlashTime = feel.flashFxTime
            };
        }

        /// <summary>
        /// 타격감 (N1, 피격자 fb 기준): 흰색 번쩍임(그대로 맞음·가드 브레이크), 타격 지점 불꽃·섬광(등급별 크기, 막으면 파란 불꽃·작게),
        /// 맞은 쪽 화면은 타격 방향으로 밀렸다 출렁이고 때린 쪽은 짧게 흔들린다. 패리는 아무것도 안 한다.
        /// </summary>
        private void ApplyHitFeel(MechFeedback fb, HitInfo info, HitResult result)
        {
            if (result == HitResult.Parried || result == HitResult.Invulnerable || fb.Mech == null) return;
            BernardTuning.HitFeelValues feel = TuningOf(fb.Mech).HitFeel;
            BernardTuning.HitGradeValues grade = feel.Grade(result == HitResult.Clash ? AttackStrength.Light : info.Strength);
            float scale = result == HitResult.Guarded ? feel.guardedScale : 1f;

            if (result == HitResult.Hit || result == HitResult.GuardBroken)
            {
                fb.FlashUntil = Time.unscaledTime + feel.flashTime;
                fb.FlashColor = feel.flashColor;
                fb.FlashStrength = feel.flashStrength;
                fb.FlashEmission = feel.flashEmission;
            }

            // 전신 모션 기체의 파일 드라이버는 외형 타격 순간에 리그가 흔들고 이펙트를 낸다 (OnMotionImpact)
            if (info.Type == AttackType.PileDriver && MechVisualRig.HandlesPileDriverImpactOf(info.Attacker)) return;

            Vector3 attackerPosition = info.Attacker != null ? info.Attacker.transform.position : fb.Mech.transform.position + fb.Mech.transform.forward;
            Vector3 source = attackerPosition + Vector3.up * feel.hitHeight;
            Collider body = fb.Mech.GetComponent<Collider>();
            Vector3 point = body != null ? body.ClosestPoint(source) : fb.Mech.transform.position + Vector3.up * feel.hitHeight;
            Vector3 sparkDirection = source - point;
            if (sparkDirection.sqrMagnitude < 0.0001f) sparkDirection = attackerPosition - fb.Mech.transform.position;
            Color sparkColor = result == HitResult.Guarded ? feel.guardSparkColor : feel.sparkColor;
            HitSparkFx.SpawnHit(point, sparkDirection, grade.effectScale * scale, SparkStyle(feel, sparkColor));

            Vector3 hitDirection = fb.Mech.transform.position - attackerPosition;
            hitDirection.y = 0f;
            MechIdentity victimId = fb.Mech.GetComponent<MechIdentity>();
            if (victimId != null && victimId.MechCamera != null)
            {
                victimId.MechCamera.AddDirectionalShake(hitDirection, grade.victimShake * scale, grade.shakeTime, feel.directionalShakeFrequency);
            }
            MechIdentity attackerId = info.Attacker != null ? info.Attacker.GetComponent<MechIdentity>() : null;
            if (attackerId != null && attackerId.MechCamera != null)
            {
                attackerId.MechCamera.AddShake(grade.attackerShake * scale, grade.shakeTime);
            }
        }

        private void RegisterFootsteps(GameObject mech)
        {
            MechVisualRig rig = mech.GetComponent<MechVisualRig>();
            if (rig == null) return;
            Action<bool, float> onStep = (left, intensity) => ErrorLog.Run(
                () => Play(footstepClip, footstepVolume * intensity), "CombatFeedback.onFootstep");
            rig.OnFootstep += onStep;
            _unsubscribers.Add(() => rig.OnFootstep -= onStep);
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
            ApplyHitFeel(fb, info, result);
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
                    else if (info.Type == AttackType.Slam) Play(slamHitClip);
                    // 전신 모션 기체의 파일 드라이버는 외형 타격 순간(OnMotionImpact)에 낸다
                    else if (info.Type == AttackType.PileDriver && !MechVisualRig.HandlesPileDriverImpactOf(info.Attacker)) Play(pileDriverHitClip);
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
                UpdateHoverLoop(fb, active && fb.StateMachine != null && fb.StateMachine.IsDashing);
                UpdateKnockdownSlideDust(fb, active);
                UpdateDashScrape(fb, active && fb.StateMachine != null && fb.StateMachine.IsDashing);
                float scrape = active && fb.Rig != null ? fb.Rig.ScrapeAmount : 0f;
                UpdateLoop(ref fb.ScrapeLoop, strideScrapeLoopClip, strideScrapeVolume * Mathf.Clamp01(scrape));
            }
        }

        /// <summary>
        /// 반복 소리 하나를 목표 크기로 (P2 긁는 소리). 목표가 0이면 페이드 후 멈춘다. 소스는 처음 필요할 때 GameManager에 붙인다.
        /// 페이드 빠르기는 호버 루프와 같은 hoverLoopFadeTime.
        /// </summary>
        private void UpdateLoop(ref AudioSource source, AudioClip clip, float volume)
        {
            if (clip == null)
            {
                if (source != null && source.isPlaying) source.Stop();
                return;
            }
            float target = volume * sfxVolume;
            if (source == null)
            {
                if (target <= 0f) return;
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
            }
            if (source.clip != clip) source.clip = clip;
            float step = hoverLoopFadeTime > 0f ? Time.unscaledDeltaTime / hoverLoopFadeTime : 1f;
            source.volume = Mathf.MoveTowards(source.volume, target, step * Mathf.Max(0.01f, sfxVolume));
            if (target > 0f && !source.isPlaying) source.Play();
            else if (target <= 0f && source.isPlaying && source.volume <= 0.0001f) source.Stop();
        }

        /// <summary>
        /// 대시 출발 분사 (V3.1): 기체를 덮는 큰 섬광이 1~2프레임(dashBurstTime) + 진행 반대쪽으로 튀는 불꽃.
        /// </summary>
        private void DashBurst(MechFeedback fb)
        {
            if (fb.Mech == null || fb.StateMachine == null) return;
            BernardTuning.HitFeelValues feel = TuningOf(fb.Mech).HitFeel;
            Collider body = fb.Mech.GetComponent<Collider>();
            Vector3 center = body != null ? body.bounds.center : fb.Mech.transform.position;
            float scale = Mathf.Max(0.01f, feel.dashBurstSparkScale);
            var style = new HitSparkFx.Style
            {
                Spark = feel.dashBurstColor,
                Flash = feel.dashBurstColor,
                SparkSize = feel.sparkSize,
                SparkCount = feel.sparkCount,
                SparkSpeed = feel.sparkSpeed,
                // SpawnHit이 섬광 크기에 scale을 곱하므로 미리 나눠 둔다
                FlashSize = feel.dashBurstSize / scale,
                FlashTime = feel.dashBurstTime
            };
            HitSparkFx.SpawnHit(center, -fb.StateMachine.DashState.Heading, scale, style);
        }

        /// <summary>대시 중 발밑에서 진행 반대쪽·위로 튀는 긁힘 불꽃 (V3.1). 히트스톱 중엔 멈춘다(게임 시간).</summary>
        private void UpdateDashScrape(MechFeedback fb, bool dashing)
        {
            if (!dashing || Time.time < fb.NextDashScrape) return;
            BernardTuning.HitFeelValues feel = TuningOf(fb.Mech).HitFeel;
            fb.NextDashScrape = Time.time + Mathf.Max(0.02f, feel.dashScrapeInterval);
            Vector3 back = -fb.StateMachine.DashState.Heading;
            HitSparkFx.SpawnHit(FootPoint(fb.Mech), (back + Vector3.up * 0.6f).normalized, feel.dashScrapeScale, SparkStyle(feel, feel.sparkColor));
        }

        /// <summary>넉다운 미끄러짐 동안 땅을 긁는 먼지 (R2). 히트스톱 중엔 멈춘다(게임 시간).</summary>
        private void UpdateKnockdownSlideDust(MechFeedback fb, bool active)
        {
            if (!active || fb.Knockdown == null || fb.Knockdown.Phase != KnockdownController.KnockPhase.Slide) return;
            if (Time.time < fb.NextSlideDust) return;
            fb.NextSlideDust = Time.time + Mathf.Max(0.02f, TuningOf(fb.Mech).Knockdown.slideDustInterval);
            KnockdownDust(fb.Mech, FootPoint(fb.Mech), 0.5f, playSound: false);
        }

        /// <summary>호버 활주 루프: 전력질주 동안만 기체마다 반복 재생, 켜고 끌 때 짧게 페이드 (O3).</summary>
        private void UpdateHoverLoop(MechFeedback fb, bool sprinting)
        {
            if (hoverLoopClip == null)
            {
                if (fb.HoverLoop != null && fb.HoverLoop.isPlaying) fb.HoverLoop.Stop();
                return;
            }
            if (fb.HoverLoop == null)
            {
                if (!sprinting) return;
                fb.HoverLoop = gameObject.AddComponent<AudioSource>();
                fb.HoverLoop.playOnAwake = false;
                fb.HoverLoop.loop = true;
                fb.HoverLoop.spatialBlend = 0f;
                fb.HoverLoop.volume = 0f;
            }
            AudioSource source = fb.HoverLoop;
            if (source.clip != hoverLoopClip) source.clip = hoverLoopClip;
            float target = sprinting ? hoverLoopVolume * sfxVolume : 0f;
            float step = hoverLoopFadeTime > 0f ? Time.unscaledDeltaTime / hoverLoopFadeTime : 1f;
            source.volume = Mathf.MoveTowards(source.volume, target, step * Mathf.Max(0.01f, hoverLoopVolume * sfxVolume));
            if (sprinting && !source.isPlaying) source.Play();
            else if (!sprinting && source.isPlaying && source.volume <= 0.0001f) source.Stop();
        }

        private void UpdateTint(MechFeedback fb, bool stunned, bool overheated, bool guarding)
        {
            if (!enableTint)
            {
                fb.Tint.Clear();
                return;
            }

            if (Time.unscaledTime < fb.FlashUntil)
            {
                // 맞은 순간 흰색 번쩍임이 다른 틴트보다 우선 (N1)
                fb.Tint.Apply(fb.FlashColor, fb.FlashStrength, fb.FlashEmission);
            }
            else if (stunned)
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

        private void Play(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, sfxVolume * volumeScale);
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
