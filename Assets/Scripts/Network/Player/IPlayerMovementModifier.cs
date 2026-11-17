namespace OskarMike.Network.Player
{
    /// <summary>
    /// 플레이어 이동에 고정 배율/제약을 거는 컴포넌트용 훅 (예: 더플백 운반).
    /// PlayerNetworkController 가 서버에서 GetSpeed()/CanFullSprint() 계산 시 조회한다.
    /// </summary>
    public interface IPlayerMovementModifier
    {
        /// <summary>이동 속도 배율 (1 = 변화 없음).</summary>
        float SpeedMultiplier { get; }

        /// <summary>false 면 풀 스프린트 불가.</summary>
        bool AllowFullSprint { get; }
    }
}
