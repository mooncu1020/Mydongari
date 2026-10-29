using MyDongari.Combat;
using MyDongari.Movement;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Visual;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyDongari.Combat.Skills
{
    /// <summary>
    /// 앵커 클로. 즉발 잡기였던 것을 피할 수 있는 투사체로 바꿨다(F1):
    /// 예비 동작(팔 당김 + 손끝 발광) → 발사 순간 상대 위치로 일직선 비행(유도 없음, 사이드스텝·슬라이딩으로 피함)
    /// → 맞으면 기존 끌어오기·파일드라이버 준비 / 사거리 끝까지 못 맞으면 와이어가 감겨 돌아온다.
    /// 쿨다운은 발사 순간부터. 와이어는 LineRenderer, 클로 머리·발광은 런타임에 만든 구.
    /// </summary>
    // 표시물을 외형 리그(MechVisualRig.LateUpdate)가 손을 움직인 뒤에 갱신하려고 뒤에 돈다
    [DefaultExecutionOrder(100)]
    public class AnchorClaw : MonoBehaviour
    {
        public enum ClawPhase { Ready, Windup, Flying, Retracting, Pulling }

        private TuningRef _tuning;
        private BernardTuning.AnchorClawValues T => _tuning.Get(this).AnchorClaw;

        private Transform _target;
        private Rigidbody _targetRb;
        private ExternalControlLock _targetLock;
        private Rigidbody _selfRb;
        private ExternalControlLock _selfLock;
        private float _cooldownTimer = 0f;
        private float _pullTimer = 0f;
        private bool _pileDriverReady = false;
        public bool IsPileDriverReady => _pileDriverReady;

        private ClawPhase _phase = ClawPhase.Ready;
        public ClawPhase Phase => _phase;
        private float _windupTimer;
        private Vector3 _headPosition;
        private Vector3 _flightDirection;
        private float _flownDistance;

        /// <summary>예비 동작 시작(팔 당김). 외형용.</summary>
        public event Action OnWindupStarted;
        /// <summary>클로가 손을 떠난 순간. 효과음·로그용.</summary>
        public event Action OnFired;
        /// <summary>앵커가 상대에게 박혔을 때(끌어오기 시작). 인자는 잡힌 대상. 로그용.</summary>
        public event Action<GameObject> OnGrabbed;
        /// <summary>사거리 끝까지 못 맞혔을 때(감겨 돌아오기 시작). 효과음·로그용.</summary>
        public event Action OnMissed;
        /// <summary>파일드라이버를 내리찍는 순간(판정 직전). 외형·로그용.</summary>
        public event Action OnPileDriverStarted;

        private LockOnSystem _lockOnSystem;
        private MeleeSystem _melee;
        private MechIdentity _identity;
        private MechVisualRig _rig;

        // 표시물은 기체 자식으로 두지 않는다 (기체 색 틴트·머리 높이 측정에 섞이지 않게)
        private GameObject _fxRoot;
        private LineRenderer _wire;
        private Transform _head;
        private Material _wireMaterial;
        private Material _headMaterial;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
            _melee = GetComponent<MeleeSystem>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _rig = GetComponent<MechVisualRig>();
            _selfRb = GetComponent<Rigidbody>();
            _selfLock = GetComponent<ExternalControlLock>();
            if (_selfLock == null) _selfLock = gameObject.AddComponent<ExternalControlLock>();
        }

        private void Update()
        {
            _cooldownTimer += Time.deltaTime;

            switch (_phase)
            {
                case ClawPhase.Windup: UpdateWindup(); break;
                case ClawPhase.Flying: UpdateFlight(); break;
                case ClawPhase.Retracting: UpdateRetract(); break;
                case ClawPhase.Pulling: _pullTimer += Time.deltaTime; break;
            }
        }

        private void LateUpdate()
        {
            // 손 위치는 외형 리그가 LateUpdate에서 움직인 뒤라야 맞으므로 표시물은 여기서 갱신
            UpdateFx();
        }

        /// <summary>예비 동작 시작. 쿨다운 중이거나 이미 쏘는 중·락온 목표가 없으면 아무것도 안 한다.</summary>
        public void TryActivate()
        {
            if (_phase != ClawPhase.Ready) return;
            if (_cooldownTimer < T.cooldown) return;
            if (_lockOnSystem == null || _lockOnSystem.Target == null) return;
            if (_melee != null && _melee.IsStunned) return;

            _target = _lockOnSystem.Target;
            _targetRb = _target.GetComponent<Rigidbody>();
            _phase = ClawPhase.Windup;
            _windupTimer = 0f;
            OnWindupStarted?.Invoke();
        }

        private void UpdateWindup()
        {
            // 예비 동작 중 스턴 = 취소 (펀치 발생 중 스턴과 같은 규칙). 아직 안 쐈으니 쿨다운도 안 쓴다
            if (_melee != null && _melee.IsStunned)
            {
                _phase = ClawPhase.Ready;
                _target = null;
                _targetRb = null;
                return;
            }

            _windupTimer += Time.deltaTime;
            if (_windupTimer >= T.windupTime) Fire();
        }

        private void Fire()
        {
            // 발사 순간의 상대 위치를 향해 일직선. 이후 상대가 움직여도 방향은 그대로(유도 없음)
            _headPosition = LaunchPoint();
            // 목표가 사라졌으면 상체 방향으로 (G2)
            Vector3 attackForward = MechFacing.AttackForward(transform);
            Vector3 aim = _target != null ? _target.position : _headPosition + attackForward;
            Vector3 direction = aim - _headPosition;
            _flightDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : attackForward;
            _flownDistance = 0f;
            _phase = ClawPhase.Flying;
            _cooldownTimer = 0f;
            OnFired?.Invoke();
        }

        private void UpdateFlight()
        {
            BernardTuning.AnchorClawValues t = T;
            float step = Mathf.Min(t.projectileSpeed * Time.deltaTime, Mathf.Max(0f, t.range - _flownDistance));

            // 이번 프레임에 지나간 길을 클로 굵기로 훑는다 (빠른 투사체가 상대를 건너뛰지 않게)
            if (step > 0f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(_headPosition, t.hitRadius, _flightDirection, step,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                foreach (RaycastHit hit in hits)
                {
                    if (!IsTargetCollider(hit.collider)) continue;
                    // 시작부터 겹쳐 있으면 distance = 0
                    if (hit.distance < nearest) nearest = hit.distance;
                }
                if (nearest < float.MaxValue)
                {
                    _headPosition += _flightDirection * nearest;
                    Grab();
                    return;
                }
            }

            _headPosition += _flightDirection * step;
            _flownDistance += step;
            if (_flownDistance >= t.range - 0.0001f)
            {
                _phase = ClawPhase.Retracting;
                OnMissed?.Invoke();
            }
        }

        /// <summary>
        /// 클로가 잡을 수 있는 콜라이더: 쏠 때 정한 목표(락온 대상)와 내 상대. 벽·바닥·자기 몸은 그냥 지나간다
        /// (경기장이 평지라 지형 충돌은 넣지 않았다 — QUESTIONS F1).
        /// </summary>
        private bool IsTargetCollider(Collider col)
        {
            if (col == null || _identity.IsOwnCollider(col)) return false;
            if (_target != null && (col.transform == _target || col.transform.IsChildOf(_target))) return true;
            return _identity.IsOpponentCollider(col);
        }

        private void UpdateRetract()
        {
            Vector3 hand = LaunchPoint();
            _headPosition = Vector3.MoveTowards(_headPosition, hand, T.retractSpeed * Time.deltaTime);
            if ((_headPosition - hand).sqrMagnitude < 0.0001f)
            {
                _phase = ClawPhase.Ready;
                _target = null;
                _targetRb = null;
            }
        }

        private void Grab()
        {
            if (_target == null)
            {
                _phase = ClawPhase.Retracting;
                return;
            }
            _targetRb = _target.GetComponent<Rigidbody>();
            _phase = ClawPhase.Pulling;
            _pullTimer = 0f;
            _pileDriverReady = true;

            // 당기는 동안 상대의 이동 로직(플레이어 State머신 / AI)이 동시에
            // 자기 힘을 넣지 못하게 잠근다. 없으면 자동으로 붙여줌.
            _targetLock = _target.GetComponent<ExternalControlLock>();
            if (_targetLock == null) _targetLock = _target.gameObject.AddComponent<ExternalControlLock>();
            _targetLock.Acquire();

            // 그랩이 박히는 순간: 저항 못 하게 짧은 경직을 걸어서 "훅 하고 걸렸다"는 걸 확실히 함
            MeleeSystem targetMelee = _target.GetComponent<MeleeSystem>();
            if (targetMelee != null) targetMelee.ApplyStun(T.hookImpactStun);

            // 나(공격자) 쪽도 같이 잠근다. 안 그러면 이동 입력을 계속 누르고 있을 때
            // 내가 타겟 쪽으로 걸어 들어가는 게 "내가 당겨지는" 것처럼 보인다.
            // 당기는 동안은 상대만 움직여야 "내가 당긴다"는 느낌이 정확히 산다.
            _selfLock.Acquire();
            if (_selfRb != null)
            {
                _selfRb.linearVelocity = new Vector3(0f, _selfRb.linearVelocity.y, 0f);
            }

            OnGrabbed?.Invoke(_target.gameObject);
        }

        private void FixedUpdate()
        {
            if (_phase != ClawPhase.Pulling) return;

            if (_target == null || _targetRb == null)
            {
                EndPull();
                return;
            }

            Vector3 dirToMe = (transform.position - _target.position).normalized;

            // 박히는 순간엔 확 끌려오다가(hookImpactSpeed) hookEaseTime에 걸쳐 pullSpeed로 잦아듦
            BernardTuning.AnchorClawValues t = T;
            float easeT = t.hookEaseTime > 0f ? Mathf.Clamp01(_pullTimer / t.hookEaseTime) : 1f;
            float currentSpeed = Mathf.Lerp(t.hookImpactSpeed, t.pullSpeed, easeT);

            _targetRb.linearVelocity = dirToMe * currentSpeed;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist < t.pullStopDistance || _pullTimer >= t.pullDuration)
            {
                _targetRb.linearVelocity = Vector3.zero;
                EndPull();
            }
        }

        private void EndPull()
        {
            _phase = ClawPhase.Ready;
            ReleaseTargetLock();
            _selfLock.Release();
        }

        private void ReleaseTargetLock()
        {
            if (_targetLock != null) _targetLock.Release();
            _targetLock = null;
        }

        /// <summary>
        /// 라운드 시작: 당기기 중단(잠금 해제), 날아가던 클로 회수, 파일드라이버 준비 해제, 쿨타임을 비워 바로 쓸 수 있게.
        /// </summary>
        public void ResetForRound()
        {
            if (_phase == ClawPhase.Pulling)
            {
                ReleaseTargetLock();
                _selfLock.Release();
            }
            _phase = ClawPhase.Ready;
            _pullTimer = 0f;
            _pileDriverReady = false;
            _target = null;
            _targetRb = null;
            _cooldownTimer = T.cooldown;
            UpdateFx();
        }

        /// <summary>파일드라이버. 준비 안 됐거나 너무 멀면 false (입력 버퍼가 다시 시도한다).</summary>
        public bool TryPileDriver()
        {
            if (!_pileDriverReady) return false;
            if (_target == null) return false;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist > T.pileDriverRange) return false;

            _pileDriverReady = false;
            OnPileDriverStarted?.Invoke();

            // 잡기 후속타라 가드·패리를 모두 무시한다 (HitInfo.PiercesGuard / IgnoresParry)
            HitInfo info = new HitInfo
            {
                Type = AttackType.PileDriver,
                Damage = T.pileDriverDamage,
                Attacker = gameObject
            };
            HitResult result = MeleeSystem.DeliverHit(_target.gameObject, info);

            // 넉다운 (R1): 히트스톱이 끝난 뒤 공격자 상체 방향으로 날린다. 전신 모션 기체는 히트스톱이
            // 외형 어퍼컷이 닿는 순간(uppercutRiseTime 뒤)에 걸리므로 그만큼 기다린 뒤 + 여유
            if (result == HitResult.Hit)
            {
                KnockdownController knockdown = KnockdownController.GetOrAdd(_target.gameObject);
                BernardTuning tuning = _tuning.Get(this);
                float delay = (MechVisualRig.HandlesPileDriverImpactOf(gameObject) ? tuning.Motion.uppercutRiseTime : 0f) + tuning.Knockdown.launchMargin;
                knockdown.Begin(MechFacing.AttackForward(transform), delay);
            }

            _target = null;
            return true;
        }

        // ---------------- 표시 (와이어·클로 머리·손끝 발광) ----------------

        /// <summary>발사 위치 = 왼손 끝(외형 리그의 펀치 지점). 리그가 없으면 기체 기준 launchOffset.</summary>
        private Vector3 LaunchPoint()
        {
            if (_rig != null && _rig.isActiveAndEnabled) return _rig.GetPunchPoint(false);
            return transform.TransformPoint(T.launchOffset);
        }

        private void UpdateFx()
        {
            bool showHead = _phase == ClawPhase.Windup || _phase == ClawPhase.Flying || _phase == ClawPhase.Retracting || _phase == ClawPhase.Pulling;
            bool showWire = _phase == ClawPhase.Flying || _phase == ClawPhase.Retracting || _phase == ClawPhase.Pulling;
            if (!showHead && _fxRoot == null) return;
            EnsureFx();

            BernardTuning.AnchorClawValues t = T;
            Vector3 hand = LaunchPoint();
            Vector3 headPosition = _phase == ClawPhase.Pulling && _target != null ? _target.position
                : _phase == ClawPhase.Windup ? hand
                : _headPosition;

            _head.gameObject.SetActive(showHead);
            if (showHead)
            {
                _head.position = headPosition;
                if (_phase == ClawPhase.Windup)
                {
                    // 손끝 발광: 예비 동작 동안 0 → glowSize로 커진다
                    float grow = t.windupTime > 0f ? Mathf.Clamp01(_windupTimer / t.windupTime) : 1f;
                    _head.localScale = Vector3.one * Mathf.Max(0.01f, t.glowSize * grow);
                    _headMaterial.color = t.glowColor;
                }
                else
                {
                    _head.localScale = Vector3.one * t.clawHeadSize;
                    _headMaterial.color = t.wireColor;
                }
            }

            _wire.enabled = showWire;
            if (showWire)
            {
                _wire.startWidth = t.wireWidth;
                _wire.endWidth = t.wireWidth;
                _wire.startColor = t.wireColor;
                _wire.endColor = t.wireColor;
                _wire.SetPosition(0, hand);
                _wire.SetPosition(1, headPosition);
            }
        }

        private void EnsureFx()
        {
            if (_fxRoot != null) return;
            // Sprites/Default는 빌드에 항상 포함되는 셰이더(GraphicsSettings)라 런타임 생성 머티리얼에 쓴다
            Shader shader = Shader.Find("Sprites/Default");
            _fxRoot = new GameObject($"{name}_AnchorClawFx");

            _wireMaterial = new Material(shader);
            _wire = _fxRoot.AddComponent<LineRenderer>();
            _wire.sharedMaterial = _wireMaterial;
            _wire.useWorldSpace = true;
            _wire.positionCount = 2;
            _wire.shadowCastingMode = ShadowCastingMode.Off;
            _wire.receiveShadows = false;
            _wire.enabled = false;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            // 보이기만: 판정은 SphereCast가 한다
            DestroyImmediate(head.GetComponent<Collider>());
            head.name = "ClawHead";
            head.transform.SetParent(_fxRoot.transform, false);
            _headMaterial = new Material(shader);
            MeshRenderer headRenderer = head.GetComponent<MeshRenderer>();
            headRenderer.sharedMaterial = _headMaterial;
            headRenderer.shadowCastingMode = ShadowCastingMode.Off;
            headRenderer.receiveShadows = false;
            _head = head.transform;
            head.SetActive(false);
        }

        private void OnDisable()
        {
            // 기체가 꺼지면(모드 전환) 날아가던 클로·와이어를 숨긴다. 당기기 중이었으면 잠금도 푼다
            if (_phase == ClawPhase.Pulling)
            {
                ReleaseTargetLock();
                if (_selfLock != null) _selfLock.Release();
            }
            if (_phase != ClawPhase.Ready)
            {
                _phase = ClawPhase.Ready;
                _target = null;
                _targetRb = null;
            }
            if (_fxRoot != null) _fxRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (_fxRoot != null) _fxRoot.SetActive(true);
        }

        private void OnDestroy()
        {
            if (_fxRoot != null) Destroy(_fxRoot);
            if (_wireMaterial != null) Destroy(_wireMaterial);
            if (_headMaterial != null) Destroy(_headMaterial);
        }
    }
}
