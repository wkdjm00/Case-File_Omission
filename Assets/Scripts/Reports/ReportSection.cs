namespace CaseFileOmission.Reports
{
    /// <summary>
    /// 04페이지 2026-11-09 구현 명세 "의뢰 보고서 — 단서 연결 방식" 표의 8개 항목.
    /// 메인 사건 최종 보고서(단서 이름 입력형 타자기 연출)와는 다른, 의뢰 사건 전용 보고서다.
    /// </summary>
    public enum ReportSection
    {
        CaseSummary,
        KeyPersons,
        CaseProgress,
        Perpetrator,
        Motive,
        MeansAndTool,
        DamageAndResult,
        SubCrime
    }
}
