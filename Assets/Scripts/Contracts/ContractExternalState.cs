using System;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 외부에 알려진 사건 상태. 신고 내용, 발견 당시 상황, 초기 설명 등을 담으며
    /// 실제 진실과 일치할 수도, 다를 수도 있다 (04페이지 확정 원칙).
    /// </summary>
    [Serializable]
    public sealed class ContractExternalState
    {
        public string ReportedSummary;
        public string InitialExplanation;
        public bool DiffersFromTruth;
        public string DifferenceNote;
    }
}
