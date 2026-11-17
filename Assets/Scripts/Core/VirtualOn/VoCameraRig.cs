using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 카메라 계산 (사양서 5장). 틱 맨 앞에서 지난 틱 기체 상태로 한 틱 갱신하고, 그릴 때 두 틱 사이를 보간한 자세를 내준다.
    /// 기준점(시선 표적)은 매 틱 차이의 cameraPositionFollow만큼 기체를 따라가고, 방위는 이동기의 카메라 방위 + 선회 리드(지연 없음),
    /// 피치 8.4° 아래, 시선 표적에서 78u 뒤. 실제 카메라에 넣고 흔들림·지형 충돌을 더하는 것은 호스트(MechCamera)가 한다.
    /// </summary>
    public class VoCameraRig
    {
        private bool _ready;
        private Vector3 _anchor;
        // 12%로 목표값을 따라가는 시선 표적 높이 오프셋(u)
        private float _targetUp;
        private Vector3 _prevPosition, _position;
        private Quaternion _prevRotation = Quaternion.identity, _rotation = Quaternion.identity;

        /// <summary>한 번이라도 틱이 돌았는지 (아니면 내줄 자세가 없다).</summary>
        public bool IsReady => _ready;

        /// <summary>다음 틱에 기체 자리·방향으로 바로 맞춘다 (따라가기 감쇠 없이).</summary>
        public void Reset()
        {
            _ready = false;
        }

        /// <summary>한 틱. aspect = 카메라 가로세로 비, fovDegrees = 세로 화각 (둘 다 가로 상한 계산용 — 화각은 사양서에 없어 호스트가 정한다).</summary>
        public void Tick(VoMotor motor, float aspect, float fovDegrees)
        {
            if (motor == null) return;
            VoSpec v = motor.V;
            // V9c: 사양서 5.3대로 12%는 시선 표적 오프셋(u)에만 건다. 지금은 카메라 모드가 하나라 목표값이 일정하지만,
            // 근접·대시 카메라처럼 오프셋이 다른 모드가 생기면 이 값이 부드럽게 넘어간다
            if (!_ready) _targetUp = v.cameraTargetUp;
            else _targetUp += (v.cameraTargetUp - _targetUp) * v.cameraTargetOffsetFollow;
            Vector3 targetPoint = new Vector3(motor.Position.x,
                motor.FeetY + motor.BodyHeight * v.cameraBaseHeightRatio + _targetUp * motor.U,
                motor.Position.z);
            if (!_ready)
            {
                _anchor = targetPoint;
            }
            else
            {
                // V8.3: 기준점 위치는 매 틱 35%로 빠르게 따라간다 — 대시 때 기체가 화면 밖으로 처지지 않게
                _anchor += (targetPoint - _anchor) * v.cameraPositionFollow;
            }
            // V9c: 방위는 원본대로 이동기의 카메라 방위(기본 모드 = 기체 방위)에 지연 없이 붙는다 + 선회 리드
            float yaw = motor.CameraYaw + motor.CameraLead;
            float pitch = VoUnits.AngleUnitsToDegrees(v.cameraPitch);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float distance = v.cameraDistance * motor.U;

            // V8.3 가로 상한: 기체가 화면 가로 중앙에서 폭의 cameraMaxScreenOffset 넘게 벗어나면 기준점을 카메라 오른쪽 축으로 그만큼 당긴다.
            // 화면 가로 위치(폭 대비) = (옆 거리 / 깊이) / (2 · tan(가로 화각 / 2)). 기준점을 옆으로 옮기면 카메라도 같이 옮겨져 깊이는 그대로다
            Vector3 right = rotation * Vector3.right;
            Vector3 toTarget = targetPoint - (_anchor - rotation * (Vector3.forward * distance));
            float depth = Vector3.Dot(toTarget, rotation * Vector3.forward);
            if (depth > 0.01f)
            {
                float halfWidth = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad) * aspect * depth;
                float maxLateral = v.cameraMaxScreenOffset * 2f * halfWidth;
                float lateral = Vector3.Dot(toTarget, right);
                if (Mathf.Abs(lateral) > maxLateral) _anchor += right * (lateral - Mathf.Sign(lateral) * maxLateral);
            }
            Vector3 position = _anchor - rotation * (Vector3.forward * distance);

            _prevPosition = _ready ? _position : position;
            _prevRotation = _ready ? _rotation : rotation;
            _position = position;
            _rotation = rotation;
            _ready = true;
        }

        /// <summary>지난 틱과 이번 틱 사이를 alpha(0~1)로 보간한 자세.</summary>
        public void Evaluate(float alpha, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.Lerp(_prevPosition, _position, alpha);
            rotation = Quaternion.Slerp(_prevRotation, _rotation, alpha);
        }
    }
}
