using System.Collections.Generic;
using CaseFileOmission.Contracts;

namespace CaseFileOmission.Reports
{
    /// <summary>
    /// 04페이지 표는 "사건 유형상 행위자 특정이 필요한 경우" 등 조건부 필수 항목의 판정 기준을
    /// 아직 구체적인 사건 유형별 매핑 데이터로 확정하지 않았다(2026-11-09 상태 갱신 표: "사건 유형별
    /// 필수 항목 매핑 데이터를 작성하고 UI 구현"이 남은 작업으로 명시됨).
    /// 그 매핑 표가 나오기 전까지, 이 리졸버는 "사건 유형"이 아니라 이미 생성된 사건의 실제 데이터
    /// (가해자 지정 여부, 동기 존재 여부, 도구 존재 여부, 부수 범죄 존재 여부)로 필요 여부를 판단한다.
    /// 이는 명시적인 가정이며, 실제 매핑 표가 확정되면 이 리졸버를 교체해야 한다.
    /// </summary>
    public static class ReportRequirementResolver
    {
        public static IReadOnlyCollection<ReportSection> GetRequiredSections(ContractInstance contract)
        {
            var required = new HashSet<ReportSection>
            {
                // 04페이지 확정: 사건 개요는 모든 의뢰에서 요구.
                ReportSection.CaseSummary,
                // 표에 조건이 없어 모든 의뢰에 적용되는 항목으로 취급한다(주요 인물/사건 경과/피해 및 결과).
                ReportSection.KeyPersons,
                ReportSection.CaseProgress,
                ReportSection.DamageAndResult,
            };

            if (!string.IsNullOrEmpty(contract.Truth.PerpetratorId))
                required.Add(ReportSection.Perpetrator);

            if (!string.IsNullOrEmpty(contract.Truth.PrimaryMotive))
                required.Add(ReportSection.Motive);

            if (!string.IsNullOrEmpty(contract.Truth.Tool))
                required.Add(ReportSection.MeansAndTool);

            // 04페이지 확정: 부수 범죄가 생성된 의뢰에서만 요구.
            if (contract.SubCrimes.Count > 0)
                required.Add(ReportSection.SubCrime);

            return required;
        }
    }
}
