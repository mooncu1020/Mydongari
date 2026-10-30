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
        Canceled,
        Exhausted,
        Stun,
        Tackle,
        Bump,
        Wall,
        Landed
    }

    /// <summary>
    /// ○ 대시 (V3.1 "대시 상세"). ○를 누른 프레임에 L스틱 방향(8방향 스냅, 중립이면 앞)으로 방향을 고정하고 바로 dashSpeed(30m/s)로
    /// 일직선 출발한다 — 누르고 있을 필요 없음(토글). 방향 기준은 하드락 밖이면 몸 정면, 안이면 그 순간 상대 쪽. 대시 중엔 방향을 틀 수 없다.
    /// 발동 후 dashCancelLockTime(1초) 동안 취소 불가, 그 뒤 ○를 다시 누르면 취소. 부스트 0%·벽 충돌이면 끝, 상대 몸 충돌은 반동(MechController).
    /// 끝나면 DashStopState(약 0.2초 미끄러지며 섬). 몸 정면(조준·카메라 기준)은 돌리지 않는다 — 외형 모델만 대시 방향으로 튼다(MechVisualRig).
    /// 1.5초 이상 계속이면 태클 차징(3초 최대).
    /// 대시 중 공격 (V4): 1.5초 미만 = 유도 대시 어택(상대 25m 안이면 꺾는 속도 상한으로 곡선 추적, 지나치면 직진),
    /// 1.5초 이상 = 차징 태클(직선). 발생 시간 뒤 activeTime 동안 돌진하며 닿으면 판정, 끝까지 못 닿으면 빗나감 경직.
    /// 대시 중엔 가드 불가(MeleeSystem), 부스트 회복 멈춤·초당 소모(BoostGauge).
    /// 공중 대시 (V5): 공중에서 ○ — 같은 규칙(방향 고정, 1초 후 취소)이고, 출발할 때 세로 속도를 0으로 한 뒤 중력을
    /// airDashGravityScale만큼만 받는다. 공중 대시 중 공격은 무시(6단계 전), 끝나면 떨어지고(AirState) 착지하면 착지 경직.
    /// </summary>
    public class DashState : MechState
    {
        private float _tackleChargeTimer = 0f;
        private bool _tackleRequested = false;
        // 대시 어택·태클 시작 후 경과(초). 음수면 공격 중 아님. 발생 시간(Startup) → 판정 구간(activeTime) 순서.
        // 종류·차징은 공격을 시작한 순간 값으로 고정
        private float _attackElapsed = -1f;
        private AttackType _attackType;
        private float _attackPower;
        // 대시 어택이 아직 상대를 따라가는지. 상대를 지나치면 그 공격 동안은 끝
        private bool _homing;
        private BernardTuning.TackleValues Tackle => StateMachine.MechController.Tuning.Tackle;
        private BernardTuning.DashAttackValues DashAttack => StateMachine.MechController.Tuning.DashAttack;
        private BernardTuning.MovementValues Move => StateMachine.MechController.Tuning.Movement;

        // 지금 돌진 방향(수평, 크기 1). 누른 순간 고정, 대시 어택 유도만 바꾼다
        private Vector3 _heading;
        // 발동 후 경과(초). dashCancelLockTime이 지나야 ○로 취소된다
        private float _elapsed;

        /// <summary>지금 돌진 방향 (외형이 몸을 트는 방향, 태클 판정 부채꼴 기준).</summary>
        public Vector3 Heading => _heading;
        /// <summary>발동 후 경과 시간(초).</summary>
        public float Elapsed => _elapsed;

        // 공중 대시인지 (TryStartDash가 Enter 직전에 정한다)
        private bool _airborne;
        private bool _pendingAirborne;
        /// <summary>공중 대시 중인지.</summary>
        public bool IsAirborne => _airborne;

        /// <summary>다음 Enter가 공중 대시인지 정한다 (MechStateMachine.TryStartDash).</summary>
        public void PrepareAirborne(bool airborne)
        {
            _pendingAirborne = airborne;
        }
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
        public bool IsAttacking => _tackleRequested || _attackElapsed >= 0f;
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
            _attackElapsed = -1f;
            _exitReason = DashExitReason.Other;
            _exitReported = false;
            _elapsed = 0f;
            _airborne = _pendingAirborne;
            _pendingAirborne = false;

            MechController controller = StateMachine.MechController;
            if (_airborne)
            {
                // 공중 대시는 수평으로 미끄러지듯: 오르내리던 속도를 지우고 약한 중력만
                Vector3 v = controller.Rb.linearVelocity;
                controller.Rb.linearVelocity = new Vector3(v.x, 0f, v.z);
            }
            _heading = SnappedDirection(controller, StateMachine.InputReader);
            // 입력 지연 없음: 누른 프레임에 바로 최고 속도 (물리 스텝을 기다리지 않는다)
            controller.SetMoveVelocity(_heading, Move.dashSpeed);
            OnDashStarted?.Invoke();
        }

        /// <summary>
        /// 누른 순간 방향: 기준(하드락 안 = 상대 쪽, 밖 = 몸 정면)에서 L스틱 각도를 45° 단위로 스냅해 돌린 방향.
        /// 스틱 중립이면 기준 그대로(앞).
        /// </summary>
        private static Vector3 SnappedDirection(MechController controller, InputReader input)
        {
            Vector3 basis = controller.TryGetHardLockForward(out Vector3 toOpponent)
                ? toOpponent
                : controller.BodyRelativeDirection(Vector2.up);
            if (input == null || !input.IsMoveInputActive) return basis;
            Vector2 stick = input.MoveInput;
            // 스틱 위 = 0°, 오른쪽 = 90° (Y축 회전과 같은 방향)
            float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 45f) * 45f;
            return (Quaternion.Euler(0f, angle, 0f) * basis).normalized;
        }

        private void Leave(DashExitReason reason, MechState next)
        {
            _exitReason = reason;
            // 공중 대시가 끝나면(취소·탈진·벽·충돌·넉백) 땅 상태 대신 떨어지고, 착지하면 착지 경직
            if (_airborne && (next is IdleState || next is RunState || next is DashStopState))
            {
                StateMachine.AirState.PrepareFallWithLandingLag();
                next = StateMachine.AirState;
            }
            StateMachine.ChangeState(next);
        }

        private void ReportExit(DashExitReason reason)
        {
            if (_exitReported) return;
            _exitReported = true;
            OnDashEnded?.Invoke(reason);
        }


        public override void Update()
        {
            // 스턴(가드 깨짐 포함)이면 대시 종료 — 발생 중이던 태클은 Exit에서 취소된다
            if (StateMachine.IsStunned)
            {
                Leave(DashExitReason.Stun, StateMachine.IdleState);
                return;
            }

            _elapsed += Time.deltaTime;

            // 공중 대시 중 땅에 닿으면 착지 (뜬 직후 바닥 검사에 걸리지 않게 잠깐 뒤부터)
            if (_airborne && StateMachine.MechController.IsGrounded && _elapsed >= Move.jumpGroundIgnoreTime &&
                StateMachine.MechController.Rb.linearVelocity.y <= 0.5f)
            {
                _exitReason = DashExitReason.Landed;
                StateMachine.Land(true);
                return;
            }

            // 공격 중(발생 시간 + 판정 구간)은 어깨를 내민 채 그대로 돌진한다 (취소·탈진으로 끊지 않음)
            if (IsAttacking) return;

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                Leave(DashExitReason.Exhausted, StateMachine.DashStopState);
                return;
            }

            // 1초 취소 불가가 대가 — 그 전에 누른 ○는 무시
            if (_elapsed >= Move.dashCancelLockTime && StateMachine.InputReader.DashPressedThisFrame)
            {
                Leave(DashExitReason.Canceled, StateMachine.DashStopState);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;
        }

        public override void FixedUpdate()
        {
            MechController controller = StateMachine.MechController;
            if (_attackElapsed >= 0f)
            {
                TickAttack(controller);
                return;
            }

            if (_airborne)
            {
                controller.ApplyAirGravity(AirState.JumpGravity(Move) * Move.airDashGravityScale);
                Advance(controller);
                // 공중 대시 중 공격·자동 태클은 아직 없다 (6단계 공중 공격)
                _tackleRequested = false;
                return;
            }

            Advance(controller);

            bool requested = _tackleRequested;
            _tackleRequested = false;
            // 버튼 공격과 차징 자동 접촉 태클을 이 한 곳에서만 처리해 같은 프레임 중복 발동을 막는다
            if (requested || (IsTackleCharged && IsOpponentInContact()))
            {
                MeleeSystem melee = controller.MeleeSystem;
                // 쿨다운 중이면 공격이 안 나가니 대시를 끊지 않는다
                if (melee == null || !melee.CanTackle) return;

                AttackType type = IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
                if (!melee.BeginTackle(type)) return;
                _attackElapsed = 0f;
                _attackType = type;
                _attackPower = TackleChargePower;
                // 유도는 시작할 때 상대가 사거리(25m) 안이어야 한다. 밖이면 그 공격은 직선
                _homing = type == AttackType.DashAttack && IsOpponentWithin(DashAttack.homingRange);
            }
        }

        /// <summary>지금 방향으로 대시 속도. 몸(정면·카메라 기준)은 돌리지 않는다 — 하드락이면 상대를 계속 본다(MechController).</summary>
        private void Advance(MechController controller)
        {
            controller.SetMoveVelocity(_heading, Move.dashSpeed);
        }

        /// <summary>
        /// 대시 어택·태클 진행: 발생 시간 동안은 돌진만, 그 뒤 판정 구간(activeTime) 동안 상대가 hitDistance 안이면 판정.
        /// 대시 어택은 유도 중이면 상대 쪽으로 초당 homingTurnRate°까지만 꺾는다 — 사이드스텝으로 옆으로 빠지면 못 따라갈 수 있다.
        /// </summary>
        private void TickAttack(MechController controller)
        {
            MeleeSystem melee = controller.MeleeSystem;
            _attackElapsed += Time.fixedDeltaTime;
            if (_homing) UpdateHoming();
            Advance(controller);
            if (melee == null) return;

            float startup = melee.TackleStartup(_attackType);
            if (_attackElapsed < startup) return;
            bool expired = _attackElapsed >= startup + ActiveTime(_attackType);

            if (IsOpponentWithin(HitDistance(_attackType)))
            {
                // 속도를 먼저 0으로: 패리 반사·맞딜 넉백이 이 뒤에 들어와야 지워지지 않는다
                Vector3 velocity = controller.Rb.linearVelocity;
                controller.Rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
                // 판정하는 동안엔 공격 중이 아닌 것으로: 패리 반사·맞딜 넉백이 대시를 끝내도 "취소"로 알리지 않게
                float elapsed = _attackElapsed;
                _attackElapsed = -1f;
                // 몸 정면이 아니라 돌진 방향 기준 부채꼴 (옆·뒤 대시 중에도 진행 방향으로 부딪힌다)
                HitResult? result = melee.ResolveTackle(_attackPower, _attackType, _heading);
                if (result != null)
                {
                    FinishAttack(melee, result);
                    return;
                }
                _attackElapsed = elapsed;
                // 거리는 닿았는데 정면 부채꼴 밖(옆으로 스침) — 판정 구간이 남았으면 계속 돈다
                if (!expired) Advance(controller);
            }

            if (expired)
            {
                controller.Rb.linearVelocity = new Vector3(0f, controller.Rb.linearVelocity.y, 0f);
                FinishAttack(melee, null);
            }
        }

        /// <summary>대시 어택 유도: 상대가 사거리 안이고 아직 앞쪽이면 그쪽으로 꺾는 속도 상한만큼 돈다. 지나치면 유도 끝.</summary>
        private void UpdateHoming()
        {
            if (!TryGetToOpponent(out Vector3 toOpponent, out float distance)) return;
            if (distance > DashAttack.homingRange) return;
            if (Vector3.Dot(_heading, toOpponent) <= 0f)
            {
                _homing = false;
                return;
            }
            float maxRadians = DashAttack.homingTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime;
            _heading = Vector3.RotateTowards(_heading, toOpponent, maxRadians, 0f);
            _heading.y = 0f;
            _heading.Normalize();
        }

        /// <summary>
        /// 공격 끝: 맞힘·막힘·빗나감이면 recoveryStun(0.5초) 경직, 패리당함·맞딜이면 경직 없음(상대 쪽에서 밀려남을 준다).
        /// </summary>
        private void FinishAttack(MeleeSystem melee, HitResult? result)
        {
            _attackElapsed = -1f;
            if (result != HitResult.Parried && result != HitResult.Clash) melee.ApplyStun(melee.TackleRecoveryStun);
            // 패리 반사·맞딜 넉백이 이미 대시를 끝냈을 수 있다 (MechController.HoldMoveControl)
            if (StateMachine.IsDashing) Leave(DashExitReason.Tackle, StateMachine.IdleState);
        }

        /// <summary>
        /// 맞딜 판정용 (MeleeSystem.ReceiveHit): 내 대시 어택·태클이 seconds 안에 상대를 맞힐 상황인지 —
        /// 발생 시간이 그 안에 끝나고, 상대가 (판정 거리 + 그동안 다가갈 거리) 안이며 정면 부채꼴 안.
        /// </summary>
        public bool WouldHitWithin(float seconds)
        {
            if (_attackElapsed < 0f) return false;
            MeleeSystem melee = StateMachine.MechController.MeleeSystem;
            if (melee == null) return false;
            if (melee.TackleStartup(_attackType) - _attackElapsed > seconds) return false;
            return IsOpponentWithin(HitDistance(_attackType) + Move.dashSpeed * seconds) && melee.IsOpponentInTackleArc(_heading);
        }

        /// <summary>대시 어택·태클끼리 맞딜: 공격을 끊고 멈춘다(경직 없음). 밀려남은 부르는 쪽(MeleeSystem)이 넣는다.</summary>
        public void EndByClash()
        {
            Rigidbody rb = StateMachine.MechController.Rb;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            // 맞딜로 소진됐으니 취소 알림 없이 끝낸다
            _attackElapsed = -1f;
            _tackleRequested = false;
            Leave(DashExitReason.Tackle, StateMachine.IdleState);
        }

        private float HitDistance(AttackType type) => type == AttackType.Tackle ? Tackle.hitDistance : DashAttack.hitDistance;
        private float ActiveTime(AttackType type) => type == AttackType.Tackle ? Tackle.activeTime : DashAttack.activeTime;

        private bool TryGetToOpponent(out Vector3 direction, out float distance)
        {
            direction = Vector3.zero;
            distance = float.MaxValue;
            MechController controller = StateMachine.MechController;
            MyDongari.Core.MechIdentity opponent = controller.Identity != null ? controller.Identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Rigidbody targetRb = opponent.GetComponent<Rigidbody>();
            Vector3 targetPosition = targetRb != null ? targetRb.position : opponent.transform.position;
            Vector3 offset = targetPosition - controller.Rb.position;
            distance = offset.magnitude;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f) return false;
            direction = offset.normalized;
            return true;
        }

        /// <summary>상대가 기체 기준 3D 거리 range 안인지.</summary>
        private bool IsOpponentWithin(float range)
        {
            TryGetToOpponent(out _, out float distance);
            return distance <= range;
        }

        /// <summary>
        /// 공격 버튼으로 어깨 태클 요청. 받아들이면 true(다음 물리 스텝에 시작), 쿨다운·발생 중이면 false
        /// (입력 버퍼가 다시 시도한다).
        /// </summary>
        public bool RequestTackle()
        {
            if (_airborne) return false;
            MeleeSystem melee = StateMachine.MechController.MeleeSystem;
            if (_tackleRequested) return true;
            if (_attackElapsed >= 0f || melee == null || !melee.CanTackle) return false;
            _tackleRequested = true;
            return true;
        }

        /// <summary>넉백을 받아 대시가 끝남 (MechController.HoldMoveControl). 공격 중이었으면 Exit에서 취소된다.</summary>
        public void EndByKnockback()
        {
            Leave(DashExitReason.Other, StateMachine.IdleState);
        }

        /// <summary>벽·지형에 정면으로 부딪혀 대시가 끝남 (MechController). 짧게 미끄러지며 선다.</summary>
        public void EndByWall()
        {
            Leave(DashExitReason.Wall, StateMachine.DashStopState);
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
            if (melee == null || !melee.IsOpponentInTackleArc(_heading)) return false;

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

            // 공격 중에 대시가 끝났다면(스턴·넉백·라운드 리셋 등) 그 공격은 판정 없이 취소
            if (_attackElapsed >= 0f)
            {
                _attackElapsed = -1f;
                MeleeSystem melee = StateMachine.MechController.MeleeSystem;
                if (melee != null) melee.CancelTackle(_attackType);
            }
        }
    }
}
