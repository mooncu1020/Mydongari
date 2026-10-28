using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>
    /// 하체(Pelvis)가 있는 모델(bernard, H1 구조 Root → Pelvis → Body)의 이동 연출.
    /// G2 상하체 분리: 하체는 이동 방향을 묵직하게 따라 돌고, 상체는 카메라(하드 락온이면 상대)를 향한다.
    /// 둘의 비틀림이 한계를 넘으면 하체가 끌려 돈다. 머리는 상체 위에서 한 번 더 늦게 따라본다.
    /// 방향은 전부 월드 yaw(°)로 들고 있다가 기체 루트 기준 오프셋으로 관절 Yaw에 넣는다 (Body는 Pelvis 자식이라 상대 yaw).
    /// 하체 뼈가 없는 임시 상자에서는 아무것도 하지 않는다 (예전 동작 그대로).
    /// </summary>
    public partial class MechVisualRig
    {
        private float _pelvisYaw, _bodyYaw, _headYaw;
        private bool _facingReady;
        private Vector3 _lastRootPosition;

        /// <summary>상하체를 나눠 상체 방향을 따로 계산하는 중인지 (공격 방향 = 상체 방향, MechFacing).</summary>
        public bool HasUpperBodyFacing => _facingReady && HasLowerBody && isActiveAndEnabled;
        /// <summary>상체가 향한 수평 방향(월드).</summary>
        public Vector3 UpperBodyForward => Quaternion.Euler(0f, _bodyYaw, 0f) * Vector3.forward;

        private float RootYaw => transform.eulerAngles.y;

        /// <summary>상체가 볼 방향: 하드 락온이면 상대, 아니면 내 카메라 정면, 카메라가 없으면(봇) 기체 정면.</summary>
        private float LookYaw()
        {
            Vector3 dir;
            if (_lockOn != null && _lockOn.IsHardLocked && _lockOn.Target != null) dir = _lockOn.Target.position - transform.position;
            else if (_identity != null && _identity.CameraTransform != null) dir = _identity.CameraTransform.forward;
            else dir = transform.forward;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : RootYaw;
        }

        private Vector3 HorizontalVelocity()
        {
            if (_rb == null) return Vector3.zero;
            Vector3 velocity = _rb.linearVelocity;
            velocity.y = 0f;
            return velocity;
        }

        private void ResetFacing()
        {
            float yaw = RootYaw;
            _pelvisYaw = yaw;
            _bodyYaw = yaw;
            _headYaw = yaw;
            _lastRootPosition = transform.position;
            _facingReady = true;
        }

        private void UpdateFacing(BernardTuning.VisualValues v, float dt)
        {
            if (!HasLowerBody) return;

            // 처음, 또는 라운드 리셋처럼 순간이동했으면 전부 기체 정면으로 맞춘다 (먼 방향에서 천천히 돌아오지 않게)
            if (!_facingReady || (transform.position - _lastRootPosition).sqrMagnitude > v.facingSnapDistance * v.facingSnapDistance)
            {
                ResetFacing();
            }
            _lastRootPosition = transform.position;

            float lookYaw = LookYaw();
            _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, lookYaw, v.upperBodyTurnSpeed * dt);

            // 하체: 움직이면 이동 방향(뒤로 가면 뒷걸음이라 반대 방향), 회전 속도 상한으로 묵직하게.
            // 서 있을 때는 제자리 회전 스텝(G4)이 돌린다
            Vector3 velocity = HorizontalVelocity();
            if (!UpdateTurnSteps(v, dt) && velocity.magnitude >= v.pelvisMoveMinSpeed)
            {
                float moveYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(_bodyYaw, moveYaw)) > v.pelvisBackpedalAngle) moveYaw += 180f;
                _pelvisYaw = Mathf.MoveTowardsAngle(_pelvisYaw, moveYaw, v.pelvisTurnSpeed * dt);
            }

            // 비틀림 한계: 넘으면 하체가 끌려 돈다
            float twist = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
            if (Mathf.Abs(twist) > v.twistLimit) _pelvisYaw = _bodyYaw - Mathf.Sign(twist) * v.twistLimit;

            // 머리: 볼 방향을 headLookLag만큼 늦게 따라가고, 상체 기준 ±headLookLimit 안에서만
            float k = v.headLookLag > 0f ? 1f - Mathf.Exp(-dt / v.headLookLag) : 1f;
            _headYaw = Mathf.LerpAngle(_headYaw, lookYaw, k);
            float headOffset = Mathf.Clamp(Mathf.DeltaAngle(_bodyYaw, _headYaw), -v.headLookLimit, v.headLookLimit);

            // 계층이 Root → Pelvis → Body → Head라 각 관절엔 부모 기준 yaw
            _pelvis.Yaw = Mathf.DeltaAngle(RootYaw, _pelvisYaw);
            if (_body != null) _body.Yaw = Mathf.DeltaAngle(_pelvisYaw, _bodyYaw);
            if (_head != null) _head.Yaw = headOffset;
        }

        // G4에서 채운다. 제자리 회전 스텝이 하체를 돌리는 중이면 true
        private bool UpdateTurnSteps(BernardTuning.VisualValues v, float dt) => false;
    }
}
