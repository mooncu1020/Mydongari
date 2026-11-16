using UnityEngine;

namespace MyDongari.Game.Combat
{
    /// <summary>
    /// 근접 명중 판정의 기하 (순수 계산). 원본은 칼끝이 한 틱 동안 쓸고 지나간 면과 피격 구 7개의 겹침인데(전투 사양서 1.4·6.1·6.2),
    /// 골격 길이를 확정하지 못해 우리 근사를 쓴다: 몸 앞에서 사거리 끝까지 이어진 공격 캡슐(원본의 칼 밑동 ~ 칼끝)을
    /// 지난 틱 위치 → 이번 틱 위치로 쓸어서 상대 피격 캡슐과 겹치는지 본다. (예전엔 사거리 끝의 구 하나라 붙은 상대가 빠졌다.)
    /// </summary>
    public static class HitGeometry
    {
        /// <summary>쓸기를 몇 조각으로 나눠 보는지. 한 틱 이동(최대 0.7m)·회전(최대 14°)을 반지름 합(1m 안팎)보다 촘촘히 자른다.</summary>
        public const int SweepSteps = 4;

        /// <summary>
        /// 공격 캡슐(축 fromA-fromB, 반지름 radius)이 한 틱 동안 toA-toB로 움직인 자취와 피격 캡슐 사이의 틈(m).
        /// 0 이하면 겹침(명중), 양수면 그만큼 모자람. 축의 두 끝을 같은 비율로 보간한 선분들로 자취를 근사한다.
        /// </summary>
        public static float SweptCapsuleGap(Vector3 fromA, Vector3 fromB, Vector3 toA, Vector3 toB, float radius,
            Vector3 capsuleA, Vector3 capsuleB, float capsuleRadius, int steps = SweepSteps)
        {
            steps = Mathf.Max(1, steps);
            float best = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float sqr = SegmentSegmentSqrDistance(Vector3.Lerp(fromA, toA, t), Vector3.Lerp(fromB, toB, t), capsuleA, capsuleB);
                if (sqr < best) best = sqr;
            }
            return Mathf.Sqrt(best) - radius - capsuleRadius;
        }

        /// <summary>공격 캡슐이 쓸고 간 자취가 피격 캡슐과 겹치는지.</summary>
        public static bool SweptCapsuleHitsCapsule(Vector3 fromA, Vector3 fromB, Vector3 toA, Vector3 toB, float radius,
            Vector3 capsuleA, Vector3 capsuleB, float capsuleRadius)
        {
            return SweptCapsuleGap(fromA, fromB, toA, toB, radius, capsuleA, capsuleB, capsuleRadius) <= 0f;
        }

        /// <summary>
        /// 반지름 sphereRadius인 구가 from → to로 움직인 자취가 캡슐(축 capsuleA ~ capsuleB, 반지름 capsuleRadius)과 겹치는지.
        /// = 두 선분 사이 최단 거리 ≤ 반지름 합.
        /// </summary>
        public static bool SweptSphereHitsCapsule(Vector3 from, Vector3 to, float sphereRadius, Vector3 capsuleA, Vector3 capsuleB, float capsuleRadius)
        {
            float reach = sphereRadius + capsuleRadius;
            return SegmentSegmentSqrDistance(from, to, capsuleA, capsuleB) <= reach * reach;
        }

        /// <summary>선분 p1-q1과 p2-q2 사이 최단 거리의 제곱.</summary>
        public static float SegmentSegmentSqrDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            const float epsilon = 1e-8f;
            float s, t;
            if (a <= epsilon && e <= epsilon) return r.sqrMagnitude;
            if (a <= epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= epsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > epsilon ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }
            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        /// <summary>
        /// 서 있는 캡슐(발 높이 feetY, 높이 height, 반지름 radius, 수평 중심 center)을 가운데 기준으로 scale배 줄인 축과 반지름.
        /// 점프 중 피격 판정 축소용 (원본: 상승 7개 → 3개, 하강 → 2개 구).
        /// </summary>
        public static void UprightCapsule(Vector3 center, float feetY, float height, float radius, float scale, out Vector3 a, out Vector3 b, out float scaledRadius)
        {
            scaledRadius = radius * scale;
            float scaledHeight = Mathf.Max(height * scale, scaledRadius * 2f);
            float mid = feetY + height * 0.5f;
            float half = scaledHeight * 0.5f - scaledRadius;
            a = new Vector3(center.x, mid - half, center.z);
            b = new Vector3(center.x, mid + half, center.z);
        }
    }
}
