namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 단서의 자료 형태. 03·04페이지에 반복 등장하는 형태(문서/증언/녹음/물건)만 우선 포함한다.
    /// 전체 자료 형태 목록은 확정되지 않았으므로 필요 시 추가한다.
    /// </summary>
    public enum ClueFormat
    {
        Document,
        Testimony,
        Recording,
        PhysicalObject
    }

    /// <summary>
    /// 04페이지 "단서·문서·증언 데이터" 공통 필드: 사실·부분적 사실·거짓·조작 여부.
    /// </summary>
    public enum ClueTruthStatus
    {
        Factual,
        PartiallyFactual,
        False,
        Fabricated
    }

    /// <summary>
    /// 06페이지 확정 카드 조작 입력: 뒤집기=왜곡, 플레이어 쪽으로 끌기=은폐.
    /// </summary>
    public enum ManipulationType
    {
        Distort,
        Conceal
    }

    /// <summary>
    /// 단서의 현재 물리적 소지 상태. "보고서 소유권"과는 별개 개념이다 —
    /// 한 번 발견한 단서는 보고서(레코드)에서 절대 사라지지 않지만, 누가 실제로 들고 있는지는 바뀔 수 있다.
    /// 장소·NPC 줍기 로직은 이후 단계(공간/NPC 모듈)에서 구현하며, 여기서는 상태값만 정의한다.
    /// </summary>
    /// <summary>
    /// 2026-11-16 확정 은폐: 은폐는 증거를 세계에서 없애지 않고, 플레이어가 지정한 단서 하나의 접근 가능 상태만 바꾼다.
    /// </summary>
    public enum ClueAccessState
    {
        Accessible,
        Concealed
    }

    /// <summary>마지막 은폐 시도 결과(성공·실패는 발각 여부와 별도 상태).</summary>
    public enum ConcealmentResult
    {
        NotAttempted,
        Succeeded,
        Failed
    }

    public enum ClueWorldState
    {
        HeldByPlayer,
        DroppedInWorld,
        HeldByNpc,
        HeldByPolice
    }
}
