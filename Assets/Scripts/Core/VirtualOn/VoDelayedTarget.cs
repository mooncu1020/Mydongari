using UnityEngine;

namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 지연 표적 위치 (이동 사양서 1.2 ② 본 갱신 1, 전투 사양서 1.3 "추적 회전") — 순수 계산, 틱 단위.
    /// 상대의 같은 행동이 delayedTargetFrames(15f) 이상 이어지면 매 틱 (지난 값 + 상대 위치) × 0.5로 따라가고, 아니면 상대 위치로 바로 맞춘다.
    /// 근접 추적 회전이 상대의 지금 위치 대신 이 값을 표적으로 쓴다 — 옆으로 꾸준히 걷는 상대를 한 틱쯤 늦게 쫓게 된다.
    /// "같은 행동"이 누구의 행동인지 사양서에 없어 상대의 행동(상태)으로 정했다 (docs/QUESTIONS.md).
    /// 원본이 즉시 맞춤 경로에서 y 자리에 상대 z를 넣는 버릇은 따라 하지 않는다 — 표적은 x·z만 쓰므로 결과가 같다.
    /// </summary>
    public class VoDelayedTarget
    {
        /// <summary>지금 표적 위치 (y는 상대 위치 그대로).</summary>
        public Vector3 Position { get; private set; }
        /// <summary>상대가 지금 행동을 이어 온 틱 수 (바뀐 틱 = 0).</summary>
        public int SameActionFrames { get; private set; }
        /// <summary>이번 틱이 반씩 따라가기였는지.</summary>
        public bool IsLagging { get; private set; }

        private object _lastAction;
        private bool _hasValue;

        /// <summary>다음 갱신에서 상대 위치로 바로 맞추게 한다 (라운드 시작·순간이동).</summary>
        public void Reset()
        {
            _hasValue = false;
            _lastAction = null;
            SameActionFrames = 0;
            IsLagging = false;
        }

        /// <summary>
        /// 한 틱 갱신. action은 상대의 지금 행동을 가리키는 값(상태 객체 등, 같은 행동이면 같은 참조) — null이면 "모름"으로 보고 바뀌지 않은 것으로 센다.
        /// enabled가 꺼져 있으면(우리 스타일) 늘 상대 위치 그대로다.
        /// </summary>
        public Vector3 Update(Vector3 opponentPosition, object action, bool enabled, int lagFrames, float follow)
        {
            if (!_hasValue || !ReferenceEquals(action, _lastAction)) SameActionFrames = 0;
            else SameActionFrames = Mathf.Min(SameActionFrames + 1, 9999);
            _lastAction = action;

            IsLagging = enabled && _hasValue && SameActionFrames >= lagFrames;
            if (IsLagging)
            {
                Vector3 p = Position + (opponentPosition - Position) * follow;
                p.y = opponentPosition.y;
                Position = p;
            }
            else
            {
                Position = opponentPosition;
            }
            _hasValue = true;
            return Position;
        }
    }
}
