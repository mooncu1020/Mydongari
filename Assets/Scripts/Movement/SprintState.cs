using UnityEngine;
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
            StateMachine.MechController.MechCamera.LockToForward();
        }

        public override void Update()
        {
            // 스턴(가드 깨짐 포함)이면 질주 종료 — 발생 중이던 태클은 Exit에서 취소된다
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            // 태클 발생 시간 동안은 어깨를 내민 채 그대로 돌진한다 (제동·퀵턴·사이드스텝·탈진 전환 안 받음)
            if (_pendingTackleElapsed >= 0f) return;

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
                return;
            }

            // 스펙: 초신지선회·사이드스텝은 전력질주 중에도 쓸 수 있다
            if (StateMachine.InputReader.QuickTurnInput)
            {
                StateMachine.ChangeState(StateMachine.QuickTurnState);
                return;
            }

            if (StateMachine.InputReader.SideStepDirection != 0)
            {
                StateMachine.ChangeState(StateMachine.SideStepState);
                return;
            }

            _tackleChargeTimer += Time.deltaTime;



            if (StateMachine.InputReader.MoveInput.sqrMagnitude < 0.25f ||
                !StateMachine.InputReader.SprintInput)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
            }
        }

        public override void FixedUpdate()
        {


            Vector2 input = StateMachine.InputReader.MoveInput;
            Vector3 camForward = StateMachine.MechController.CameraTransform.forward;
            Vector3 camRight = StateMachine.MechController.CameraTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;
            if (moveDir != Vector3.zero)
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
            StateMachine.ChangeState(StateMachine.IdleState);
        }

        /// <summary>공격 버튼으로 어깨 태클 요청. 다음 물리 스텝에 처리된다.</summary>
        public void RequestTackle()
        {
            _tackleRequested = true;
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