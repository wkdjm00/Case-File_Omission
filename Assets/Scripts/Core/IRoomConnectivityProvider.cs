namespace CaseFileOmission.Core
{
    /// <summary>
    /// "연결되지 않은 방에서는 상호작용할 수 없다"(§2-4)를 검사하기 위한 공용 인터페이스.
    /// Core는 Room 모듈을 직접 참조하지 않으므로, Room 모듈의 공간 그래프가 이 인터페이스를
    /// 구현해 ActionGuards와 연결한다.
    /// </summary>
    public interface IRoomConnectivityProvider
    {
        bool RoomExists(string roomId);
        bool AreConnected(string roomIdA, string roomIdB);
    }
}
