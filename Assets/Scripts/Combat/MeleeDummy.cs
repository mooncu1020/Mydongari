using System;
using MyDongari.Core;
using MyDongari.Input;
using MyDongari.Mech;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MyDongari.Combat
{
    /// <summary>연습 봇 행동 모드.</summary>
    public enum BotMode
    {
        Idle,
        AlwaysGuard,
        Approach,
        Lunger,
        Parrier,
        Mixed
    }

    /// <summary>
    /// 연습 봇 두뇌. 판정은 하나도 직접 하지 않는다 — 가드는 BotInput(ICombatInputSource)으로 "버튼을 눌러"
    /// MeleeSystem이 플레이어와 같은 규칙(재사용 대기·패리 창·가드 히트·과열 가드 깨짐·패리 반사)으로 처리하고,
    /// 공격은 BotInput 공격 버튼을 "누르고", 플레이어와 같은 입력 버퍼(AttackInputBuffer)가 발동 시점 상황으로
    /// 펀치(MeleeSystem.TryMeleeAttack) 또는 런지(ThrusterLunge.TryLunge)를 고른다.
    /// 다른 컴포넌트보다 먼저 Awake해야(실행 순서 -50) 런타임에 붙인 히트 게이지·입력 소스를 MeleeSystem이 찾는다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MeleeDummy : MonoBehaviour
    {
        [Header("행동 모드")]
        [SerializeField] private BotMode mode = BotMode.Approach;

        [Header("이동·펀치 (Approach·Parrier 공통)")]
        [Tooltip("이 거리(m) 안이면 상대를 향해 돌고 다가간다")]
        [SerializeField] private float detectionRange = 5f;
        [Tooltip("이 거리(m) 안이면 멈춰서 펀치")]
        [SerializeField] private float attackRange = 3f;
        [Tooltip("봇이 펀치를 내는 간격(초). 펀치 자체 쿨다운·과열 규칙은 MeleeSystem이 따로 건다")]
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private float moveSpeed = 5f;
        [Tooltip("상대 쪽으로 몸을 돌리는 속도. 펀치·런지가 정면 판정이라 안 돌면 안 맞는다")]
        [SerializeField] private float turnSpeed = 8f;

        [Header("Lunger")]
        [Tooltip("이보다 가까우면 물러난다(m)")]
        [SerializeField] private float lungerMinDistance = 6f;
        [Tooltip("이보다 멀면 다가간다(m). 이 거리 안에서 런지를 노린다")]
        [SerializeField] private float lungerMaxDistance = 12f;
        [Tooltip("런지 사이 간격(초) 최소·최대 (무작위)")]
        [SerializeField] private float lungeIntervalMin = 1.5f;
        [SerializeField] private float lungeIntervalMax = 3.5f;
        [Tooltip("상대가 정면에서 이 각도(°) 안일 때만 런지 (조준)")]
        [SerializeField] private float lungeAimAngle = 10f;

        [Header("Parrier")]
        [Tooltip("상대 공격 하나마다 패리를 시도할 확률")]
        [Range(0f, 1f)] [SerializeField] private float parryChance = 0.6f;
        [Tooltip("상대 공격 시작(예비 동작)을 보고 가드를 누를 수 있을 때까지 걸리는 시간(초). " +
                 "공격 발생 시간(펀치 0.1·대시 어택 0.12·태클 0.1)이나 런지 도착보다 늦으면 못 막는다")]
        [SerializeField] private float reactionTime = 0.08f;
        [Tooltip("패리 시도로 누른 가드를 유지하는 시간(초). 놓으면 재사용 대기 규칙이 그대로 걸린다")]
        [SerializeField] private float parryHoldTime = 0.3f;
        [Tooltip("패리에 성공하면 반격 펀치")]
        [SerializeField] private bool counterAfterParry = true;
        [Tooltip("패리 성공 후 반격 펀치까지(초)")]
        [SerializeField] private float counterDelay = 0.1f;

        [Header("Mixed")]
        [Tooltip("행동을 바꾸는 간격(초) 최소·최대 (무작위). AlwaysGuard·Approach·Lunger·Parrier 중에서 고른다")]
        [SerializeField] private float mixedSegmentMin = 4f;
        [SerializeField] private float mixedSegmentMax = 8f;

        private static readonly BotMode[] MixedPool = { BotMode.AlwaysGuard, BotMode.Approach, BotMode.Lunger, BotMode.Parrier };

        /// <summary>설정된 모드.</summary>
        public BotMode Mode => mode;
        /// <summary>지금 실제로 하는 행동. Mixed면 현재 고른 하위 행동, 아니면 Mode와 같다.</summary>
        public BotMode ActiveBehaviour => _active;
        /// <summary>모드가 바뀌었을 때(순환 단축키 등). UI 표시용.</summary>
        public event Action<BotMode> OnModeChanged;
        /// <summary>입력 버퍼로(누른 프레임보다 뒤에) 발동한 공격. 로그용 (플레이어의 WeaponSystem과 같은 이벤트).</summary>
        public event Action<AttackType> OnBufferedAttack;

        private readonly AttackInputBuffer _attackBuffer = new AttackInputBuffer();

        private Transform _player;
        private MechIdentity _identity;
        private MeleeSystem _meleeSystem;
        private ThrusterLunge _lunge;
        private BotInput _botInput;
        private Health _health;
        private Rigidbody _rb;
        private ExternalControlLock _controlLock;
        private TuningRef _tuning;

        private BotMode _active;
        // 인스펙터에서 플레이 중 모드를 바꿔도 반영되게, 마지막으로 적용한 모드를 기억한다
        private BotMode _appliedMode;
        private float _attackTimer;
        private float _nextLungeTime;
        private float _mixedSwitchTime;
        private float _guardHoldUntil = -1f;
        private float _pendingGuardTime = -1f;
        private float _pendingCounterTime = -1f;

        // 상대의 공격 발동을 보고 패리하려고 상대 컴포넌트 이벤트를 구독한다 (상대가 바뀌면 다시 구독)
        private MechIdentity _watchedOpponent;
        private MeleeSystem _watchedMelee;
        private ThrusterLunge _watchedLunge;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _rb = GetComponent<Rigidbody>();
            _controlLock = GetComponent<ExternalControlLock>();
            _identity = MechIdentity.GetOrAdd(gameObject);

            // 플레이어 기체와 같은 규칙을 거치도록 같은 부품을 갖춘다 (씬에 없으면 런타임에 붙임)
            _botInput = GetComponent<BotInput>();
            if (_botInput == null) _botInput = gameObject.AddComponent<BotInput>();
            if (GetComponent<HeatGauge>() == null) gameObject.AddComponent<HeatGauge>();
            if (GetComponent<BoostGauge>() == null) gameObject.AddComponent<BoostGauge>();
            _lunge = GetComponent<ThrusterLunge>();
            if (_lunge == null) _lunge = gameObject.AddComponent<ThrusterLunge>();

            // MeleeSystem.Awake가 이미 돌았더라도 입력 소스를 확실히 봇 입력으로
            if (_meleeSystem != null)
            {
                _meleeSystem.RebindComponents();
                _meleeSystem.SetInputSource(_botInput);
                _meleeSystem.OnHitResolved += OnOwnHitResolved;
            }

            _active = mode == BotMode.Mixed ? MixedPool[0] : mode;
            _appliedMode = mode;
        }

        private void OnDestroy()
        {
            if (_meleeSystem != null) _meleeSystem.OnHitResolved -= OnOwnHitResolved;
            WatchOpponent(null);
        }

        // ---------------- 모드 ----------------

        public void SetMode(BotMode newMode)
        {
            mode = newMode;
            _appliedMode = newMode;
            _mixedSwitchTime = 0f;
            _active = newMode == BotMode.Mixed ? PickMixed() : newMode;
            ResetBehaviour();
            OnModeChanged?.Invoke(mode);
        }

        /// <summary>다음 모드로 (Mixed 다음은 Idle).</summary>
        public void CycleMode()
        {
            int count = Enum.GetValues(typeof(BotMode)).Length;
            SetMode((BotMode)(((int)mode + 1) % count));
        }

        private BotMode PickMixed()
        {
            // 같은 행동이 연달아 나오지 않게
            BotMode next = _active;
            for (int i = 0; i < 4 && next == _active; i++) next = MixedPool[Random.Range(0, MixedPool.Length)];
            return next;
        }

        /// <summary>진행 중인 가드·예약 행동을 비운다 (모드 전환·라운드 리셋).</summary>
        private void ResetBehaviour()
        {
            _botInput.Clear();
            _attackBuffer.Clear();
            _guardHoldUntil = -1f;
            _pendingGuardTime = -1f;
            _pendingCounterTime = -1f;
            _attackTimer = 0f;
            _nextLungeTime = Time.time + Random.Range(lungeIntervalMin, lungeIntervalMax);
        }

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            // 상대는 GameManager가 Awake 이후·모드 전환 때 연결하니 매 프레임 참조를 갱신
            if (mode != _appliedMode) SetMode(mode);
            WatchOpponent(_identity.Opponent);
            _player = _identity.Opponent != null ? _identity.Opponent.transform : null;

            // 카운트다운·라운드 사이·사망·상대 없음: 버튼을 다 떼고 가만히
            if (!CanAct())
            {
                if (_botInput.GuardHeld || _pendingGuardTime >= 0f || _pendingCounterTime >= 0f) ResetBehaviour();
                return;
            }

            if (mode == BotMode.Mixed && Time.time >= _mixedSwitchTime)
            {
                _active = PickMixed();
                _mixedSwitchTime = Time.time + Random.Range(mixedSegmentMin, mixedSegmentMax);
                ResetBehaviour();
            }

            _attackTimer += Time.deltaTime;
            float dist = Vector3.Distance(transform.position, _player.position);

            switch (_active)
            {
                case BotMode.Idle:
                    _botInput.SetGuard(false);
                    break;

                case BotMode.AlwaysGuard:
                    // 계속 누르고 있는 가드: 가드 히트로 과열되면 MeleeSystem이 깨고, 스턴이 풀리면 다시 올라간다
                    _botInput.SetGuard(true);
                    break;

                case BotMode.Approach:
                    _botInput.SetGuard(false);
                    if (dist <= attackRange) TryAttack();
                    break;

                case BotMode.Lunger:
                    _botInput.SetGuard(false);
                    UpdateLunger(dist);
                    break;

                case BotMode.Parrier:
                    UpdateParrier();
                    break;
            }

            UpdateAttackInput();
        }

        /// <summary>이번 프레임 누른 공격 버튼 + 버퍼에 남은 입력을 플레이어와 같은 규칙으로 처리.</summary>
        private void UpdateAttackInput()
        {
            if (_botInput.AttackPressedThisFrame) _attackBuffer.Press();
            float bufferTime = _tuning.Get(this).Startup.inputBuffer;
            if (_attackBuffer.Tick(bufferTime, TryStartAttack, out AttackType started, out bool fromBuffer) && fromBuffer)
            {
                OnBufferedAttack?.Invoke(started);
            }
        }

        /// <summary>
        /// 발동 시점 상황으로 공격을 고른다 — 플레이어와 같은 기준: 펀치 전환 거리(6m) 안이면 펀치, 밖이면 런지.
        /// 스턴·쿨다운·발생 중·런지 중이면 null (버퍼가 다시 시도).
        /// </summary>
        private AttackType? TryStartAttack()
        {
            if (_player == null) return null;
            if (_meleeSystem != null && _meleeSystem.IsStunned) return null;

            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist <= _tuning.Get(this).LockOn.meleeSwitchRange)
            {
                return _meleeSystem != null && _meleeSystem.TryMeleeAttack() ? AttackType.Punch : (AttackType?)null;
            }
            return _lunge != null && _lunge.TryLunge() ? AttackType.Lunge : (AttackType?)null;
        }

        private void FixedUpdate()
        {
            if (!CanAct()) return;
            if (_controlLock == null) _controlLock = GetComponent<ExternalControlLock>();
            if (_controlLock != null && _controlLock.IsLocked) return; // 앵커클로 등에 잡혀있으면 양보
            if (_lunge != null && _lunge.IsLunging) return;            // 돌진은 ThrusterLunge가 진행
            if (_meleeSystem != null && _meleeSystem.IsStunned) return;

            float dist = Vector3.Distance(transform.position, _player.position);

            switch (_active)
            {
                case BotMode.Idle:
                    break;

                case BotMode.AlwaysGuard:
                    FaceTarget();
                    break;

                case BotMode.Approach:
                case BotMode.Parrier:
                    if (dist <= detectionRange)
                    {
                        FaceTarget();
                        if (dist > attackRange) Move((_player.position - transform.position).normalized);
                    }
                    break;

                case BotMode.Lunger:
                    FaceTarget();
                    Vector3 toPlayer = (_player.position - transform.position).normalized;
                    if (dist < lungerMinDistance) Move(-toPlayer);
                    else if (dist > lungerMaxDistance) Move(toPlayer);
                    break;
            }
        }

        private bool CanAct()
        {
            if (_player == null) return false;
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive) return false;
            if (_health != null && _health.IsDead) return false;
            // 파일 드라이버 넉다운 중엔 아무것도 안 한다 — 버튼을 다 떼고, 이동·회전은 KnockdownController에 맡긴다 (R3)
            if (IsKnockedDown) return false;
            return true;
        }

        private KnockdownController _knockdown;

        private bool IsKnockedDown
        {
            get
            {
                // MeleeSystem이 없는 봇이면 앵커 클로가 맞힐 때 붙이므로 그때 찾는다
                if (_knockdown == null) _knockdown = GetComponent<KnockdownController>();
                return _knockdown != null && _knockdown.IsKnockedDown;
            }
        }

        // ---------------- 행동 ----------------

        private void TryAttack()
        {
            if (_attackTimer < attackCooldown) return;
            _attackTimer = 0f;
            _botInput.PressAttack();
        }

        private void UpdateLunger(float dist)
        {
            if (_lunge == null || _lunge.IsLunging) return;
            if (Time.time < _nextLungeTime) return;
            if (dist < lungerMinDistance || dist > lungerMaxDistance) return;
            if (!IsAimed(lungeAimAngle)) return;

            // 런지 거리(하드 락온 밖)라 버튼을 누르면 런지가 나간다. 부스트 부족 등으로 안 나가면 버퍼가 잠깐 다시 시도
            _botInput.PressAttack();
            _nextLungeTime = Time.time + Random.Range(lungeIntervalMin, lungeIntervalMax);
        }

        private void UpdateParrier()
        {
            if (_pendingGuardTime >= 0f && Time.time >= _pendingGuardTime)
            {
                _pendingGuardTime = -1f;
                PressGuard();
            }

            if (_guardHoldUntil >= 0f && Time.time >= _guardHoldUntil)
            {
                _guardHoldUntil = -1f;
                _botInput.SetGuard(false);
            }

            if (_pendingCounterTime >= 0f && Time.time >= _pendingCounterTime)
            {
                _pendingCounterTime = -1f;
                _guardHoldUntil = -1f;
                _botInput.SetGuard(false);
                // 반격도 일반 펀치 (쿨다운·과열·정면 부채꼴 규칙 그대로)
                _botInput.PressAttack();
            }
        }

        /// <summary>가드를 새로 누른다 → MeleeSystem이 패리 창을 연다. 이미 누르고 있으면 새 패리 창이 없으니 무시.</summary>
        private void PressGuard()
        {
            if (_botInput.GuardHeld || _meleeSystem == null) return;
            _botInput.SetGuard(true);
            _guardHoldUntil = Time.time + parryHoldTime;
            // 같은 프레임에 들어오는 공격에 대비해 가드 처리를 지금 한 번 돌린다 (규칙은 Update와 동일)
            _meleeSystem.ApplyGuardInputNow();
        }

        private bool ParrierActive => _active == BotMode.Parrier && CanAct();

        /// <summary>상대가 펀치·대시 어택·태클을 냈다. 발동과 명중이 같은 프레임이라 "읽었다" 치고 확률로 즉시 누른다.</summary>
        private void OnOpponentAttackStarted(AttackType type)
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                OnOpponentAttackStartedLogic(type);
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MeleeDummy.OnOpponentAttackStarted");
            }
        }

        private void OnOpponentAttackStartedLogic(AttackType type)
        {
            if (!ParrierActive) return;
            if (type != AttackType.Punch && type != AttackType.DashAttack && type != AttackType.Tackle) return;
            if (Random.value >= parryChance) return;

            // 런지와 같은 반응 방식: 상대 예비 동작을 보고 reactionTime 뒤, 판정 시각에 패리 창이 걸치도록 누른다.
            // 발생 시간이 반응 시간보다 짧으면(0 = 즉시 판정 포함) 늦어서 못 막는다
            BernardTuning.AttackStartupValues startup = TuningOf(_watchedOpponent).Startup;
            float hitIn = type == AttackType.Punch ? startup.punch
                : type == AttackType.Tackle ? startup.chargedTackle : startup.dashAttack;
            float window = _tuning.Get(this).Parry.window;
            _pendingGuardTime = Time.time + Mathf.Max(reactionTime, hitIn - window * 0.5f);
        }

        /// <summary>상대가 런지 돌진을 시작했다. 반응 시간 뒤, 예상 도착에 패리 창이 걸치도록 누른다.</summary>
        private void OnOpponentLungeStarted()
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                OnOpponentLungeStartedLogic();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MeleeDummy.OnOpponentLungeStarted");
            }
        }

        private void OnOpponentLungeStartedLogic()
        {
            if (!ParrierActive || _player == null) return;
            if (Random.value >= parryChance) return;

            BernardTuning opponentTuning = TuningOf(_watchedOpponent);
            BernardTuning.LungeValues lunge = opponentTuning.Lunge;
            float speed = Mathf.Max(0.1f, lunge.speed);
            float distance = Vector3.Distance(transform.position, _player.position);
            float eta;
            if (lunge.judgeMode == BernardTuning.LungeJudgeMode.StrikeAtRange)
            {
                // 새 방식(M4): 타격 거리까지 돌진 → 앞발 박기 → 뻗는 첫 순간에 맞는다
                eta = Mathf.Max(0f, distance - lunge.strikeDistance) / speed + lunge.plantTime;
            }
            else
            {
                // 상대 판정 구가 기체 앞쪽으로 나와 있어 그만큼 먼저 닿는다
                float reach = lunge.hitForwardOffset + lunge.hitRadius;
                eta = Mathf.Max(0f, distance - reach) / speed;
            }
            float window = _tuning.Get(this).Parry.window;
            _pendingGuardTime = Time.time + Mathf.Max(reactionTime, eta - window * 0.5f);
        }

        private void OnOwnHitResolved(HitInfo info, HitResult result)
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                OnOwnHitResolvedLogic(info, result);
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MeleeDummy.OnOwnHitResolved");
            }
        }

        private void OnOwnHitResolvedLogic(HitInfo info, HitResult result)
        {
            if (result == HitResult.Parried && counterAfterParry && _active == BotMode.Parrier)
            {
                _pendingCounterTime = Time.time + counterDelay;
            }
        }

        // ---------------- 보조 ----------------

        private void WatchOpponent(MechIdentity opponent)
        {
            if (opponent == _watchedOpponent) return;

            if (_watchedMelee != null) _watchedMelee.OnAttackStarted -= OnOpponentAttackStarted;
            if (_watchedLunge != null) _watchedLunge.OnLungeStarted -= OnOpponentLungeStarted;

            _watchedOpponent = opponent;
            _watchedMelee = opponent != null ? opponent.GetComponent<MeleeSystem>() : null;
            _watchedLunge = opponent != null ? opponent.GetComponent<ThrusterLunge>() : null;

            if (_watchedMelee != null) _watchedMelee.OnAttackStarted += OnOpponentAttackStarted;
            if (_watchedLunge != null) _watchedLunge.OnLungeStarted += OnOpponentLungeStarted;
        }

        private static BernardTuning TuningOf(MechIdentity mech)
        {
            MechTuning holder = mech != null ? mech.GetComponent<MechTuning>() : null;
            return holder != null ? holder.Values : BernardTuning.Fallback;
        }

        private bool IsAimed(float maxAngle)
        {
            Vector3 toTarget = _player.position - transform.position;
            toTarget.y = 0f;
            // 조준 판정도 공격과 같은 상체 방향 (G2)
            Vector3 forward = MechFacing.AttackForward(transform);
            return toTarget.sqrMagnitude < 0.0001f || Vector3.Angle(forward, toTarget) <= maxAngle;
        }

        private void Move(Vector3 dir)
        {
            dir.y = 0f;
            _rb.AddForce(dir.normalized * moveSpeed, ForceMode.Acceleration);

            Vector3 flat = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (flat.magnitude > moveSpeed)
            {
                flat = flat.normalized * moveSpeed;
                _rb.linearVelocity = new Vector3(flat.x, _rb.linearVelocity.y, flat.z);
            }
        }

        private void FaceTarget()
        {
            Vector3 toTarget = _player.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
        }
    }
}
