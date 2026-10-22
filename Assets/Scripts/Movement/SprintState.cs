using UnityEngine;
using MyDongari.Combat;

namespace MyDongari.Movement
{
    public class SprintState : MechState
    {
        private float _tackleChargeTimer = 0f;
        public bool IsTackleCharged => _tackleChargeTimer >= 1.5f;
        public float TackleChargePower => Mathf.Clamp01((_tackleChargeTimer - 1.5f) / 1.5f);

        public SprintState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        public override void Enter()
        {
            _tackleChargeTimer = 0f;
            StateMachine.MechController.MechCamera.LockToForward();
        }

        public override void Update()
        {
            if (StateMachine.IsStunned)
            {
                StateMachine.ChangeState(StateMachine.IdleState);
                return;
            }

            if (StateMachine.MechController.BoostGauge.IsExhausted)
            {
                StateMachine.ChangeState(StateMachine.BrakeState);
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

            if (IsTackleCharged)
            {
                Collider[] hits = Physics.OverlapSphere(
                    StateMachine.MechController.transform.position, 2f);
                foreach (Collider hit in hits)
                {
                    if (!StateMachine.MechController.Identity.IsOpponentCollider(hit)) continue;

                    MeleeSystem melee = StateMachine.MechController.MeleeSystem;
                    // 쿨다운 중이면 부딪쳐도 태클이 안 나가니 질주를 끊지 않는다
                    if (!melee.CanTackle) return;

                    // 속도를 먼저 0으로: 패리 반사 넉백이 이 뒤에 들어와야 지워지지 않는다
                    StateMachine.MechController.Rb.linearVelocity = Vector3.zero;
                    HitResult? result = melee.TryTackle(TackleChargePower);
                    if (result != HitResult.Parried)
                    {
                        melee.ApplyStun(melee.TackleRecoveryStun);
                    }
                    StateMachine.ChangeState(StateMachine.IdleState);
                    return;
                }
            }
        }

        public override void Exit()
        {
        }
    }
}