namespace CaseFileOmission.Core
{
    /// <summary>
    /// 06페이지 2026-11-10 확정: 조사 시간은 아침·저녁 두 구간이며 아침 → 저녁 → 다음 날 아침 순서로 넘어간다.
    /// (과거 §2-2의 오전/오후 표기를 최신 용어로 바꿨다. 저장 데이터의 정수 값은 그대로다.)
    /// </summary>
    public enum TimeBlock
    {
        Morning,
        Evening
    }
}
