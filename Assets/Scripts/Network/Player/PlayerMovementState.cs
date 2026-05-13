namespace OskarMike.Network.Player
{
    /// <summary>
    /// 플레이어 자세 상태.
    /// 각 상태마다 이동 속도 배율과 CharacterController 높이가 달라진다.
    /// </summary>
    public enum PlayerPosture : byte
    {
        Stand  = 0,
        Crouch = 1,
        Prone  = 2
    }

    /// <summary>
    /// 플레이어 이동 상태 (자세와 독립적으로 관리).
    /// </summary>
    public enum PlayerMoveState : byte
    {
        Idle    = 0,
        Walk    = 1,
        Sprint  = 2
    }
}
