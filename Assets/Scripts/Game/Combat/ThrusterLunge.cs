using System;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Game.States;
using MyDongari.Game.Tuning;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 스러스터 런지: 원거리 무기가 없는 버나드 계열의 "멀리서 거는 기본 공격".
    /// 카메라 수평 정면으로 일직선 돌진하고, 돌진 중엔 하드 락온·방향 전환이 없다.
    /// 이동 자체는 LungeState가 매 물리 스텝 BeginDash/TickDash/EndDash를 불러 진행한다.
    /// </summary>
    public class ThrusterLunge : MonoBehaviour
    {
        /// <summary>런지 진행 단계. 예전 판정 방식은 Dash만 쓴다.</summary>
        public enum LungePhase { None, Dash, Plant, Strike }

        private TuningRef _tuning;
        private BernardTuning.LungeValues T => _tuning.Get(this).Lunge;

        /// <summary>돌진 시작.</summary>
        public event Action OnLungeStarted;
        /// <summary>
        /// 새 판정 방식에서 돌진을 멈추고 앞발을 박는 순간 (M4). 인자: 상대가 타격 거리 안이었는지(false = 돌진이 끝나 헛펀치).
        /// 외형(무릎·먼지·화면 흔들림)용.
        /// </summary>
        public event Action<bool> OnLungePlant;
        /// <summary>돌진 종료. 인자는 명중 결과, 빗나감(중단 포함)이면 null.</summary>
        public event Action<HitResult?> OnLungeEnded;
        /// <summary>쿨다운 중에 런지 입력이 들어와 버려진 순간 (V9g). HUD가 쿨다운 호를 흔들어 "아직 안 됨"을 보여준다.</summary>
        public event Action OnCooldownDenied;

        public bool IsLunging { get; private set; }
        /// <summary>
        /// 런지 쿨다운 중인지 (V9f): 런지를 시작한 순간부터 cooldown초. 부스트 대신 이것이 런지 연타를 막는다.
        /// 쿨다운 중 공격 입력은 버리고(WeaponSystem·MeleeDummy) 버퍼에도 남기지 않는다.
        /// </summary>
        public bool IsOnCooldown => _cooldownFrames > 0;
        /// <summary>쿨다운 길이(초 — 표시용, 값은 틱). HUD 호의 채움 비율 계산용.</summary>
        public float CooldownDuration => CombatTime.Seconds(T.cooldownFrames);
        /// <summary>쿨다운 중 런지 입력을 버렸다고 알린다 (WeaponSystem·MeleeDummy가 부른다).</summary>
        public void NotifyCooldownDenied() => OnCooldownDenied?.Invoke();
        /// <summary>남은 쿨다운(초). 0이면 쓸 수 있다.</summary>
        public float CooldownRemaining => CombatTime.Seconds(_cooldownFrames);
        public LungePhase Phase { get; private set; } = LungePhase.None;

        private MechStateMachine _stateMachine;
        private MechController _controller;
        private LockOnSystem _lockOn;
        private MechIdentity _identity;
        private Rigidbody _rb;
        private MeleeSystem _melee;
        private ExternalControlLock _controlLock;
        // 상태 머신이 없는 기체(연습 봇)는 LungeState 대신 이 컴포넌트가 FixedUpdate에서 돌진을 진행한다
        private bool _selfDriven;

        // 남은 쿨다운(틱). 전투 틱마다 준다
        private int _cooldownFrames;
        // 이번 틱 끝(CollectHits)에 명중 판정을 할지
        private bool _strikeCheck;
        // 명중을 상대 기록에 쌓고 결과(막힘·패리 등)를 기다리는 중
        private bool _awaitingResult;

        private Vector3 _direction;
        private Vector3 _startPosition;
        // 돌진 경과(틱)
        private int _elapsed;
        // 박기·뻗기 단계 경과(틱)
        private int _phaseTime;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _controller = GetComponent<MechController>();
            _lockOn = GetComponent<LockOnSystem>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _rb = GetComponent<Rigidbody>();
            _melee = GetComponent<MeleeSystem>();
            if (_melee != null) _melee.OnAttackResolved += OnAttackResolved;
        }

        private void OnDestroy()
        {
            if (_melee != null) _melee.OnAttackResolved -= OnAttackResolved;
        }

        /// <summary>내 런지의 결과가 상대 쪽에서 정해졌다 (다음 틱 정산). 그때 돌진 종료를 알린다.</summary>
        private void OnAttackResolved(HitInfo info, HitResult result)
        {
            if (info.Type != AttackType.Lunge || !_awaitingResult) return;
            _awaitingResult = false;
            OnLungeEnded?.Invoke(result);
        }

        private bool IsStunned => _stateMachine != null ? _stateMachine.IsStunned : _melee != null && _melee.IsStunned;

        private bool IsExternallyControlled
        {
            get
            {
                if (_controller != null) return _controller.IsExternallyControlled;
                // 잠금 컴포넌트는 앵커 클로가 잡을 때 런타임에 붙기도 해서 없으면 다시 찾는다
                if (_controlLock == null) _controlLock = GetComponent<ExternalControlLock>();
                return _controlLock != null && _controlLock.IsLocked;
            }
        }

        /// <summary>
        /// 조건이 맞으면(쿨다운이 끝났으면) 돌진을 시작하고 쿨다운을 건다 (V9f: 부스트 없음). 플레이어 기체는 LungeState로 전환,
        /// 상태 머신이 없는 기체(연습 봇)는 같은 BeginDash/TickDash를 이 컴포넌트가 직접 돌린다.
        /// </summary>
        public bool TryLunge()
        {
            if (IsLunging || _rb == null) return false;
            if (IsStunned) return false;
            if (_melee != null && _melee.IsKnockedDown) return false;
            if (IsExternallyControlled) return false;
            if (_stateMachine != null && !_stateMachine.CanStartLunge) return false;
            if (IsOnCooldown) return false;
            _cooldownFrames = T.cooldownFrames;

            if (_stateMachine != null)
            {
                _stateMachine.ChangeState(_stateMachine.LungeState);
                return true;
            }

            _selfDriven = true;
            BeginDash();
            return true;
        }

        /// <summary>전투 틱마다 (MeleeSystem이 부른다): 쿨다운을 줄이고, 상태 머신이 없는 기체는 돌진도 진행한다.</summary>
        public void CombatTick(bool frozen)
        {
            if (_cooldownFrames > 0) _cooldownFrames--;
            // 히트스톱 중엔 쿨다운만 돌고 돌진은 멈춘다
            if (!_selfDriven || frozen) return;
            // LungeState.Update와 같은 규칙: 스턴이면 돌진을 끊는다(빗나감)
            if (IsStunned)
            {
                _selfDriven = false;
                EndDash();
                return;
            }
            if (TickDash()) _selfDriven = false;
        }

        public void BeginDash()
        {
            // V3: 보는 방향 = 상체 정면 (G2). 백뷰 카메라는 몸보다 늦게 돌아서 카메라 정면을 쓰면 어긋난다
            _direction = MechFacing.AttackForward(transform);

            _startPosition = _rb.position;
            _elapsed = 0;
            _phaseTime = 0;
            _strikeCheck = false;
            IsLunging = true;
            Phase = LungePhase.Dash;

            // 돌진 중엔 하드 락온 금지: 6m 안에 들어가도 방향이 바뀌거나 펀치로 바뀌지 않게
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(true);

            _rb.MoveRotation(Quaternion.LookRotation(_direction, Vector3.up));
            SetDashVelocity(T.speed);
            OnLungeStarted?.Invoke();
        }

        /// <summary>틱마다 호출 (이동·단계 진행). 명중 판정은 틱 끝 CollectHits에서 한다. 돌진이 끝났으면 true.</summary>
        public bool TickDash()
        {
            if (!IsLunging) return true;

            // 앵커 클로 등에 잡히면 돌진을 끊는다 (외부 제어 우선)
            if (IsExternallyControlled)
            {
                Finish(null);
                return true;
            }

            BernardTuning.LungeValues t = T;
            _elapsed++;
            _strikeCheck = false;

            // 새 판정 방식의 박기·뻗기 단계 (M4)
            if (Phase == LungePhase.Plant || Phase == LungePhase.Strike) return TickStrike(t);

            SetDashVelocity(LimitedDashSpeed(t));
            // 돌진 중엔 감속 없이 일정 속도 (Slide/SideStep과 같은 우선순위)
            if (_controller != null) _controller.RequestDamping(0f, priority: 5);

            if (t.judgeMode == BernardTuning.LungeJudgeMode.StrikeAtRange)
            {
                // 정면 타격 거리에 들어오면 그 자리에서 박기, 못 들어오고 돌진이 끝나면 헛펀치
                if (IsOpponentInStrikeRange(t)) BeginPlant(t, inRange: true);
                else if (IsDashOver(t)) BeginPlant(t, inRange: false);
                return false;
            }

            // 예전 방식: 돌진 중 판정 구에 닿으면 명중 (판정은 틱 끝)
            _strikeCheck = true;

            if (IsDashOver(t))
            {
                SetDashVelocity(t.speed * t.endSpeedRatio);
                Finish(null);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 틱 끝 (모든 기체가 움직인 뒤): 판정 구가 상대에 닿았으면 상대 기록에 쌓고 돌진을 끝낸다. 결과는 다음 틱 정산에서 온다.
        /// </summary>
        public void CollectHits()
        {
            if (!IsLunging || !_strikeCheck) return;
            _strikeCheck = false;
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;
            BernardTuning.LungeValues t = T;
            Vector3 center = _rb.position + _direction * t.hitForwardOffset;
            foreach (Collider hit in Physics.OverlapSphere(center, t.hitRadius))
            {
                if (!_identity.IsOpponentCollider(hit)) continue;
                // 속도를 먼저 줄여 둔다: 패리 반사 넉백이 이 뒤에 들어와야 덮어쓰이지 않는다
                SetDashVelocity(Phase == LungePhase.Dash ? t.speed * t.endSpeedRatio : 0f);
                HitInfo info = _melee != null
                    ? _melee.NewHit(AttackType.Lunge, HitKind.Strong, t.damage, opponent.gameObject)
                    : new HitInfo { Type = AttackType.Lunge, Kind = HitKind.Strong, Damage = t.damage, Attacker = gameObject };
                info.Direction = _direction;
                // 강타: 가드 불능, 날아감
                info.Impact = StyleOverrides.Current.EffectiveCombat.impactStrong;
                MeleeSystem.QueueHit(opponent.gameObject, info);
                FinishHit();
                return;
            }
        }

        /// <summary>
        /// 정지 거리·넘어가기 방지 (전투 사양서 1.3·3.1의 대시 근접 값): 상대가 돌진 정면에 있을 때,
        /// 거리가 정지 거리(15u) 이하면 속력 0, 이번 틱 이동량은 상대 거리를 넘지 않는다. 옆·뒤의 상대는 지나친다.
        /// </summary>
        private float LimitedDashSpeed(BernardTuning.LungeValues t)
        {
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return t.speed;
            Vector3 toTarget = opponent.transform.position - _rb.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > 0.0001f && Vector3.Angle(_direction, toTarget) > t.strikeHalfAngle) return t.speed;
            float u = _melee != null ? _melee.U : StyleOverrides.Current.Effective.fallbackBodyHeight / StyleOverrides.Current.Effective.bodyHeightUnits;
            if (distance <= StyleOverrides.Current.EffectiveCombat.dashMeleeStopDistance * u) return 0f;
            return Mathf.Min(t.speed, distance * CombatTime.TickRate);
        }

        /// <summary>돌진 거리를 다 갔거나(벽 등으로 막혀) 최대 시간이 지났는지.</summary>
        private bool IsDashOver(BernardTuning.LungeValues t)
        {
            Vector3 travelled = _rb.position - _startPosition;
            travelled.y = 0f;
            int timeout = t.speed > 0f ? CombatTime.Frames(t.distance / t.speed * t.timeoutMultiplier) : 0;
            return travelled.magnitude >= t.distance || _elapsed >= timeout;
        }

        /// <summary>상대 중심이 돌진 방향 정면(±strikeHalfAngle) strikeDistance 안인지 (수평만).</summary>
        private bool IsOpponentInStrikeRange(BernardTuning.LungeValues t)
        {
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Vector3 toTarget = opponent.transform.position - _rb.position;
            toTarget.y = 0f;
            if (toTarget.magnitude > t.strikeDistance) return false;
            return toTarget.sqrMagnitude < 0.0001f || Vector3.Angle(_direction, toTarget) <= t.strikeHalfAngle;
        }

        /// <summary>새 방식: 돌진을 멈추고 앞발 박기 시작 (그 자리에서). 박기 동안은 판정 없음.</summary>
        private void BeginPlant(BernardTuning.LungeValues t, bool inRange)
        {
            SetDashVelocity(t.speed * t.endSpeedRatio);
            Phase = LungePhase.Plant;
            _phaseTime = 0;
            OnLungePlant?.Invoke(inRange);
        }

        /// <summary>박기 → 뻗기. 판정은 뻗는 구간에만 (명중 판정 구는 예전과 같은 수치). 뻗기가 끝날 때까지 못 맞히면 빗나감.</summary>
        private bool TickStrike(BernardTuning.LungeValues t)
        {
            _phaseTime++;
            // 돌진 끝 속도에서 그 자리에 멈춰 선다 (박기·뻗기 중엔 감속 요청)
            if (_controller != null) _controller.RequestDamping(t.strikeStopDamping, priority: 5);

            if (Phase == LungePhase.Plant)
            {
                if (_phaseTime < t.plantFrames) return false;
                _phaseTime -= t.plantFrames;
                Phase = LungePhase.Strike;
            }

            if (_phaseTime > t.strikeExtendFrames)
            {
                // 헛펀치: 그 뒤는 예전과 같은 빗나감 처리(하드 락온 금지 딜레이)
                Finish(null);
                return true;
            }
            // 뻗는 구간: 틱 끝에 판정
            _strikeCheck = true;
            return false;
        }

        /// <summary>LungeState를 벗어날 때 호출. 도중에 끊긴(스턴 등) 돌진은 빗나감으로 정리.</summary>
        public void EndDash()
        {
            if (IsLunging) Finish(null);
        }

        /// <summary>라운드 리셋: 이벤트 없이 조용히 끝낸다 (로그에 빗나감으로 남지 않게).</summary>
        public void ResetForRound()
        {
            IsLunging = false;
            Phase = LungePhase.None;
            _selfDriven = false;
            _cooldownFrames = 0;
            _strikeCheck = false;
            _awaitingResult = false;
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(false);
        }

        /// <summary>명중을 상대 기록에 쌓았다: 돌진은 여기서 끝나고, 종료 알림(OnLungeEnded)은 결과가 정해질 때 한다.</summary>
        private void FinishHit()
        {
            IsLunging = false;
            Phase = LungePhase.None;
            _selfDriven = false;
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(false);
            if (_melee != null) _awaitingResult = true;
            else OnLungeEnded?.Invoke(HitResult.Hit);
        }

        private void Finish(HitResult? result)
        {
            IsLunging = false;
            Phase = LungePhase.None;
            _strikeCheck = false;
            if (_lockOn != null)
            {
                _lockOn.SetHardLockSuppressed(false);
                if (result == null) _lockOn.BlockHardLockFor(T.missLockDelay);
            }
            OnLungeEnded?.Invoke(result);
        }

        private void SetDashVelocity(float speed)
        {
            MotionBridge.SetHorizontalVelocity(_rb, new Vector3(_direction.x * speed, 0f, _direction.z * speed));
        }
    }
}
