using CaseFileOmission.Contracts;

namespace CaseFileOmission.Reports
{
    public sealed class TruthComparisonResult
    {
        public bool PerpetratorClaimed;
        public bool MatchesActualTruth;
    }

    /// <summary>
    /// §9-3 "사건 진실과 보고서의 일치 여부 검사"의 1차 구현. 일치하지 않는다고 제출을 막지
    /// 않는다 — 플레이어가 의도적으로 실제 진실과 다른 결론을 보고서에 담는 것은 수사 교란의
    /// 핵심 플레이이기 때문이다(06페이지 확정). 이 비교 결과는 공식 결론 처리나 디버깅에 쓴다.
    /// </summary>
    public static class ReportTruthComparator
    {
        public static TruthComparisonResult Compare(ContractInstance contract, ContractReportState report)
        {
            var perpetratorEntry = report.GetSection(ReportSection.Perpetrator);
            bool claimed = !string.IsNullOrEmpty(perpetratorEntry.ClaimedNpcId);

            return new TruthComparisonResult
            {
                PerpetratorClaimed = claimed,
                MatchesActualTruth = claimed && perpetratorEntry.ClaimedNpcId == contract.Truth.PerpetratorId
            };
        }
    }
}
