using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Combat;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        private bool _tackleRequested = false;
        // 어깨 태클 발생 시간 경과(초). 음수면 발생 중인 태클 없음. 종류·차징은 버튼을 누른 순간 값으로 고정
        private float _pendingTackleElapsed = -1f;
        private AttackType _pendingTackleType;
        private float _pendingTacklePower;
        private BernardTuning.TackleValues Tackle => StateMachine.MechController.Tuning.Tackle;
        private BernardTuning.MovementValues Move => StateMachine.MechController.Tuning.Movement;

        // 패드 두 스틱으로 들어왔는지 (false = 버튼 Space·L3). 유지·해제 규칙이 다르다
        private bool _byStick;
        // 두 스틱 질주를 스틱을 놓아서 끝낸 뒤 슬라이딩 입력을 기다린 시간(초). 음수면 대기 아님
        private float _slideWaitTimer = -1f;
        private SprintExitReason _exitReason;
        private bool _exitReported;

        /// <summary>전력질주가 실제로 시작됐을 때 (true = 패드 두 스틱, false = 버튼). 로그용.</summary>
        public event Action<bool> OnSprintStarted;
        /// <summary>전력질주가 끝났을 때 (이유). 슬라이딩 대기로 들어가면 대기 시작 시점에 한 번. 로그용.</summary>
        public event Action<SprintExitReason> OnSprintEnded;

        /// <summary>스틱을 놓아서 끝난 뒤 슬라이딩 입력을 기다리는 중인지 (아직 상태는 SprintState).</summary>
        public bool IsWaitingForSlide => _slideWaitTimer >= 0f;
        // 차징 상태로 이 거리(m) 안에 상대가 닿으면 자동 태클 (기존 동작 유지)
        private float ContactRadius => Tackle.contactRadius;
        public bool IsTackleCharged => _tackleChargeTimer >= Tackle.chargeStartTime;
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

        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        /// <summary>RunState가 진입 직전에 부른다. 부르지 않고 들어오면 버튼 질주로 본다.</summary>
        public void PrepareEntry(bool byStick)
        {
            _byStick = byStick;
        }

        public override void Enter()
        {
            _tackleChargeTimer = 0f;
            _tackleRequested = false;
            _pendingTackleElapsed = -1f;
            _slideWaitTimer = -1f;
            _exitReason = SprintExitReason.Other;
            _exitReported = false;
            StateMachine.MechController.MechCamera.LockToForward();
            OnSprintStarted?.Invoke(_byStick);
        }

        private void Leave(SprintExitReason reason, MechState next)
        {
            _exitReason = reason;
            StateMachine.ChangeState(next);
        }

        private void ReportExit(SprintExitReason reason)
        {
            if (_exitReported) return;
            _exitReported = true;
            OnSprintEnded?.Invoke(reason);
        }

        public override void Update()
        {
            // 스턴(가드 깨짐 포함)이면 질주 종료 — 발생 중이던 태클은 Exit에서 취소된다
            if (StateMachine.IsStunned)
            {
                Leave(SprintExitReason.Stun, StateMachine.IdleState);
                return;
            }

            // 태클 발생 시간 동안은 어깨를 내민 채 그대로 돌진한다 (제동·퀵턴·사이드스텝·탈진 전환 안 받음)
            if (_pendingTackleElapsed >= 0f) return;

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                Leave(SprintExitReason.Exhausted, StateMachine.BrakeState);
                return;
            }

            // 스펙: 초신지선회·사이드스텝은 전력질주 중에도 쓸 수 있다
            if (StateMachine.InputReader.QuickTurnInput)
            {
                Leave(SprintExitReason.QuickTurn, StateMachine.QuickTurnState);
                return;
            }

            if (StateMachine.InputReader.SideStepDirection != 0)
            {
                Leave(SprintExitReason.SideStep, StateMachine.SideStepState);
                return;
            }

            InputReader input = StateMachine.InputReader;

            // 슬라이딩 대기: 이 동안은 제동으로 넘어가지 않는다. 입력이 안 들어오면 그때 제동
            if (_slideWaitTimer >= 0f)
            {
                if (TwinStickSprintInput.IsSlideInput(input.MoveStickRaw, input.LookStickRaw, Move))
                {
                    StateMachine.ChangeState(StateMachine.SlideState);
                    return;
                }
                _slideWaitTimer += Time.deltaTime;
                if (_slideWaitTimer >= Move.slideInputWindow)
                {
                    StateMachine.ChangeState(StateMachine.BrakeState);
                }
                return;
            }

            _tackleChargeTimer += Time.deltaTime;

            if (_byStick)
            {
                SprintExitReason reason = TwinStickSprintInput.CheckHold(input.MoveStickRaw, input.LookStickRaw, Move);
                if (reason == SprintExitReason.LeftReleased || reason == SprintExitReason.RightReleased)
                {
                    // 스틱 중립으로 풀림 → 질주는 끝, 슬라이딩 입력 대기 (각도로 풀린 경우는 대기 없이 제동)
                    ReportExit(reason);
                    _slideWaitTimer = 0f;
                }
                else if (reason == SprintExitReason.Angle)
                {
                    Leave(reason, StateMachine.BrakeState);
                }
                return;
            }

            if (input.MoveInput.sqrMagnitude < 0.25f || !input.SprintInput)
            {
                Leave(SprintExitReason.ButtonReleased, StateMachine.BrakeState);
            }
        }

        public override void FixedUpdate()
        {
            // 슬라이딩 대기 중엔 추진하지 않고 관성으로 미끄러진다 (태클도 안 나감)
            if (_slideWaitTimer >= 0f) return;

            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 camForward = StateMachine.MechController.CameraTransform.forward;
            Vector3 camRight = StateMachine.MechController.CameraTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;
            // 스틱을 놓는 순간 튕겨 돌아오는 작은 값으로 슬라이딩 방향이 뒤집히지 않게, 이동 입력이 있을 때만 기억
            if (moveDir != Vector3.zero && StateMachine.InputReader.IsMoveInputActive)
            {
                StateMachine.LastMoveDirection = moveDir;
            }

            StateMachine.MechController.ApplyMoveForce(
                moveDir,
                StateMachine.MechController.SprintSpeed,
                StateMachine.MechController.MaxSprintSpeed);

            MeleeSystem melee = StateMachine.MechController.MeleeSystem;

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
                // 쿨다운 중이면 태클이 안 나가니 질주를 끊지 않는다
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

        private void FinishTackle(MeleeSystem melee, float power, AttackType type)
        {
            // 속도를 먼저 0으로: 패리 반사 넉백이 이 뒤에 들어와야 지워지지 않는다
            StateMachine.MechController.Rb.linearVelocity = Vector3.zero;
            HitResult? result = melee.ResolveTackle(power, type);
            if (result != HitResult.Parried)
            {
                melee.ApplyStun(melee.TackleRecoveryStun);
            }
            Leave(SprintExitReason.Tackle, StateMachine.IdleState);
        }

        /// <summary>
        /// 공격 버튼으로 어깨 태클 요청. 받아들이면 true(다음 물리 스텝에 시작), 쿨다운·발생 중이면 false
        /// (입력 버퍼가 다시 시도한다).
        /// </summary>
        public bool RequestTackle()
        {
            MeleeSystem melee = StateMachine.MechController.MeleeSystem;
            if (_tackleRequested) return true;
            if (_slideWaitTimer >= 0f || _pendingTackleElapsed >= 0f || melee == null || !melee.CanTackle) return false;
            _tackleRequested = true;
            return true;
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
            _slideWaitTimer = -1f;
            _byStick = false;

            // 발생 중에 질주가 끝났다면(스턴·라운드 리셋 등) 그 태클은 나가지 않는다
            if (_pendingTackleElapsed >= 0f)
            {
                _pendingTackleElapsed = -1f;
                MeleeSystem melee = StateMachine.MechController.MeleeSystem;
                if (melee != null) melee.CancelTackle(_pendingTackleType);
            }
        }
    }
}