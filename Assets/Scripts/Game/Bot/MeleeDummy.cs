using System;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Combat;
using MyDongari.Game.Flow;
using MyDongari.Game.Heat;
using MyDongari.Game.Input;
using MyDongari.Game.Knockdown;

namespace MyDongari.Game.Bot
{
    /// <summary>
    /// 연습 봇. 플레이어 기체와 똑같은 부품(가상 패드 InputReader·상태 머신·MechController/VoMotor·WeaponSystem·런지·히트 게이지)을
    /// 갖추고 "패드를 대신 누른다" — 이동은 V8 이동기, 공격 선택·가드·패리·기상은 플레이어와 같은 코드가 처리한다. 봇 전용 이동·판정은 없다.
    /// 한 틱: 눈(BotSenses: 상대는 반응 지연만큼 지난 장면, 자기는 지금) → 두뇌(BotBrain: 무엇을 할지) → 가상 패드(BotPad: 어떻게 누를지).
    /// 씬의 DummyTarget엔 이 컴포넌트·MeleeSystem·Health 정도만 있어도 된다 — 나머지는 Awake에서 붙인다(실행 순서 -50, 다른 부품의 Awake보다 먼저).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MeleeDummy : MonoBehaviour
    {
        [Header("행동 모드")]
        [SerializeField] private BotMode mode = BotMode.Approach;

        [Header("두뇌 (행동 선택)")]
        [SerializeField] private BotBrainTuning brain = new BotBrainTuning();

        [Header("가상 패드 (누르는 타이밍)")]
        [SerializeField] private BotPadTiming pad = new BotPadTiming();

        [Header("몸")]
        [Tooltip("켜면 시작할 때 상대(플레이어) 기체의 리지드바디 질량·감속을 따라 한다 — 밀려나는 거리·넉백이 플레이어와 같아지게")]
        [SerializeField] private bool matchOpponentBody = true;

        [Tooltip("켜면 모드·행동 선택을 콘솔에 찍는다")]
        [SerializeField] private bool debugLog = false;

        /// <summary>설정된 모드.</summary>
        public BotMode Mode => mode;
        /// <summary>지금 하는 행동 (UI 표시용). 섞어서 모드에서도 모드 이름을 준다.</summary>
        public BotMode ActiveBehaviour => mode;
        /// <summary>모드가 바뀌었을 때(순환 단축키 등). UI 표시용.</summary>
        public event Action<BotMode> OnModeChanged;
        /// <summary>가상 패드가 지금 진행 중인 행동 (스모크·로그용).</summary>
        public BotOrderKind CurrentOrder => _pad != null ? _pad.Current : BotOrderKind.Neutral;

        private MechIdentity _identity;
        private InputReader _reader;
        private MechStateMachine _stateMachine;
        private MechController _controller;
        private MeleeSystem _meleeSystem;
        private ThrusterLunge _lunge;
        private Health _health;
        private KnockdownController _knockdown;

        private readonly BotSenses _senses = new BotSenses();
        private BotBrain _brain;
        private BotPad _pad;
        private BotMode _appliedMode;
        private bool _bodyMatched;
        private BotOrderKind _lastLogged;

        private void Awake()
        {
            _identity = MechIdentity.GetOrAdd(gameObject);
            _health = GetComponent<Health>();
            // 플레이어와 같은 부품을 갖춘다. 순서: 입력 → 상태 머신 → MechController(이동기를 붙이고 둘을 묶는다) → 공격 선택
            _reader = GetOrAdd<InputReader>();
            _stateMachine = GetOrAdd<MechStateMachine>();
            _controller = GetOrAdd<MechController>();
            if (GetComponent<HeatGauge>() == null) gameObject.AddComponent<HeatGauge>();
            _lunge = GetOrAdd<ThrusterLunge>();
            GetOrAdd<WeaponSystem>();
            _meleeSystem = GetComponent<MeleeSystem>();
            if (_meleeSystem != null)
            {
                _meleeSystem.RebindComponents();
                _meleeSystem.OnHitResolved += OnOwnHitResolved;
            }

            _brain = new BotBrain(brain, 7919 + gameObject.GetInstanceID());
            _pad = new BotPad(_reader, pad);
            _appliedMode = mode;
        }

        private T GetOrAdd<T>() where T : Component
        {
            T c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }

        private void Start()
        {
            _knockdown = GetComponent<KnockdownController>();
        }

        private void OnDestroy()
        {
            if (_meleeSystem != null) _meleeSystem.OnHitResolved -= OnOwnHitResolved;
        }

        // ---------------- 모드 ----------------

        public void SetMode(BotMode newMode)
        {
            mode = newMode;
            _appliedMode = newMode;
            ResetBehaviour();
            OnModeChanged?.Invoke(mode);
            if (debugLog) Debug.Log($"[Bot] {name} 모드 → {mode}", this);
        }

        /// <summary>다음 모드로 (마지막 다음은 처음).</summary>
        public void CycleMode()
        {
            int count = Enum.GetValues(typeof(BotMode)).Length;
            SetMode((BotMode)(((int)mode + 1) % count));
        }

        /// <summary>패드를 다 떼고 기억을 비운다 (모드 전환·라운드 리셋).</summary>
        private void ResetBehaviour()
        {
            if (_pad != null) _pad.Release();
            _brain?.Reset();
            _senses.Clear();
        }

        private void Update()
        {
            // 인스펙터에서 플레이 중 모드를 바꿔도 반영
            if (mode != _appliedMode) SetMode(mode);
            if (!_bodyMatched) MatchBody();
        }

        /// <summary>상대(플레이어) 기체와 같은 질량·감속 — 같은 공격에 같은 거리로 밀리게.</summary>
        private void MatchBody()
        {
            MechIdentity opponent = _identity.Opponent;
            if (!matchOpponentBody || opponent == null) return;
            Rigidbody mine = GetComponent<Rigidbody>();
            Rigidbody theirs = opponent.GetComponent<Rigidbody>();
            if (mine == null || theirs == null) return;
            _bodyMatched = true;
            mine.mass = theirs.mass;
            // 감속은 직접 쓰지 않고 MechController의 기본값으로 (프레임 끝에 적용된다)
            if (_controller != null) _controller.SetBaselineDamping(theirs.linearDamping);
        }

        // ---------------- 전투 틱 ----------------

        /// <summary>
        /// 전투 틱마다 (MeleeSystem이 부른다 — 히트스톱 중에도): 눈 → 두뇌 → 가상 패드. 여기서 넣은 패드 값은 다음 틱에 이동기·전투가 읽는다
        /// (사람 패드도 틱 사이에 들어온 입력을 다음 틱에 읽는다).
        /// </summary>
        public void CombatTick()
        {
            MechIdentity opponent = _identity.Opponent;
            _senses.Record(opponent, VoTickDriver.TickCount);
            if (!CanAct(opponent))
            {
                _pad.Release();
                _brain.Reset();
                return;
            }
            VoMotor motor = _controller != null ? _controller.Motor : null;
            if (_knockdown == null) _knockdown = GetComponent<KnockdownController>();
            SelfView self = _senses.ReadSelf(motor, _stateMachine, _meleeSystem, _lunge, _knockdown);
            OpponentView seen = _senses.Seen(_brain.ReactionFrames(mode));
            BotOrder order = _brain.Think(mode, self, seen, _pad.Busy, out float? aim);
            // 가드 중엔 가드 자동 선회가 몸을 돌린다(사람도 R스틱을 못 쓴다) — 패드는 R스틱을 기울이지 않는다
            if (self.Guarding) aim = null;
            float rate = motor != null ? VoUnits.AngleUnitsToDegrees(motor.V.turnInsideFar) : 2f;
            _pad.Tick(order, self, aim, rate);

            if (debugLog && _pad.Current != _lastLogged)
            {
                _lastLogged = _pad.Current;
                Debug.Log($"[Bot] {name} {mode}: {_lastLogged}", this);
            }
        }

        private bool CanAct(MechIdentity opponent)
        {
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive) return false;
            if (_health != null && _health.IsDead) return false;
            if (mode == BotMode.Idle) return false;
            return true;
        }

        private void OnOwnHitResolved(HitInfo info, HitResult result)
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                if (result == HitResult.Parried) _brain.NotifyParried();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MeleeDummy.OnOwnHitResolved");
            }
        }
    }
}
