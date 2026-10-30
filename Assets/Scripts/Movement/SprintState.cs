using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Combat;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>전력질주가 끝난 이유 (로그용). V3에서 트윈스틱 해제 이유(각도·스틱 놓음 등)는 삭제.</summary>
    public enum SprintExitReason
    {
        Other,
        ButtonReleased,
        Exhausted,
        SideStep,
        Stun,
        Tackle
    }

    /// <summary>
    /// 임시 전력질주 (V3 2단계): 키보드 Space를 누르고 있는 동안만. 패드 트윈스틱 진입·슬라이딩은 삭제됐다.
    /// 3단계 ○ 대시가 이 상태의 태클 차징·대시 어택 판정을 이어받는다.
    /// </summary>
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        private bool _tackleRequested = false;
        // 어깨 태클 발생 시간 경과(초). 음수면 발생 중인 태클 없음. 종류·차징은 버튼을 누른 순간 값으로 고정
        private float _pendingTackleElapsed = -1f;
        private AttackType _pendingTackleType;
        private float _pendingTacklePower;
        private BernardTuning.TackleValues Tackle => StateMachine.MechController.Tuning.Tackle;

        private SprintExitReason _exitReason;
        private bool _exitReported;

        /// <summary>전력질주가 실제로 시작됐을 때. 로그용.</summary>
        public event Action OnSprintStarted;
        /// <summary>전력질주가 끝났을 때 (이유). 로그용.</summary>
        public event Action<SprintExitReason> OnSprintEnded;
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

        public override void Enter()
        {
            _tackleChargeTimer = 0f;
            _tackleRequested = false;
            _pendingTackleElapsed = -1f;
            _exitReason = SprintExitReason.Other;
            _exitReported = false;
            OnSprintStarted?.Invoke();
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

            // 태클 발생 시간 동안은 어깨를 내민 채 그대로 돌진한다 (사이드스텝·탈진 전환 안 받음)
            if (_pendingTackleElapsed >= 0f) return;

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                Leave(SprintExitReason.Exhausted, StateMachine.RunState);
                return;
            }

            // 사이드스텝은 전력질주 중에도 쓸 수 있다
            if (StateMachine.InputReader.SideStepDirection != 0)
            {
                Leave(SprintExitReason.SideStep, StateMachine.SideStepState);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;

            InputReader input = StateMachine.InputReader;
            if (!input.IsMoveInputActive || !input.SprintInput)
            {
                Leave(SprintExitReason.ButtonReleased,
                    input.IsMoveInputActive ? (MechState)StateMachine.RunState : StateMachine.IdleState);
            }
        }

        public override void FixedUpdate()
        {
            // V3: 가속도 없이 몸 기준 방향으로 바로 그 속도. 스틱을 놓으면(태클 발생 중) 지금 속도 그대로 돌진
            InputReader input = StateMachine.InputReader;
            if (input.IsMoveInputActive)
            {
                StateMachine.MechController.SetMoveVelocity(
                    StateMachine.MechController.BodyRelativeDirection(input.MoveInput),
                    StateMachine.MechController.MaxSprintSpeed);
            }

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
            if (_pendingTackleElapsed >= 0f || melee == null || !melee.CanTackle) return false;
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