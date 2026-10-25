using UnityEngine;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>전력질주가 끝난 이유. 슬라이딩 대기 여부와 로그(sprint_exit_*)에 쓴다.</summary>
    public enum SprintExitReason
    {
        Other,
        // 패드 두 스틱 경로
        Angle,
        // 각도로 풀렸지만 L은 시계(↗ 쪽)·R은 반시계(↖ 쪽)로 안쪽으로 굴린 경우. 중립 해제처럼 슬라이딩 대기를 건다
        AngleRoll,
        LeftReleased,
        RightReleased,
        // 버튼 경로 (키보드 Space)
        ButtonReleased,
        Exhausted,
        QuickTurn,
        SideStep,
        Stun,
        Tackle
    }

    /// <summary>
    /// 패드 두 스틱 전력질주·슬라이딩 판정 (E6). 왼쪽 스틱 = 방향, 오른쪽 스틱 = 가속.
    /// 입력 값은 InputReader의 스틱 원래 값만 쓰고, 기준 수치는 BernardTuning(이동)에서 매번 읽는다.
    /// 오른쪽 스틱 "꺾기"는 상태와 무관하게 계속 추적해야 해서(중립을 벗어난 시각) 상태머신이 매 프레임 Tick한다.
    /// </summary>
    public class TwinStickSprintInput
    {
        private bool _rightInNeutral = true;
        private float _rightLeaveNeutralTime;
        // 이번 꺾기에서 임계값 도달 판정을 이미 했는지 (도달 순간 한 번만 본다. 천천히 민 경우 다시 보지 않게)
        private bool _rightReachJudged;
        // 진입 조건을 만족한 프레임. 같은 프레임에 걷기 → 달리기로 막 넘어가는 경우를 위해 다음 프레임까지 유효
        private int _entryFrame = int.MinValue;

        public void Reset()
        {
            _rightInNeutral = true;
            _rightReachJudged = false;
            _entryFrame = int.MinValue;
        }

        public void Tick(Vector2 left, Vector2 right, float now, BernardTuning.MovementValues m)
        {
            float rightMag = right.magnitude;
            if (rightMag < m.sprintStickNeutral)
            {
                _rightInNeutral = true;
                _rightReachJudged = false;
                return;
            }

            if (_rightInNeutral)
            {
                _rightInNeutral = false;
                _rightLeaveNeutralTime = now;
            }

            if (_rightReachJudged || rightMag < m.sprintStickThreshold) return;
            _rightReachJudged = true;

            if (now - _rightLeaveNeutralTime > m.sprintEntryTime) return;
            if (left.magnitude < m.sprintStickThreshold) return;
            if (Vector2.Angle(left, right) > m.sprintEntryAngle) return;
            _entryFrame = Time.frameCount;
        }

        /// <summary>
        /// 두 스틱 전력질주 진입 입력이 있으면 true를 돌려주고 소모한다.
        /// 꺾은 프레임(또는 그 다음 프레임)이고, 지금도 두 스틱이 진입 각도 안에 있어야 한다.
        /// </summary>
        public bool ConsumeEntry(Vector2 left, Vector2 right, BernardTuning.MovementValues m)
        {
            if (Time.frameCount - _entryFrame > 1) return false;
            _entryFrame = int.MinValue;
            return left.magnitude >= m.sprintStickThreshold &&
                   right.magnitude >= m.sprintStickThreshold &&
                   Vector2.Angle(left, right) <= m.sprintEntryAngle;
        }

        /// <summary>두 스틱 전력질주 유지 판정. 유지면 Other, 아니면 해제 이유. 놓음을 각도보다 먼저 본다.</summary>
        public static SprintExitReason CheckHold(Vector2 left, Vector2 right, BernardTuning.MovementValues m)
        {
            if (left.magnitude < m.sprintStickThreshold) return SprintExitReason.LeftReleased;
            if (right.magnitude < m.sprintStickThreshold) return SprintExitReason.RightReleased;
            if (Vector2.Angle(left, right) > m.sprintExitAngle) return SprintExitReason.Angle;
            return SprintExitReason.Other;
        }

        /// <summary>
        /// 각도 해제가 "슬라이딩으로 굴리기"인지: 마지막 유지 방향(두 스틱 사이) 기준으로
        /// 왼쪽 스틱은 시계 방향, 오른쪽 스틱은 반시계 방향으로 둘 다 돌았을 때만 true.
        /// 한쪽만 돌았거나 반대로 벌어지면 false (바로 제동).
        /// </summary>
        public static bool IsInwardRoll(Vector2 heldHeading, Vector2 left, Vector2 right)
        {
            if (heldHeading == Vector2.zero) return false;
            // SignedAngle은 반시계가 +
            return Vector2.SignedAngle(heldHeading, left) < 0f &&
                   Vector2.SignedAngle(heldHeading, right) > 0f;
        }

        /// <summary>슬라이딩 입력: 왼쪽 ↗ + 오른쪽 ↖ (각도는 오른쪽 = 0°, 위 = 90°).</summary>
        public static bool IsSlideInput(Vector2 left, Vector2 right, BernardTuning.MovementValues m)
        {
            return left.magnitude >= m.slideStickThreshold &&
                   right.magnitude >= m.slideStickThreshold &&
                   IsNearAngle(left, m.slideLeftAngle, m.slideAngleTolerance) &&
                   IsNearAngle(right, m.slideRightAngle, m.slideAngleTolerance);
        }

        private static bool IsNearAngle(Vector2 stick, float targetDeg, float toleranceDeg)
        {
            float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(angle, targetDeg)) <= toleranceDeg;
        }
    }
}
