using UnityEngine;

namespace MyDongari.Input
{
    /// <summary>스틱 튕기기 판정 수치. InputReader 인스펙터에서 조정한다.</summary>
    [System.Serializable]
    public class StickGestureSettings
    {
        [Tooltip("스틱 크기가 이 값 미만이면 중립")]
        public float neutralThreshold = 0.3f;
        [Tooltip("스틱 크기가 이 값 초과면 끝까지 민 것")]
        public float edgeThreshold = 0.8f;
        [Tooltip("중립을 벗어난 뒤 이 시간(초) 안에 끝에 도달해야 튕기기 후보")]
        public float flickReachTime = 0.15f;
        [Tooltip("끝에 도달한 뒤 이 시간(초) 안에 중립으로 돌아와야 튕기기. 더 오래 밀고 있으면 그냥 이동")]
        public float flickReleaseTime = 0.25f;
        [Tooltip("↑↓←→ 판정 부채꼴 반각(°)")]
        public float sectorHalfAngle = 30f;
    }

    /// <summary>
    /// 왼쪽 스틱 "튕기기" 제스처 판정기. 걷기·달리기(스틱을 계속 밀고 있음)와 구분하려고
    /// "중립 → 끝 → 중립"이 짧은 시간 안에 일어난 경우만 튕기기로 본다.
    /// 방향은 카메라와 무관하게 스틱 원래 방향 기준.
    /// (E6: 전력질주 ↑↑·슬라이딩 ↗↖은 두 스틱 조작으로 바뀌어 여기서 뺐다 — Movement/TwinStickSprintInput)
    /// </summary>
    public class StickGestureDetector
    {
        private enum Phase { Neutral, Rising, AtEdge, Held }

        // 한 프레임짜리 출력
        public bool FlickDown { get; private set; }
        public int FlickSide { get; private set; }

        private Phase _phase = Phase.Neutral;
        private float _leaveNeutralTime;
        private float _edgeTime;
        private Vector2 _edgeDir;

        public void Reset()
        {
            _phase = Phase.Neutral;
            ClearOneShots();
        }

        public void Tick(Vector2 stick, float now, StickGestureSettings s)
        {
            ClearOneShots();

            float mag = stick.magnitude;
            bool neutral = mag < s.neutralThreshold;
            bool edge = mag > s.edgeThreshold;

            // 한 프레임에 중립 → 끝으로 바로 가는 경우도 있어서 단계를 순서대로 이어서 본다
            if (_phase == Phase.Neutral && !neutral)
            {
                _phase = Phase.Rising;
                _leaveNeutralTime = now;
            }

            if (_phase == Phase.Rising)
            {
                if (neutral)
                {
                    _phase = Phase.Neutral;
                }
                else if (edge)
                {
                    if (now - _leaveNeutralTime <= s.flickReachTime)
                    {
                        _phase = Phase.AtEdge;
                        _edgeTime = now;
                        _edgeDir = stick.normalized;
                    }
                    else
                    {
                        // 천천히 밀었다 = 일반 이동
                        _phase = Phase.Held;
                    }
                }
                return;
            }

            if (_phase == Phase.AtEdge)
            {
                if (neutral)
                {
                    if (now - _edgeTime <= s.flickReleaseTime) OnFlick(_edgeDir, s);
                    _phase = Phase.Neutral;
                }
                else if (now - _edgeTime > s.flickReleaseTime)
                {
                    _phase = Phase.Held;
                }
                return;
            }

            if (_phase == Phase.Held && neutral)
            {
                _phase = Phase.Neutral;
            }
        }

        private void ClearOneShots()
        {
            FlickDown = false;
            FlickSide = 0;
        }

        private void OnFlick(Vector2 dir, StickGestureSettings s)
        {
            float angleFromUp = Vector2.SignedAngle(dir, Vector2.up);
            float absAngle = Mathf.Abs(angleFromUp);

            // ↑ 튕기기는 쓰지 않는다 (E6에서 ↑↑ 전력질주 삭제)
            if (absAngle >= 180f - s.sectorHalfAngle)
            {
                FlickDown = true;
            }
            else if (Mathf.Abs(absAngle - 90f) <= s.sectorHalfAngle)
            {
                FlickSide = angleFromUp > 0f ? 1 : -1;
            }
        }
    }
}
