using System;
using UnityEngine;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.Combat
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

        public bool IsLunging { get; private set; }
        public LungePhase Phase { get; private set; } = LungePhase.None;

        private MechStateMachine _stateMachine;
        private MechController _controller;
        private BoostGauge _boostGauge;
        private LockOnSystem _lockOn;
        private MechIdentity _identity;
        private Rigidbody _rb;
        private MeleeSystem _melee;
        private ExternalControlLock _controlLock;
        // 상태 머신이 없는 기체(연습 봇)는 LungeState 대신 이 컴포넌트가 FixedUpdate에서 돌진을 진행한다
        private bool _selfDriven;

        private Vector3 _direction;
        private Vector3 _startPosition;
        private float _elapsed;
        // 박기·뻗기 단계 경과(초, 물리 스텝)
        private float _phaseTime;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _controller = GetComponent<MechController>();
            _boostGauge = GetComponent<BoostGauge>();
            _lockOn = GetComponent<LockOnSystem>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _rb = GetComponent<Rigidbody>();
            _melee = GetComponent<MeleeSystem>();
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
        /// 조건이 맞으면 부스트를 쓰고 돌진을 시작한다. 플레이어 기체는 LungeState로 전환,
        /// 상태 머신이 없는 기체(연습 봇)는 같은 BeginDash/TickDash를 이 컴포넌트가 직접 돌린다.
        /// </summary>
        public bool TryLunge()
        {
            if (IsLunging || _rb == null) return false;
            if (IsStunned) return false;
            if (_melee != null && _melee.IsKnockedDown) return false;
            if (IsExternallyControlled) return false;
            if (_stateMachine != null && !_stateMachine.CanStartLunge) return false;
            // TryConsume은 탈진이면 false
            if (_boostGauge != null && !_boostGauge.TryConsume(T.boostCost)) return false;

            if (_stateMachine != null)
            {
                _stateMachine.ChangeState(_stateMachine.LungeState);
                return true;
            }

            _selfDriven = true;
            BeginDash();
            return true;
        }

        private void FixedUpdate()
        {
            if (!_selfDriven) return;
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
            Transform cam = _identity.CameraTransform;
            // 카메라 정면 우선(상체도 카메라를 향한다). 카메라가 없거나 수직이면 상체 방향 (G2)
            Vector3 forward = cam != null ? cam.forward : MechFacing.AttackForward(transform);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = MechFacing.AttackForward(transform);
            _direction = forward.normalized;

            _startPosition = _rb.position;
            _elapsed = 0f;
            _phaseTime = 0f;
            IsLunging = true;
            Phase = LungePhase.Dash;

            // 돌진 중엔 하드 락온 금지: 6m 안에 들어가도 방향이 바뀌거나 펀치로 바뀌지 않게
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(true);

            _rb.MoveRotation(Quaternion.LookRotation(_direction, Vector3.up));
            SetDashVelocity(T.speed);
            OnLungeStarted?.Invoke();
        }

        /// <summary>물리 스텝마다 호출. 돌진이 끝났으면 true.</summary>
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
            _elapsed += Time.fixedDeltaTime;

            // 새 판정 방식의 박기·뻗기 단계 (M4)
            if (Phase == LungePhase.Plant || Phase == LungePhase.Strike) return TickStrike(t);

            SetDashVelocity(t.speed);
            // 돌진 중엔 감속 없이 일정 속도 (Slide/SideStep과 같은 우선순위)
            if (_controller != null) _controller.RequestDamping(0f, priority: 5);

            if (t.judgeMode == BernardTuning.LungeJudgeMode.StrikeAtRange)
            {
                // 정면 타격 거리에 들어오면 그 자리에서 박기, 못 들어오고 돌진이 끝나면 헛펀치
                if (IsOpponentInStrikeRange(t)) BeginPlant(t, inRange: true);
                else if (IsDashOver(t)) BeginPlant(t, inRange: false);
                return false;
            }

            MechIdentity opponent = _identity.Opponent;
            if (opponent != null && opponent.gameObject.activeInHierarchy)
            {
                Vector3 center = _rb.position + _direction * t.hitForwardOffset;
                foreach (Collider hit in Physics.OverlapSphere(center, t.hitRadius))
                {
                    if (!_identity.IsOpponentCollider(hit)) continue;

                    // 속도를 먼저 줄여 둔다: 패리 반사 넉백이 이 뒤에 들어와야 덮어쓰이지 않는다
                    SetDashVelocity(t.speed * t.endSpeedRatio);
                    Finish(DeliverLungeHit(opponent, t));
                    return true;
                }
            }

            if (IsDashOver(t))
            {
                SetDashVelocity(t.speed * t.endSpeedRatio);
                Finish(null);
                return true;
            }
            return false;
        }

        private HitResult DeliverLungeHit(MechIdentity opponent, BernardTuning.LungeValues t)
        {
            HitInfo info = new HitInfo
            {
                Type = AttackType.Lunge,
                Damage = t.damage,
                Attacker = gameObject,
                KnockbackDirection = _direction,
                KnockbackForce = t.hitKnockback,
                StunDuration = t.hitStun
            };
            return MeleeSystem.DeliverHit(opponent.gameObject, info);
        }

        /// <summary>돌진 거리를 다 갔거나(벽 등으로 막혀) 최대 시간이 지났는지.</summary>
        private bool IsDashOver(BernardTuning.LungeValues t)
        {
            Vector3 travelled = _rb.position - _startPosition;
            travelled.y = 0f;
            float timeout = t.speed > 0f ? t.distance / t.speed * t.timeoutMultiplier : 0f;
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
            _phaseTime = 0f;
            OnLungePlant?.Invoke(inRange);
        }

        /// <summary>박기 → 뻗기. 판정은 뻗는 구간에만 (명중 판정 구는 예전과 같은 수치). 뻗기가 끝날 때까지 못 맞히면 빗나감.</summary>
        private bool TickStrike(BernardTuning.LungeValues t)
        {
            _phaseTime += Time.fixedDeltaTime;
            // 돌진 끝 속도에서 그 자리에 멈춰 선다 (박기·뻗기 중엔 Slide/SideStep과 같은 우선순위로 감속)
            if (_controller != null) _controller.RequestDamping(t.strikeStopDamping, priority: 5);

            if (Phase == LungePhase.Plant)
            {
                if (_phaseTime < t.plantTime) return false;
                _phaseTime -= t.plantTime;
                Phase = LungePhase.Strike;
            }

            MechIdentity opponent = _identity.Opponent;
            if (opponent != null && opponent.gameObject.activeInHierarchy)
            {
                Vector3 center = _rb.position + _direction * t.hitForwardOffset;
                foreach (Collider hit in Physics.OverlapSphere(center, t.hitRadius))
                {
                    if (!_identity.IsOpponentCollider(hit)) continue;
                    // 패리 반사 넉백이 감속에 지워지지 않게 속도를 먼저 줄여 둔다
                    SetDashVelocity(0f);
                    Finish(DeliverLungeHit(opponent, t));
                    return true;
                }
            }

            if (_phaseTime >= t.strikeExtendTime)
            {
                // 헛펀치: 그 뒤는 예전과 같은 빗나감 처리(하드 락온 금지 딜레이)
                Finish(null);
                return true;
            }
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
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(false);
        }

        private void Finish(HitResult? result)
        {
            IsLunging = false;
            Phase = LungePhase.None;
            if (_lockOn != null)
            {
                _lockOn.SetHardLockSuppressed(false);
                if (result == null) _lockOn.BlockHardLockFor(T.missLockDelay);
            }
            OnLungeEnded?.Invoke(result);
        }

        private void SetDashVelocity(float speed)
        {
            _rb.linearVelocity = new Vector3(_direction.x * speed, _rb.linearVelocity.y, _direction.z * speed);
        }
    }
}
