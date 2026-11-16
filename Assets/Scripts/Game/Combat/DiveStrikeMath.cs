using UnityEngine;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 내려찍기(점프 중 공격 — 우리 규칙)의 궤적 계산 — 순수 계산, 틱 단위.
    /// 예비가 끝난 순간 상대 위치를 향한 직선을 잡고(Aim), 꽂히는 동안 틱마다 상대 쪽으로 조금씩만 꺾는다(Steer).
    /// 수평으로 maxHorizontal(m)보다 먼 상대는 그 방향으로 maxHorizontal 지점에 떨어지고, 진로는 항상 minDownAngle(°) 이상 아래를 향한다
    /// (상대가 나보다 위에 있어도 땅에 닿아 끝나게).
    /// </summary>
    public static class DiveStrikeMath
    {
        /// <summary>from에서 target을 향한 내리꽂는 방향(크기 1). 수평 거리는 maxHorizontal로 자르고, 아래로 minDownAngle 이상 기울인다.</summary>
        public static Vector3 Aim(Vector3 from, Vector3 target, float maxHorizontal, float minDownAngle)
        {
            Vector3 flat = target - from;
            float dy = flat.y;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 flatDir = distance > 0.0001f ? flat / distance : Vector3.zero;
            float reach = Mathf.Min(distance, Mathf.Max(0f, maxHorizontal));
            return ClampDown(flatDir * reach + Vector3.up * dy, flatDir, minDownAngle);
        }

        /// <summary>
        /// 지금 진로(direction)를 target 쪽으로 꺾는다 — 좌우(방위)와 기울기(내려가는 각)를 각각 최대 maxDegrees(°)씩만.
        /// 3D 각도 하나로 재면 가파르게 꽂을수록 작은 회전으로 좌우를 크게 따라가 버려서, 방위를 따로 묶는다. 그래서 옆 대시로 피할 수 있다.
        /// </summary>
        public static Vector3 Steer(Vector3 direction, Vector3 from, Vector3 target, float maxDegrees, float maxHorizontal, float minDownAngle)
        {
            Vector3 wanted = Aim(from, target, maxHorizontal, minDownAngle);
            direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.down;
            float step = Mathf.Max(0f, maxDegrees);
            bool hasYaw = TryYaw(direction, out float yaw);
            bool wantsYaw = TryYaw(wanted, out float wantedYaw);
            // 똑바로 아래를 향하던 중이면 방위가 없다 — 원하는 방위를 그대로 받는다
            if (!hasYaw) yaw = wantedYaw;
            else if (wantsYaw) yaw = Mathf.MoveTowardsAngle(yaw, wantedYaw, step);
            float pitch = Mathf.MoveTowards(DownAngle(direction), DownAngle(wanted), step);
            pitch = Mathf.Clamp(pitch, Mathf.Clamp(minDownAngle, 0f, 89f), 90f);
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        /// <summary>수평에서 아래로 기운 각(°). 똑바로 아래 = 90.</summary>
        private static float DownAngle(Vector3 direction) => Mathf.Asin(Mathf.Clamp(-direction.y, -1f, 1f)) * Mathf.Rad2Deg;

        /// <summary>수평 방위(°, Unity Y 회전). 수평 성분이 없으면 false.</summary>
        private static bool TryYaw(Vector3 direction, out float yaw)
        {
            yaw = 0f;
            if (direction.x * direction.x + direction.z * direction.z < 0.000001f) return false;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>v를 정규화하되 수평에서 아래로 minDownAngle(°)보다 얕으면 그 각도로 눕힌다. 수평 성분이 없으면 똑바로 아래.</summary>
        private static Vector3 ClampDown(Vector3 v, Vector3 flatDir, float minDownAngle)
        {
            float flatLength = new Vector2(v.x, v.z).magnitude;
            if (flatLength < 0.0001f || flatDir == Vector3.zero) return Vector3.down;
            float angle = Mathf.Atan2(-v.y, flatLength) * Mathf.Rad2Deg;
            float min = Mathf.Clamp(minDownAngle, 0f, 89f);
            if (angle >= min) return v.normalized;
            float rad = min * Mathf.Deg2Rad;
            return (flatDir * Mathf.Cos(rad) + Vector3.down * Mathf.Sin(rad)).normalized;
        }
    }
}
