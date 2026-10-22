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
        [Header("돌진")]
        [Tooltip("돌진 거리(m)")]
        [SerializeField] private float lungeDistance = 8f;
        [Tooltip("돌진 속도(m/s)")]
        [SerializeField] private float lungeSpeed = 30f;
        [Tooltip("돌진이 끝날 때 남기는 속도 비율. 0이면 딱 멈춤")]
        [Range(0f, 1f)]
        [SerializeField] private float endSpeedRatio = 0.2f;
        [Tooltip("벽 등에 막혀 거리를 못 채울 때를 대비한 최대 돌진 시간 = (거리/속도) × 이 배수")]
        [SerializeField] private float timeoutMultiplier = 1.5f;
        [Tooltip("발동 시 소모하는 부스트. 탈진이면 발동 안 함")]
        [SerializeField] private float boostCost = 15f;

        [Header("명중")]
        [Tooltip("명중 피해 (펀치보다 약간 낮게)")]
        [SerializeField] private float damage = 15f;
        [Tooltip("명중 시 상대 스턴(초). 후속 펀치 1회가 들어갈 틈")]
        [SerializeField] private float hitStun = 0.5f;
        [Tooltip("명중 시 상대 넉백. 0이면 밀지 않음(후속 펀치 거리 유지)")]
        [SerializeField] private float hitKnockback = 0f;
        [Tooltip("명중 판정 구의 반경(m)")]
        [SerializeField] private float hitRadius = 1.2f;
        [Tooltip("명중 판정 구를 기체 앞쪽으로 내미는 거리(m)")]
        [SerializeField] private float hitForwardOffset = 1f;

        [Header("빗나감")]
        [Tooltip("빗나간 뒤 이 시간(초) 동안 하드 락온이 다시 걸리지 않는다 (사이드스텝으로 피한 상대의 반격 틈)")]
        [SerializeField] private float missLockDelay = 0.6f;

        /// <summary>돌진 시작.</summary>
        public event Action OnLungeStarted;
        /// <summary>돌진 종료. 인자는 명중 결과, 빗나감(중단 포함)이면 null.</summary>
        public event Action<HitResult?> OnLungeEnded;

        public bool IsLunging { get; private set; }

        private MechStateMachine _stateMachine;
        private MechController _controller;
        private BoostGauge _boostGauge;
        private LockOnSystem _lockOn;
        private MechIdentity _identity;
        private Rigidbody _rb;

        private Vector3 _direction;
        private Vector3 _startPosition;
        private float _elapsed;

        private void Awake()
        {
            _stateMachine = GetComponent<MechStateMachine>();
            _controller = GetComponent<MechController>();
            _boostGauge = GetComponent<BoostGauge>();
            _lockOn = GetComponent<LockOnSystem>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _rb = GetComponent<Rigidbody>();
        }

        /// <summary>조건이 맞으면 부스트를 쓰고 LungeState로 전환한다.</summary>
        public bool TryLunge()
        {
            if (IsLunging || _stateMachine == null || _rb == null) return false;
            if (_stateMachine.IsStunned) return false;
            if (_controller != null && _controller.IsExternallyControlled) return false;
            if (!_stateMachine.CanStartLunge) return false;
            // TryConsume은 탈진이면 false
            if (_boostGauge != null && !_boostGauge.TryConsume(boostCost)) return false;

            _stateMachine.ChangeState(_stateMachine.LungeState);
            return true;
        }

        public void BeginDash()
        {
            Transform cam = _identity.CameraTransform;
            Vector3 forward = cam != null ? cam.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
            _direction = forward.normalized;

            _startPosition = _rb.position;
            _elapsed = 0f;
            IsLunging = true;

            // 돌진 중엔 하드 락온 금지: 6m 안에 들어가도 방향이 바뀌거나 펀치로 바뀌지 않게
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(true);

            _rb.MoveRotation(Quaternion.LookRotation(_direction, Vector3.up));
            SetDashVelocity(lungeSpeed);
            OnLungeStarted?.Invoke();
        }

        /// <summary>물리 스텝마다 호출. 돌진이 끝났으면 true.</summary>
        public bool TickDash()
        {
            if (!IsLunging) return true;

            // 앵커 클로 등에 잡히면 돌진을 끊는다 (외부 제어 우선)
            if (_controller != null && _controller.IsExternallyControlled)
            {
                Finish(null);
                return true;
            }

            _elapsed += Time.fixedDeltaTime;
            SetDashVelocity(lungeSpeed);
            // 돌진 중엔 감속 없이 일정 속도 (Slide/SideStep과 같은 우선순위)
            if (_controller != null) _controller.RequestDamping(0f, priority: 5);

            MechIdentity opponent = _identity.Opponent;
            if (opponent != null && opponent.gameObject.activeInHierarchy)
            {
                Vector3 center = _rb.position + _direction * hitForwardOffset;
                foreach (Collider hit in Physics.OverlapSphere(center, hitRadius))
                {
                    if (!_identity.IsOpponentCollider(hit)) continue;

                    // 속도를 먼저 줄여 둔다: 패리 반사 넉백이 이 뒤에 들어와야 덮어쓰이지 않는다
                    SetDashVelocity(lungeSpeed * endSpeedRatio);
                    HitInfo info = new HitInfo
                    {
                        Type = AttackType.Lunge,
                        Damage = damage,
                        Attacker = gameObject,
                        KnockbackDirection = _direction,
                        KnockbackForce = hitKnockback,
                        StunDuration = hitStun
                    };
                    Finish(MeleeSystem.DeliverHit(opponent.gameObject, info));
                    return true;
                }
            }

            Vector3 travelled = _rb.position - _startPosition;
            travelled.y = 0f;
            float timeout = lungeSpeed > 0f ? lungeDistance / lungeSpeed * timeoutMultiplier : 0f;
            if (travelled.magnitude >= lungeDistance || _elapsed >= timeout)
            {
                SetDashVelocity(lungeSpeed * endSpeedRatio);
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
            if (_lockOn != null) _lockOn.SetHardLockSuppressed(false);
        }

        private void Finish(HitResult? result)
        {
            IsLunging = false;
            if (_lockOn != null)
            {
                _lockOn.SetHardLockSuppressed(false);
                if (result == null) _lockOn.BlockHardLockFor(missLockDelay);
            }
            OnLungeEnded?.Invoke(result);
        }

        private void SetDashVelocity(float speed)
        {
            _rb.linearVelocity = new Vector3(_direction.x * speed, _rb.linearVelocity.y, _direction.z * speed);
        }
    }
}
