using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>
    /// 공중 (V5): △ 점프로 뛰었거나, 땅에서 떨어졌거나(발판 끝), 공중 대시가 끝나 떨어지는 중.
    /// 점프 (V6c, 템진 기준): 이륙 준비 jumpTakeoffTime(2프레임) 뒤 상승 속도 jumpRiseSpeed(23m/s), 중력 jumpGravity(31.5m/s²) →
    /// 최고점 약 8.4m(0.73초). 내려올 땐 maxFallSpeed(7.5m/s)로 상한이 걸려 천천히 내려온다.
    /// 공중 수평 이동은 방향별 airMoveSpeed(옆이 제일 빠름). 공중 대시는 상승 중에만(CanAirDash).
    /// 뛰는 순간 거리와 상관없이 몸(과 몸을 따라오는 카메라)을 jumpTurnTime(0.2초)에 걸쳐 상대 쪽으로 돌린다(점프 락온).
    /// 공중에선 가드·패리·앵커 클로·공격 불가, 부스트 회복 멈춤. 공중 ○ = 공중 대시(DashState). 착지하면 LandingState.
    /// </summary>
    public class AirState : MechState
    {
        private BernardTuning.MovementValues Move => StateMachine.MechController.Tuning.Movement;

        private bool _pendingJump;
        private bool _pendingLandingLag;
        private float _elapsed;
        // 이륙 준비 남은 시간(초). 0보다 크면 아직 안 떴다
        private float _takeoffTimer;
        // 착지 경직을 받아야 하는지: 점프·공중 대시·공중 피격이면 true, 그냥 떨어진 거면 false
        private bool _needsLandingLag;
        // 점프 락온 회전
        private bool _turning;
        private float _turnTimer;
        private Quaternion _turnFrom;
        private Quaternion _turnTo;

        /// <summary>점프했을 때 (로그·연출용).</summary>
        public event Action OnJumped;

        /// <summary>V8 점프도 같은 이벤트로 알린다.</summary>
        public void RaiseJumped() => OnJumped?.Invoke();

        /// <summary>점프 락온으로 몸을 상대 쪽으로 돌리는 중 (카메라가 몸에 바로 붙어 따라온다).</summary>
        public bool IsJumpTurning => _turning;
        /// <summary>착지하면 경직을 받는지.</summary>
        public bool NeedsLandingLag => _needsLandingLag;

        /// <summary>이륙 준비 중(아직 안 뜸).</summary>
        public bool IsTakingOff => _takeoffTimer > 0f;

        /// <summary>공중 대시를 할 수 있는지 (V6c: 뜬 뒤 상승 중에만).</summary>
        public bool CanAirDash => !IsTakingOff && StateMachine.MechController.Rb.linearVelocity.y > 0f;

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

            _takeoffTimer = 0f;
            if (_pendingJump)
            {
                _pendingJump = false;
                // 이륙 준비 2프레임: 그 자리 속도 그대로 웅크렸다가 뜬다 (준비가 0이면 바로)
                _takeoffTimer = Move.jumpTakeoffTime;
                BeginJumpTurn(controller);
                OnJumped?.Invoke();
                if (_takeoffTimer <= 0f) Launch(controller);
            }
        }

        /// <summary>이륙: 세로 속도를 jumpRiseSpeed로. 착지 판정 유예(jumpGroundIgnoreTime)는 여기서부터 잰다.</summary>
        private void Launch(MechController controller)
        {
            _takeoffTimer = 0f;
            _elapsed = 0f;
            Vector3 v = controller.Rb.linearVelocity;
            controller.Rb.linearVelocity = new Vector3(v.x, Move.jumpRiseSpeed, v.z);
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
            // 이륙 준비 중엔 아직 땅이다 — 대시·착지 판정 없음 (뜨는 건 물리 스텝에서)
            if (IsTakingOff) return;
            _elapsed += Time.deltaTime;

            // 공중 ○ = 공중 대시 (상승 중에만, 스턴·탈진이면 안 나감 — MechStateMachine.TryStartDash)
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

            if (IsTakingOff)
            {
                _takeoffTimer -= Time.fixedDeltaTime;
                if (_takeoffTimer <= 0f) Launch(controller);
                return;
            }

            controller.ApplyAirGravity(Move.jumpGravity);

            // 공중 이동: 스틱 쪽 방향별 airMoveSpeed로 airAccel만큼씩 다가간다 (뛰기 전 속도는 서서히 줄어든다)
            InputReader input = StateMachine.InputReader;
            Vector3 velocity = controller.Rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 target = input != null && input.IsMoveInputActive
                ? controller.BodyRelativeDirection(input.MoveInput) * Move.airMoveSpeed.Evaluate(input.MoveInput)
                : Vector3.zero;
            controller.SetHorizontalVelocity(Vector3.MoveTowards(horizontal, target, Move.airAccel * Time.fixedDeltaTime));
        }

        public override void Exit()
        {
            _turning = false;
            _takeoffTimer = 0f;
        }
    }
}
