using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// 레버 해석 (V8, 사양서 2.1). 원본은 트윈 레버 두 개지만 우리는 L스틱 하나 — 두 레버를 같은 방향으로 민 것으로 본다.
    /// 방향마다 누적값(0 ~ 1536)을 두고, 미는 방향은 1024까지 상승률만큼 빠르게(앞 약 11f) 그 뒤 1536까지 1씩,
    /// 반대 방향은 반대감쇠, 나머지는 감쇠로 줄인다. 누적값을 방향 코사인으로 합한 것이 레버 이동 벡터(두 레버 1024 = 0.8, 1536 = 1.2).
    /// 방향 코드: 0 앞, 1 앞좌, 2 좌, 3 뒤좌, 4 뒤, 5 뒤우, 6 우, 7 앞우 (-1 = 중립).
    /// </summary>
    public class VoLever
    {
        private readonly float[] _acc = new float[8];

        /// <summary>지금 스틱의 8방향 코드 (-1 = 중립).</summary>
        public int Direction { get; private set; } = -1;
        /// <summary>레버 이동 벡터 (x = 오른쪽, y = 앞), 거리 보정 포함. 두 레버 1024면 크기 0.8.</summary>
        public Vector2 Vector { get; private set; }
        /// <summary>레버 이동 벡터의 각(°, 앞 0, 오른쪽 +). 속도 곡선 조회용.</summary>
        public float Angle => Vector.sqrMagnitude > 0.000001f ? Mathf.Atan2(Vector.x, Vector.y) * Mathf.Rad2Deg : 0f;
        public bool IsNeutral => Direction < 0;

        /// <summary>방향 코드 → 기체 정면 기준 각(°, 오른쪽 +). 코드가 커질수록 왼쪽(반시계)으로 돈다.</summary>
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
            for (int i = 0; i < 8; i++) _acc[i] = 0f;
            Direction = -1;
            Vector = Vector2.zero;
        }

        /// <summary>한 틱 진행. near = 근접 구역(표가 바뀜), distanceScale = 상대 거리 보정(0.7 ~ 1).</summary>
        public void Tick(Vector2 stick, bool near, BernardTuning.V8Values v, float distanceScale)
        {
            Direction = Code(stick, v.stickThreshold);
            int[] rise = near ? v.riseNear : v.riseFar;
            int[] opposite = near ? v.oppositeDecayNear : v.oppositeDecayFar;
            int threshold = near ? v.thresholdNear : v.thresholdFar;

            for (int c = 0; c < 8; c++)
            {
                if (c == Direction)
                {
                    _acc[c] += _acc[c] < 1024f ? Get(rise, c) : 1f;
                    if (_acc[c] > 1536f) _acc[c] = 1536f;
                    continue;
                }
                bool isOpposite = Direction >= 0 && c == (Direction + 4) % 8;
                _acc[c] -= isOpposite ? Get(opposite, c) : Get(v.decay, c);
                // 기울이지 않은 방향이 임계값 밑으로 내려가면 지운다 (중립일 때도)
                if (_acc[c] < threshold) _acc[c] = 0f;
            }

            // 한 레버의 성분 = Σ 누적값 × 2 × (sin, cos)(방향), 두 레버(같은 값) 합 / 5120 → Σ 누적값 × (sin, cos) / 1280
            Vector2 sum = Vector2.zero;
            for (int c = 0; c < 8; c++)
            {
                if (_acc[c] <= 0f) continue;
                float rad = Offset(c) * Mathf.Deg2Rad;
                sum += new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * _acc[c];
            }
            Vector = sum / 1280f * distanceScale;
        }

        private static float Get(int[] table, int index) => table != null && index < table.Length ? table[index] : 0f;
    }
}
