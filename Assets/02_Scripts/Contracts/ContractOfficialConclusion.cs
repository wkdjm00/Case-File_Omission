using System;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 경찰 회의에서 공식적으로 인정된 결론. 실제 진실·외부 상태와 별도 데이터로 관리하며,
    /// 플레이어의 조작 결과에 따라 실제 가해자와 다른 인물이 공식적으로 인정될 수 있다.
    /// </summary>
    [Serializable]
    public sealed class ContractOfficialConclusion
    {
        public bool IsFinalized;
        public string OfficiallyAccusedNpcId;
        public ContractOutcome Outcome = ContractOutcome.Pending;
    }
}
