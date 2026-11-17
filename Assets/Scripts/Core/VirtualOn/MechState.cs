namespace MyDongari.Core.VirtualOn
{
    /// <summary>
    /// 상태 머신이 돌리는 상태의 바탕. 이동 상태(VoState — 이동기의 60Hz 틱에서 Aux/Main)와
    /// 우리 게임 상태(GameState — 착지 경직·런지·넉다운, 틱마다 Update → FixedUpdate)가 이걸 상속한다.
    /// </summary>
    public abstract class MechState
    {
        public abstract void Enter();
        public abstract void Update();
        public abstract void FixedUpdate();
        public abstract void Exit();
    }
}
