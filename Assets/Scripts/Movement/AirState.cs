using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>
    /// 공중 (V5): △ 점프로 뛰었거나, 땅에서 떨어졌거나(발판 끝), 공중 대시가 끝나 떨어지는 중.
    /// 점프는 최고 높이 jumpHeight(5m)에 jumpRiseTime(1초)에 닿도록 초기 속도와 중력을 정한다 (높고 느린 스러스터 점프).
    /// 뛰는 순간 거리와 상관없이 몸(과 몸을 따라오는 카메라)을 jumpTurnTime(0.2초)에 걸쳐 상대 쪽으로 돌린다(점프 락온).
    /// 공중에선 가드·패리·앵커 클로·공격 불가, 부스트 회복 멈춤. 공중 ○ = 공중 대시(DashState). 착지하면 LandingState.
    /// </summary>
    public class AirState : MechState
    {
        private BernardTuning.MovementValues Move => StateMachine.MechController.Tuning.Movement;

        private bool _pendingJump;
        private bool _pendingLandingLag;
        private float _elapsed;
        // 착지 경직을 받아야 하는지: 점프·공중 대시·공중 피격이면 true, 그냥 떨어진 거면 false
        private bool _needsLandingLag;
        // 점프 락온 회전
        private bool _turning;
        private float _turnTimer;
        private Quaternion _turnFrom;
        private Quaternion _turnTo;

        /// <summary>점프했을 때 (로그·연출용).</summary>
        public event Action OnJumped;

        /// <summary>점프 락온으로 몸을 상대 쪽으로 돌리는 중 (카메라가 몸에 바로 붙어 따라온다).</summary>
        public bool IsJumpTurning => _turning;
        /// <summary>착지하면 경직을 받는지.</summary>
        public bool NeedsLandingLag => _needsLandingLag;

        /// <summary>점프 중력 (m/s²). 높이 h를 시간 t에 오르려면 g = 2h / t².</summary>
        public static float JumpGravity(BernardTuning.MovementValues m)
        {
            float t = Mathf.Max(0.05f, m.jumpRiseTime);
            return 2f * m.jumpHeight / (t * t);
        }

        public AirState(MechStateMachine stateMachine) : base(stateMachine)
        {
        }

        /// <summary>다음 Enter를 점프로 (TryStartJump가 부른다).</summary>
        public void PrepareJump()
        {
            _pendingJump = true;
        }

        /// <summary>다음 Enter를 착지 경직이 필요한 낙하로 (공중 대시가 끝났거나 공중에서 맞음).</summary>
        public void PrepareFallWithLandingLag()
        {
            _pendingLandingLag = true;
        }

        /// <summary>이미 공중 상태일 때 맞았다 — 착지 경직을 받게 한다.</summary>
        public void MarkLandingLag()
        {
            _needsLandingLag = true;
        }

        public override void Enter()
        {
            MechController controller = StateMachine.MechController;
            _elapsed = 0f;
            _turning = false;
            _needsLandingLag = _pendingJump || _pendingLandingLag;
            _pendingLandingLag = false;

            if (_pendingJump)
            {
                _pendingJump = false;
                // 높이 h, 오르는 시간 t: 초기 속도 v = 2h / t
                float t = Mathf.Max(0.05f, Move.jumpRiseTime);
                float vy = 2f * Move.jumpHeight / t;
                Vector3 v = controller.Rb.linearVelocity;
                controller.Rb.linearVelocity = new Vector3(v.x, vy, v.z);
                BeginJumpTurn(controller);
                OnJumped?.Invoke();
            }
        }

        /// <summary>점프 락온: 거리와 상관없이 상대 쪽 수평 방향으로 jumpTurnTime에 걸쳐 돈다.</summary>
        private void BeginJumpTurn(MechController controller)
        {
            MyDongari.Core.MechIdentity opponent = controller.Identity != null ? controller.Identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;
            Rigidbody targetRb = opponent.GetComponent<Rigidbody>();
            Vector3 toOpponent = (targetRb != null ? targetRb.position : opponent.transform.position) - controller.Rb.position;
            toOpponent.y = 0f;
            if (toOpponent.sqrMagnitude < 0.0001f) return;
            _turnFrom = controller.Rb.rotation;
            _turnTo = Quaternion.LookRotation(toOpponent.normalized, Vector3.up);
            _turnTimer = 0f;
            _turning = true;
        }

        public override void Update()
        {
            _elapsed += Time.deltaTime;

            // 공중 ○ = 공중 대시 (스턴·탈진이면 안 나감)
            if (StateMachine.TryStartDash()) return;

            if (CanLand()) StateMachine.Land(_needsLandingLag);
        }

        /// <summary>뛴 직후 몇 스텝은 아직 바닥 검사에 걸리므로, 잠깐 지나고 내려오는 중일 때만 착지.</summary>
        private bool CanLand()
        {
            MechController controller = StateMachine.MechController;
            return controller.IsGrounded && _elapsed >= Move.jumpGroundIgnoreTime && controller.Rb.linearVelocity.y <= 0.5f;
        }

        public override void FixedUpdate()
        {
            MechController controller = StateMachine.MechController;

            if (_turning)
            {
                _turnTimer += Time.fixedDeltaTime;
                float t = Move.jumpTurnTime > 0f ? Mathf.Clamp01(_turnTimer / Move.jumpTurnTime) : 1f;
                if (!controller.IsExternallyControlled) controller.Rb.MoveRotation(Quaternion.Slerp(_turnFrom, _turnTo, Mathf.SmoothStep(0f, 1f, t)));
                if (t >= 1f) _turning = false;
            }

            controller.ApplyAirGravity(JumpGravity(Move));

            // 공중 이동: 스틱 쪽 airMoveSpeed로 airAccel만큼씩 다가간다 (달리던 속도는 서서히 줄어든다 — 높고 느린 점프)
            InputReader input = StateMachine.InputReader;
            Vector3 velocity = controller.Rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 target = input != null && input.IsMoveInputActive
                ? controller.BodyRelativeDirection(input.MoveInput) * Move.airMoveSpeed
                : Vector3.zero;
            controller.SetHorizontalVelocity(Vector3.MoveTowards(horizontal, target, Move.airAccel * Time.fixedDeltaTime));
        }

        public override void Exit()
        {
            _turning = false;
        }
    }
}
