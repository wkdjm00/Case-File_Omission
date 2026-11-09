using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Reports
{
    public enum ReportSubmissionOutcome
    {
        Submitted,
        Rejected
    }

    public sealed class ReportSubmissionResult
    {
        public ReportSubmissionOutcome Outcome;
        public ReportValidationResult ValidationResult;
    }

    /// <summary>
    /// §9-4 "보고서 결과와 엔딩 시스템 연결"의 1차 구현. 제출이 통과하면 Perpetrator 섹션의
    /// 지목 결과를 ContractOfficialConclusion에 반영한다. 공식 결론의 최종 확정(IsFinalized)과
    /// 사건 종료 처리(Outcome)는 회의/경찰 쪽 최종 판정의 책임이므로 여기서는 건드리지 않는다
    /// (§7 Meeting이 담당할 영역) — 이 조정기는 "누구를 공식적으로 지목했는지"만 기록한다.
    /// </summary>
    public static class ReportSubmissionCoordinator
    {
        public static ReportSubmissionResult Submit(
            ContractInstance contract,
            ContractReportState report,
            ClueRepository repository,
            IContractReportValidator validator)
        {
            var validationResult = validator.Validate(contract, report, repository);
            if (!validationResult.CanSubmit)
                return new ReportSubmissionResult { Outcome = ReportSubmissionOutcome.Rejected, ValidationResult = validationResult };

            report.MarkSubmitted();

            var perpetratorEntry = report.GetSection(ReportSection.Perpetrator);
            if (!string.IsNullOrEmpty(perpetratorEntry.ClaimedNpcId))
                contract.OfficialConclusion.OfficiallyAccusedNpcId = perpetratorEntry.ClaimedNpcId;

            return new ReportSubmissionResult { Outcome = ReportSubmissionOutcome.Submitted, ValidationResult = validationResult };
        }
    }
}
