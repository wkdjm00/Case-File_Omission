namespace CaseFileOmission.Core
{
    /// <summary>
    /// "사망한 인물과는 직접 상호작용할 수 없다"(§2-4)를 검사하기 위한 공용 인터페이스.
    /// Core는 NPC·경찰 모듈을 직접 참조하지 않으므로, 각 모듈의 인물 상태 클래스가 이 인터페이스를
    /// 구현해 ActionGuards와 연결한다.
    /// </summary>
    public interface IAliveCheckable
    {
        bool IsAlive { get; }
    }
}
