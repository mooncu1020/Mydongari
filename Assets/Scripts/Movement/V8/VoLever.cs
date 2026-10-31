using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 레버 해석 (V8.1). L스틱이 stickThreshold(0.3) 이상이면 그 방향으로 민 것 — 걷기·점프는 즉시 그 방향의 최대 속도(디지털에 가깝게).
    /// V8의 누적값 모델(8.5초에 걸쳐 빨라짐)은 잘못된 해석이라 V8.1에서 뺐다.
    /// 사양서의 "레버 이동 벡터 × 0.7~1.0(상대 8.3~11.8m)"은 걷기·점프 속력 배율(DistanceScale)로 읽는다.
    /// 방향 코드(대시 방향 잠금용): 0 앞, 1 앞좌, 2 좌, 3 뒤좌, 4 뒤, 5 뒤우, 6 우, 7 앞우 (-1 = 중립).
    /// </summary>
    public class VoLever
    {
        /// <summary>지금 스틱의 8방향 코드 (-1 = 중립).</summary>
        public int Direction { get; private set; } = -1;
        /// <summary>민 방향(x = 오른쪽, y = 앞, 크기 1). 중립이면 0.</summary>
        public Vector2 Stick { get; private set; }
        /// <summary>민 방향의 각(°, 앞 0, 오른쪽 +). 속도 곡선 조회용.</summary>
        public float Angle => Stick.sqrMagnitude > 0.000001f ? Mathf.Atan2(Stick.x, Stick.y) * Mathf.Rad2Deg : 0f;
        /// <summary>상대 거리 입력 축소 (8.3m 안 0.7, 11.8m까지 선형으로 1).</summary>
        public float DistanceScale { get; private set; } = 1f;
        public bool IsNeutral => Direction < 0;

        /// <summary>방향 코드 → 기준 정면에서 잰 각(°, 오른쪽 +). 코드가 커질수록 왼쪽(반시계)으로 돈다.</summary>
        public static float Offset(int code) => code < 0 ? 0f : Mathf.DeltaAngle(0f, -code * 45f);

        /// <summary>스틱 → 8방향 코드. threshold 미만이면 중립.</summary>
        public static int Code(Vector2 stick, float threshold)
        {
            if (stick.magnitude < threshold) return -1;
            float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
            int code = Mathf.RoundToInt(-angle / 45f);
            return ((code % 8) + 8) % 8;
        }

        public void Reset()
        {
            Direction = -1;
            Stick = Vector2.zero;
            DistanceScale = 1f;
        }

        /// <summary>한 틱.</summary>
        public void Tick(Vector2 stick, BernardTuning.V8Values v, float distanceScale)
        {
            Direction = Code(stick, v.stickThreshold);
            Stick = Direction >= 0 ? stick.normalized : Vector2.zero;
            DistanceScale = distanceScale;
        }
    }
}
