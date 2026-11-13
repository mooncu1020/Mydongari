using UnityEngine;
using MyDongari.Combat;
using MyDongari.Core;
using MyDongari.Mech;
using MyDongari.Movement.V8;

namespace MyDongari.Movement
{
    /// <summary>
    /// 이동기(코어)와 우리 게임의 연결 (IVoHost 구현). 기체마다 하나, MechController가 만든다.
    /// 코어가 묻는 것 — 수치, 입력, 스턴, 콤보 감속, 외부 속도 모드, 가드 자동 선회, 상대, 카메라 — 을 전투·입력 컴포넌트에서 읽어 준다.
    /// 원본에 없는 기체끼리 분리·몸 충돌 반동도 여기서 한다.
    /// </summary>
    public class VoHostAdapter : IVoHost
    {
        private readonly MechController _controller;
        private readonly VoInputAdapter _input;
        private readonly Collider _body;
        private readonly CapsuleCollider _capsule;

        public VoHostAdapter(MechController controller)
        {
            _controller = controller;
            _input = new VoInputAdapter(controller.InputReader);
            _body = controller.GetComponent<Collider>();
            _capsule = _body as CapsuleCollider;
        }

        private MeleeSystem Melee => _controller.MeleeSystem;
        private MechStateMachine StateMachine => _controller.StateMachine;
        private MechIdentity Identity => _controller.Identity;

        // ---- 수치 ----
        public BernardTuning.V8Values Spec => _controller.Tuning.V8;
        public float GroundMinNormalY => _controller.Tuning.Movement.groundMinNormalY;

        // ---- 입력 ----
        public bool HasInput => _input.HasInput;
        public void ResetInput() => _input.Reset();
        public VoInputFrame ReadInput() => _input.Read();

        /// <summary>화면 프레임마다 (MechController.Update): 마우스 이동량을 모은다.</summary>
        public void FrameUpdate()
        {
            if (_controller.Motor == null || !_controller.Motor.IsActive) return;
            _input.FrameUpdate(_controller.Tuning.Movement.mouseTurnSensitivity);
        }

        // ---- 게임 상태 ----
        public bool IsStunned => Melee != null && Melee.IsStunned;
        public float MoveSpeedScale => Melee != null ? Melee.MoveSpeedScale : 1f;

        public bool IsExternalMode
        {
            get
            {
                MechState state = StateMachine != null ? StateMachine.CurrentState : null;
                return _controller.IsMoveHeld || _controller.IsExternallyControlled ||
                       state is LungeState || state is KnockdownState;
            }
        }

        /// <summary>
        /// 가드 자동 선회 (V9a): ✕를 누른 엄지로는 R스틱을 못 쓰니, 가드 중엔 몸이 상대 쪽으로 저절로 돈다.
        /// 상대가 정면 안이면 느리게(132°/s), 밖이면 빠르게(360°/s) — 등 뒤로 돌아 들어온 상대를 가드로 따라잡되 즉시는 아니다.
        /// </summary>
        public bool TryGetAutoTurnToOpponent(out float insideRateUnits, out float outsideRateUnits)
        {
            BernardTuning.V8Values v = Spec;
            insideRateUnits = v.guardTurnInside;
            outsideRateUnits = v.guardTurnOutside;
            return Melee != null && Melee.IsGuarding;
        }

        // ---- 상대·충돌 ----
        public Transform Opponent
        {
            get
            {
                MechIdentity opponent = Identity != null ? Identity.Opponent : null;
                return opponent != null ? opponent.transform : null;
            }
        }

        public bool IsOwnCollider(Collider collider) => Identity != null && Identity.IsOwnCollider(collider);

        public int PlayerIndex => Identity != null ? Identity.PlayerIndex : 0;

        /// <summary>
        /// 기체끼리: 사양서엔 밀어내기가 없지만 키네마틱끼리는 서로 통과하므로 캡슐 반경 합만큼만 떨어뜨린다(내 쪽만 — 상대는 자기 틱에).
        /// 지상 대시가 공격 없이 닿으면 몸 충돌 반동(V3)으로 넘긴다.
        /// </summary>
        public void AfterIntegrate(MechMotor motor)
        {
            Transform opponent = Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;
            Vector3 other = MechMotor.OpponentPosition(opponent);
            Collider otherCollider = opponent.GetComponent<Collider>();
            float otherRadius = otherCollider is CapsuleCollider cc ? cc.radius : (otherCollider != null ? Mathf.Min(otherCollider.bounds.extents.x, otherCollider.bounds.extents.z) : 0.5f);
            float myRadius = _capsule != null ? _capsule.radius : 0.5f;
            float minDistance = myRadius + otherRadius;
            Vector3 offset = motor.Position - other;
            float heightOverlap = otherCollider != null ? otherCollider.bounds.extents.y + (_body != null ? _body.bounds.extents.y : 1f) : 2f;
            if (Mathf.Abs(offset.y) >= heightOverlap) return;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance >= minDistance) return;
            Vector3 apart = distance > 0.0001f ? offset / distance : -motor.Forward;
            motor.Nudge(apart * (minDistance - distance));

            MechStateMachine sm = StateMachine;
            if (sm.IsDashing && sm.ActiveDash != null && !sm.ActiveDash.IsAttacking && !sm.IsAirborne)
            {
                if (sm.ActiveDash.IsTackleCharged && sm.ActiveDash.RequestTackle()) return;
                _controller.ApplyBump(apart);
                MechController otherController = opponent.GetComponent<MechController>();
                if (otherController != null) otherController.ApplyBump(-apart);
            }
        }

        // ---- 카메라 ----
        public void TickCamera(MechMotor motor)
        {
            MyDongari.Camera.MechCamera cam = _controller.MechCamera;
            if (cam == null && Identity != null) cam = Identity.MechCamera;
            if (cam != null) cam.TickV8(motor);
        }
    }
}
