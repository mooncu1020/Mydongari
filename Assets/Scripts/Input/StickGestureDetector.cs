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
        [Tooltip("↑ 튕기기 후 이 시간(초) 안에 다시 ↑ 끝에 도달하면 전력질주(↑↑)")]
        public float doubleFlickWindow = 0.35f;
        [Tooltip("밀고 있던 스틱이 중립에 들어온 뒤 이 시간(초) 안에 ↗/↖ 끝에 도달하면 슬라이딩")]
        public float slideWindow = 0.2f;
        [Tooltip("슬라이딩 ↗/↖로 인정하는 각도 범위(위쪽 기준, °). 사이드스텝 부채꼴과 겹치지 않게 둔다")]
        public float slideMinAngle = 20f;
        public float slideMaxAngle = 55f;
    }

    /// <summary>
    /// 왼쪽 스틱 "튕기기" 제스처 판정기. 걷기·달리기(스틱을 계속 밀고 있음)와 구분하려고
    /// "중립 → 끝 → 중립"이 짧은 시간 안에 일어난 경우만 튕기기로 본다.
    /// 방향은 카메라와 무관하게 스틱 원래 방향 기준.
    /// </summary>
    public class StickGestureDetector
    {
        private enum Phase { Neutral, Rising, AtEdge, Held }

        // 한 프레임짜리 출력
        public bool FlickDown { get; private set; }
        public int FlickSide { get; private set; }
        public bool SlideTriggered { get; private set; }
        public int SlideDirection { get; private set; }
        // 유지되는 출력: ↑↑ 이후 스틱을 놓을 때까지 전력질주 입력 유지
        public bool SprintHeld { get; private set; }

        private Phase _phase = Phase.Neutral;
        private float _leaveNeutralTime;
        private float _edgeTime;
        private Vector2 _edgeDir;
        private float _lastUpFlickTime = float.NegativeInfinity;
        private float _neutralFromPushTime = float.NegativeInfinity;
        private bool _suppressFlick;

        public void Reset()
        {
            _phase = Phase.Neutral;
            SprintHeld = false;
            _lastUpFlickTime = float.NegativeInfinity;
            _neutralFromPushTime = float.NegativeInfinity;
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
                        OnEdgeReached(now, s);
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
                    if (now - _edgeTime <= s.flickReleaseTime && !_suppressFlick) OnFlick(_edgeDir, now, s);
                    EnterNeutralFromPush(now);
                }
                else if (now - _edgeTime > s.flickReleaseTime)
                {
                    _phase = Phase.Held;
                }
                return;
            }

            if (_phase == Phase.Held && neutral)
            {
                EnterNeutralFromPush(now);
            }
        }

        private void ClearOneShots()
        {
            FlickDown = false;
            FlickSide = 0;
            SlideTriggered = false;
        }

        private void EnterNeutralFromPush(float now)
        {
            _phase = Phase.Neutral;
            _neutralFromPushTime = now;
            _suppressFlick = false;
            // 스틱을 놓으면 전력질주 입력도 끝 (SprintState가 Brake로 넘어간다)
            SprintHeld = false;
        }

        private void OnEdgeReached(float now, StickGestureSettings s)
        {
            _suppressFlick = false;
            float angleFromUp = Vector2.SignedAngle(_edgeDir, Vector2.up); // 오른쪽이 +

            // ↑↑: 직전 ↑ 튕기기 후 곧바로 다시 ↑ 끝 → 전력질주 (두 번째는 놓지 않고 그대로 밀고 간다)
            if (Mathf.Abs(angleFromUp) <= s.sectorHalfAngle && now - _lastUpFlickTime <= s.doubleFlickWindow)
            {
                SprintHeld = true;
                _lastUpFlickTime = float.NegativeInfinity;
                _suppressFlick = true;
                return;
            }

            // 슬라이딩: 밀고 있던 스틱을 중립으로 튕긴 직후 ↗/↖
            float absAngle = Mathf.Abs(angleFromUp);
            if (now - _neutralFromPushTime <= s.slideWindow &&
                absAngle >= s.slideMinAngle && absAngle <= s.slideMaxAngle)
            {
                SlideTriggered = true;
                SlideDirection = angleFromUp > 0f ? 1 : -1;
                _suppressFlick = true;
            }
        }

        private void OnFlick(Vector2 dir, float now, StickGestureSettings s)
        {
            float angleFromUp = Vector2.SignedAngle(dir, Vector2.up);
            float absAngle = Mathf.Abs(angleFromUp);

            if (absAngle <= s.sectorHalfAngle)
            {
                // ↑ 한 번은 그 자체로는 아무것도 안 하고 ↑↑을 기다린다
                _lastUpFlickTime = now;
            }
            else if (absAngle >= 180f - s.sectorHalfAngle)
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
