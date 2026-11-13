using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 이동기의 지면·벽·천장 판정 (사양서 6장). 리지드바디는 키네마틱이라 충돌은 캐스트·겹침 조회로만 본다.
    /// 지면 = Walkable 레이어의 위를 향한 면(GroundLayers), 벽 = 정적 지형의 옆면(Wall 레이어는 면 방향 무관), 5u 이하 단차는 넘는다.
    /// 충돌 크기는 사양서 추정(반경 5u·높이 15u) 대신 기체에 붙은 캡슐 콜라이더를 쓴다.
    /// </summary>
    public partial class VoMotor
    {
        /// <summary>
        /// 벽 보정 (사양서 6.2): 다음 위치까지 캡슐을 쓸어 보고, 정적 지형에 닿으면 그 앞까지 간 뒤 남은 이동에서 법선 성분을 지워 미끄러진다.
        /// 정면이면 남는 게 없어 멈춘다. 속력 값은 안 줄인다(벽에서 벗어나면 원래 속도). 지상은 캡슐 바닥을 5u 들어 올려 낮은 단차는 넘는다.
        /// </summary>
        private void MoveHorizontal(Vector3 delta, bool liftForStep)
        {
            Depenetrate();
            Vector3 remaining = delta;
            for (int i = 0; i < 3 && remaining.sqrMagnitude > 0.0000001f; i++)
            {
                float distance = remaining.magnitude;
                Vector3 dir = remaining / distance;
                if (!CastWall(dir, distance, liftForStep, out RaycastHit hit))
                {
                    _position += remaining;
                    return;
                }
                float move = Mathf.Max(0f, hit.distance - WallSkin);
                _position += dir * move;
                remaining -= dir * move;
                Vector3 n = hit.normal;
                n.y = 0f;
                if (n.sqrMagnitude < 0.000001f) return;
                n.Normalize();
                float into = Vector3.Dot(remaining, n);
                if (into < 0f) remaining -= n * into;
            }
        }

        private const float WallSkin = 0.02f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Overlaps = new Collider[16];

        private void CapsulePoints(Vector3 root, bool liftForStep, out Vector3 p1, out Vector3 p2, out float radius)
        {
            float height = _capsule != null ? _capsule.height : (_body != null ? _body.bounds.size.y : 2f);
            radius = _capsule != null ? _capsule.radius : 0.5f;
            Vector3 center = root + (_capsule != null ? _capsule.center : Vector3.zero);
            float feet = root.y + _footOffset;
            float bottom = feet + radius + (liftForStep ? V.stepHeight * U : 0f);
            float top = feet + height - radius;
            if (bottom > top) bottom = top;
            p1 = new Vector3(center.x, bottom, center.z);
            p2 = new Vector3(center.x, top, center.z);
            radius *= 0.98f;
        }

        private bool CastWall(Vector3 dir, float distance, bool liftForStep, out RaycastHit nearest)
        {
            nearest = default;
            CapsulePoints(_position, liftForStep, out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, dir, Hits, distance + WallSkin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (!IsStaticObstacle(hit.collider)) continue;
                // 시작부터 겹친 것은 밀어내기(Depenetrate)가 맡는다
                if (hit.distance <= 0f) continue;
                // 위를 향한 면(바닥·완만한 경사)은 벽이 아니다 — Wall 레이어는 면 방향과 상관없이 벽
                if (!GroundLayers.IsWallLayer(hit.collider) && hit.normal.y >= Host.GroundMinNormalY) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>정적 지형에 파묻혀 있으면 수평으로 밀어낸다.</summary>
        private void Depenetrate()
        {
            if (_body == null) return;
            CapsulePoints(_position, true, out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                Collider other = Overlaps[i];
                if (!IsStaticObstacle(other)) continue;
                if (Physics.ComputePenetration(_body, _position, rotation, other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float depth))
                {
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.000001f) continue;
                    _position += direction.normalized * depth;
                }
            }
        }

        private bool IsStaticObstacle(Collider c)
        {
            if (c == null || c.isTrigger || c.attachedRigidbody != null) return false;
            if (Host.IsOwnCollider(c)) return false;
            return true;
        }

        /// <summary>
        /// 발밑 지면: feet 위 up(m)에서 아래로 up + down(m)까지 레이. Walkable + 위를 향한 면(GroundLayers) 중 가장 높은 점.
        /// </summary>
        public bool ProbeGround(float feet, float up, float down, out float groundY)
        {
            groundY = float.MinValue;
            Vector3 origin = new Vector3(_position.x, feet + up + 0.01f, _position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, up + down + 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (Host.IsOwnCollider(hit.collider)) continue;
                if (!GroundLayers.IsGround(hit.collider, hit.normal, Host.GroundMinNormalY)) continue;
                if (hit.point.y > groundY)
                {
                    groundY = hit.point.y;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>공중에서 prevFeet → newFeet로 내려오는 사이에 지면을 지났는지 (착지 판정, 사양서 6.1: y ≤ 지면).</summary>
        public bool ProbeLanding(float prevFeet, float newFeet, out float groundY)
        {
            float up = Mathf.Max(0f, prevFeet - newFeet) + 0.05f;
            return ProbeGround(newFeet, up, 0.01f, out groundY) && groundY >= newFeet - 0.0001f;
        }

        /// <summary>천장: 머리 위로 up(m) 안에 정적 지형이 있는지 (점프 상승 중 → 급강하).</summary>
        public bool CheckCeiling(float up)
        {
            if (up <= 0f) return false;
            CapsulePoints(_position, false, out _, out Vector3 top, out float radius);
            int count = Physics.SphereCastNonAlloc(top, radius, Vector3.up, Hits, up, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsStaticObstacle(Hits[i].collider) && Hits[i].distance > 0f) return true;
            }
            return false;
        }

        /// <summary>공중 상태가 y += vy(u/f)를 할 때. 착지했으면 true (y = 지면, vy = 0, 땅에 있음).</summary>
        public bool AddVyAndCheckLanding()
        {
            float prev = FeetY;
            FeetY += Vy * U;
            IsGrounded = false;
            if (Vy <= 0f && ProbeLanding(prev, FeetY, out float ground))
            {
                FeetY = ground;
                Vy = 0f;
                IsGrounded = true;
                return true;
            }
            return false;
        }
    }
}
