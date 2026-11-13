using UnityEngine;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 레버 해석. 호스트가 준 레버가 중립이 아니면 그 방향으로 민 것 — 걷기·점프는 즉시 그 방향의 최대 속도(디지털에 가깝게, V8.1).
    /// 얼마나 기울여야 "민 것"인지(우리 스틱 0.3)는 호스트의 입력 어댑터가 정해서, 여기엔 중립(0) 아니면 민 방향이 온다.
    /// 원본의 누적값 모델(방향별 0~1536, 속력 × 0.8~1.2 — 사양서 2.1)은 쓰지 않는다: 레버 크기는 항상 1.
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

        // 이보다 작으면 중립 (호스트가 기울기 문턱 아래를 0으로 보내 준다)
        private const float NeutralEpsilon = 0.0001f;

        /// <summary>한 틱. stick = 호스트가 준 레버(문턱 아래는 0).</summary>
        public void Tick(Vector2 stick, float distanceScale)
        {
            Direction = Code(stick, NeutralEpsilon);
            Stick = Direction >= 0 ? stick.normalized : Vector2.zero;
            DistanceScale = distanceScale;
        }
    }
}
