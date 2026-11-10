using System;
using System.Collections.Generic;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Reports
{
    public sealed class ReportValidationIssue
    {
        public ReportSection Section;
        public string Detail;
    }

    public sealed class ReportValidationResult
    {
        public bool CanSubmit => Issues.Count == 0;
        public readonly List<ReportValidationIssue> Issues = new List<ReportValidationIssue>();
    }

    public interface IContractReportValidator
    {
        ReportValidationResult Validate(ContractInstance contract, ContractReportState report, ClueRepository repository);
    }

    /// <summary>
    /// 04페이지 2026-11-09 구현 명세가 확정한 두 조건만 구현한다:
    /// "필수 항목이 비어 있거나 필수 항목에 근거 단서가 연결되지 않으면 제출할 수 없다."
    /// "제출 전에 실제 사건의 진실과 모순되는 보고 내용이 있는지 검증한다."의 세부 의미 규칙
    /// (예: 거짓 결론 제출 자체를 막을지, 인용된 단서와 주장의 논리적 정합성만 볼지)은
    /// 아직 확정되지 않았으므로 여기서는 임의로 만들지 않고 보류한다.
    /// </summary>
    public sealed class ContractReportValidator : IContractReportValidator
    {
        public ReportValidationResult Validate(ContractInstance contract, ContractReportState report, ClueRepository repository)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            var result = new ReportValidationResult();
            var requiredSections = ReportRequirementResolver.GetRequiredSections(contract);

            // 04 2026-11-09 확정: 보고서는 항목별 단서 연결 방식이다. 자유 서술(Content)은 기본 방식이 아니므로 필수 조건에 넣지 않는다.
            // 항목이 "비어 있다"는 것은 연결된 단서가 없다는 뜻이다. 행위자 항목은 누구를 지목하는지도 있어야 한다.
            foreach (var section in requiredSections)
            {
                var entry = report.GetSection(section);

                if (entry.LinkedClueIds.Count == 0)
                {
                    result.Issues.Add(new ReportValidationIssue { Section = section, Detail = "근거 단서가 연결되어 있지 않습니다." });
                    continue;
                }

                if (section == ReportSection.Perpetrator && string.IsNullOrEmpty(entry.ClaimedNpcId))
                    result.Issues.Add(new ReportValidationIssue { Section = section, Detail = "지목한 인물이 없습니다." });
            }

            // 필수 여부와 무관하게, 연결한 단서는 플레이어가 실제로 확보한 이 의뢰의 단서여야 한다.
            foreach (ReportSection section in Enum.GetValues(typeof(ReportSection)))
            {
                foreach (var clueId in report.GetSection(section).LinkedClueIds)
                {
                    if (!repository.Exists(clueId))
                    {
                        result.Issues.Add(new ReportValidationIssue
                        {
                            Section = section,
                            Detail = $"단서 '{clueId}'를 찾을 수 없습니다."
                        });
                    }
                    else if (repository.GetDefinition(clueId).ContractId != contract.ContractId)
                    {
                        result.Issues.Add(new ReportValidationIssue
                        {
                            Section = section,
                            Detail = $"단서 '{clueId}'는 이 의뢰의 단서가 아닙니다."
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// §9-2 "증언과 증거의 모순 검사"의 1차 구현. 거짓·조작으로 판정된(TruthStatus가
        /// False 또는 Fabricated인) 단서가 반박 근거 없이 그대로 인용되어 있으면 식별한다.
        /// 이 검사는 제출 자체를 막지 않는다 — 플레이어가 의도적으로 거짓 자료를 제시해
        /// 수사를 교란하는 것은 확정된 핵심 플레이이므로(06페이지), Validate()의 CanSubmit에는
        /// 포함하지 않고 "식별 가능하다"는 완료 기준만 충족한다.
        /// </summary>
        public IReadOnlyList<ReportValidationIssue> CheckForUnrebuttedFalseEvidence(ContractReportState report, ClueRepository repository)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            var issues = new List<ReportValidationIssue>();

            foreach (ReportSection section in Enum.GetValues(typeof(ReportSection)))
            {
                foreach (var clueId in report.GetSection(section).LinkedClueIds)
                {
                    if (!repository.Exists(clueId))
                        continue; // 존재하지 않는 단서는 위 Validate()가 이미 보고한다.

                    var definition = repository.GetDefinition(clueId);
                    if (definition.TruthStatus == ClueTruthStatus.False || definition.TruthStatus == ClueTruthStatus.Fabricated)
                    {
                        issues.Add(new ReportValidationIssue
                        {
                            Section = section,
                            Detail = $"단서 '{clueId}'는 거짓/조작 상태({definition.TruthStatus})로 표시되어 있습니다."
                        });
                    }
                }
            }

            return issues;
        }
    }
}
