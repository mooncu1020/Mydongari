using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Combat;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>대시가 끝난 이유 (로그용).</summary>
    public enum DashExitReason
    {
        Other,
        ButtonReleased,
        Exhausted,
        Stun,
        Tackle,
        Bump
    }

    /// <summary>
    /// ○ 대시 (V3 "이동"). 버튼을 누르고 있는 동안 dashSpeed(30m/s)로 돌진하고 부스트를 초당 소모한다(BoostGauge).
    /// 하드락 밖: 보는 방향으로 돌진, L스틱으로 방향을 튼다(초당 dashTurnRate° 상한 — 스틱 쪽으로 계속 돌아간다).
    /// 하드락 안: 상대에게 돌진. 1.5초 이상 유지하면 태클 차징(3초 최대). 대시 중 공격 = 대시 어택/차징 태클.
    /// 몸은 대시 방향을 본다(카메라가 따라온다). 대시 중엔 가드 불가(MeleeSystem), 부스트 회복 멈춤(BoostGauge).
    /// </summary>
    public class DashState : MechState
    {
        private float _tackleChargeTimer = 0f;
        private bool _tackleRequested = false;
        // 어깨 태클 발생 시간 경과(초). 음수면 발생 중인 태클 없음. 종류·차징은 버튼을 누른 순간 값으로 고정
        private float _pendingTackleElapsed = -1f;
        private AttackType _pendingTackleType;
        private float _pendingTacklePower;
        private BernardTuning.TackleValues Tackle => StateMachine.MechController.Tuning.Tackle;
        private BernardTuning.MovementValues Move => StateMachine.MechController.Tuning.Movement;

        // 지금 돌진 방향(수평, 크기 1)
        private Vector3 _heading;
        private DashExitReason _exitReason;
        private bool _exitReported;

        /// <summary>대시가 실제로 시작됐을 때. 로그용.</summary>
        public event Action OnDashStarted;
        /// <summary>대시가 끝났을 때 (이유). 로그용.</summary>
        public event Action<DashExitReason> OnDashEnded;
        // 차징 상태로 이 거리(m) 안에 상대가 닿으면 자동 태클 (기존 동작 유지)
        private float ContactRadius => Tackle.contactRadius;
        public bool IsTackleCharged => _tackleChargeTimer >= Tackle.chargeStartTime;
        /// <summary>대시 어택·태클이 발생 중인지 (몸 충돌 대신 공격으로 처리해야 하는지).</summary>
        public bool IsAttacking => _tackleRequested || _pendingTackleElapsed >= 0f;
        /// <summary>차징 시작(1.5초)에서 0, 최대 차징(3초)에서 1. 태클 피해·넉백 배율에 쓴다.</summary>
        public float TackleChargePower
        {
            get
            {
                float span = Tackle.fullChargeTime - Tackle.chargeStartTime;
                if (span <= 0f) return IsTackleCharged ? 1f : 0f;
                return Mathf.Clamp01((_tackleChargeTimer - Tackle.chargeStartTime) / span);
            }
        }

        public DashState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            _tackleChargeTimer = 0f;
            _tackleRequested = false;
            _pendingTackleElapsed = -1f;
            _exitReason = DashExitReason.Other;
            _exitReported = false;

            // 시작 방향: 하드락이면 상대 쪽, 아니면 보는 방향(몸 정면)
            MechController controller = StateMachine.MechController;
            _heading = controller.TryGetHardLockForward(out Vector3 toOpponent)
                ? toOpponent
                : controller.BodyRelativeDirection(Vector2.up);
            OnDashStarted?.Invoke();
        }

        private void Leave(DashExitReason reason, MechState next)
        {
            _exitReason = reason;
            StateMachine.ChangeState(next);
        }

        private void ReportExit(DashExitReason reason)
        {
            if (_exitReported) return;
            _exitReported = true;
            OnDashEnded?.Invoke(reason);
        }

        /// <summary>대시가 끝나면 스틱을 밀고 있으면 달리기, 아니면 정지로.</summary>
        private MechState AfterDash => StateMachine.InputReader.IsMoveInputActive
            ? (MechState)StateMachine.RunState
            : StateMachine.IdleState;

        public override void Update()
        {
            // 스턴(가드 깨짐 포함)이면 대시 종료 — 발생 중이던 태클은 Exit에서 취소된다
            if (StateMachine.IsStunned)
            {
                Leave(DashExitReason.Stun, StateMachine.IdleState);
                return;
            }

            // 태클 발생 시간 동안은 어깨를 내민 채 그대로 돌진한다 (버튼을 떼도·탈진해도 끝까지)
            if (IsAttacking) return;

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                Leave(DashExitReason.Exhausted, AfterDash);
                return;
            }

            if (!StateMachine.InputReader.DashHeld)
            {
                Leave(DashExitReason.ButtonReleased, AfterDash);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;
        }

        public override void FixedUpdate()
        {
            MechController controller = StateMachine.MechController;
            UpdateHeading(controller);
            controller.SetMoveVelocity(_heading, Move.dashSpeed);
            if (!controller.IsExternallyControlled) controller.Rb.MoveRotation(Quaternion.LookRotation(_heading, Vector3.up));

            MeleeSystem melee = controller.MeleeSystem;

            // 발생 시간이 지난 물리 스텝에 판정 (판정 범위·상대 위치는 이 순간 기준)
            if (_pendingTackleElapsed >= 0f)
            {
                _tackleRequested = false;
                _pendingTackleElapsed += Time.fixedDeltaTime;
                if (melee != null && _pendingTackleElapsed >= melee.TackleStartup(_pendingTackleType))
                {
                    _pendingTackleElapsed = -1f;
                    FinishTackle(melee, _pendingTacklePower, _pendingTackleType);
                }
                return;
            }

            bool requested = _tackleRequested;
            _tackleRequested = false;
            // 버튼 태클과 차징 자동 접촉 태클을 이 한 곳에서만 처리해 같은 프레임 중복 발동을 막는다
            if (requested || (IsTackleCharged && IsOpponentInContact()))
            {
                // 쿨다운 중이면 태클이 안 나가니 대시를 끊지 않는다
                if (melee == null || !melee.CanTackle) return;

                AttackType type = IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
                if (!melee.BeginTackle(type)) return;

                if (melee.TackleStartup(type) <= 0f)
                {
                    FinishTackle(melee, TackleChargePower, type);
                    return;
                }
                _pendingTackleElapsed = 0f;
                _pendingTackleType = type;
                _pendingTacklePower = TackleChargePower;
            }
        }

        /// <summary>
        /// 돌진 방향 갱신. 하드락 안: 상대 쪽으로 바로. 밖: L스틱(몸 기준) 쪽으로 초당 dashTurnRate°까지만 돌린다.
        /// 스틱 방향을 몸 기준으로 재므로, 옆으로 밀고 있으면 그쪽으로 계속 돌아간다(운전대처럼). 발생 중인 태클은 방향 고정.
        /// </summary>
        private void UpdateHeading(MechController controller)
        {
            if (_pendingTackleElapsed >= 0f) return;
            if (controller.TryGetHardLockForward(out Vector3 toOpponent))
            {
                _heading = toOpponent;
                return;
            }
            InputReader input = StateMachine.InputReader;
            if (!input.IsMoveInputActive) return;
            Vector3 desired = controller.BodyRelativeDirection(input.MoveInput);
            if (desired.sqrMagnitude < 0.0001f) return;
            float maxRadians = Move.dashTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime;
            _heading = Vector3.RotateTowards(_heading, desired, maxRadians, 0f);
            _heading.y = 0f;
            _heading.Normalize();
        }

        private void FinishTackle(MeleeSystem melee, float power, AttackType type)
        {
            // 속도를 먼저 0으로: 패리 반사 넉백이 이 뒤에 들어와야 지워지지 않는다
            StateMachine.MechController.Rb.linearVelocity = Vector3.zero;
            HitResult? result = melee.ResolveTackle(power, type);
            if (result != HitResult.Parried)
            {
                melee.ApplyStun(melee.TackleRecoveryStun);
            }
            Leave(DashExitReason.Tackle, StateMachine.IdleState);
        }

        /// <summary>
        /// 공격 버튼으로 어깨 태클 요청. 받아들이면 true(다음 물리 스텝에 시작), 쿨다운·발생 중이면 false
        /// (입력 버퍼가 다시 시도한다).
        /// </summary>
        public bool RequestTackle()
        {
            MeleeSystem melee = StateMachine.MechController.MeleeSystem;
            if (_tackleRequested) return true;
            if (_pendingTackleElapsed >= 0f || melee == null || !melee.CanTackle) return false;
            _tackleRequested = true;
            return true;
        }

        /// <summary>몸끼리 부딪혀 대시가 끝남 (MechController.OnCollisionEnter). 속도는 부르는 쪽이 넣는다.</summary>
        public void EndByBump()
        {
            Leave(DashExitReason.Bump, StateMachine.IdleState);
        }

        private bool IsOpponentInContact()
        {
            // 정면 부채꼴 밖(옆으로 스침)이면 자동 태클을 걸지 않는다. 걸면 헛치고 경직만 남는다
            MeleeSystem melee = StateMachine.MechController.MeleeSystem;
            if (melee == null || !melee.IsOpponentInTackleArc()) return false;

            Collider[] hits = Physics.OverlapSphere(
                StateMachine.MechController.transform.position, ContactRadius);
            foreach (Collider hit in hits)
            {
                if (StateMachine.MechController.Identity.IsOpponentCollider(hit)) return true;
            }
            return false;
        }

        public override void Exit()
        {
            // 라운드 리셋 등 바깥에서 끊긴 경우는 Other
            ReportExit(_exitReason);
            _tackleRequested = false;

            // 발생 중에 대시가 끝났다면(스턴·라운드 리셋 등) 그 태클은 나가지 않는다
            if (_pendingTackleElapsed >= 0f)
            {
                _pendingTackleElapsed = -1f;
                MeleeSystem melee = StateMachine.MechController.MeleeSystem;
                if (melee != null) melee.CancelTackle(_pendingTackleType);
            }
        }
    }
}
