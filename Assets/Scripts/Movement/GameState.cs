namespace MyDongari.Movement
{
    /// <summary>
    /// 우리 게임이 이동 상태 위에 얹는 상태(착지 경직·런지·넉다운)의 바탕. 이동기가 틱마다 Update → FixedUpdate를 한 번씩 대신 부르고,
    /// 그동안 기체는 외부 속도로 움직인다(런지·넉다운) 또는 제자리에 선다(착지 경직).
    /// </summary>
    public abstract class GameState : MechState
    {
        protected readonly MechStateMachine StateMachine;

        protected GameState(MechStateMachine stateMachine)
        {
            StateMachine = stateMachine;
        }
    }
}
