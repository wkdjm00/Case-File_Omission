using System;
using System.Collections.Generic;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 의뢰 사건 하나의 런타임 데이터. 실제 진실/외부 상태/공식 결론/조직 조건을
    /// 서로 다른 필드로 분리해 관리한다 (04페이지 "데이터 기록 구조" 확정 원칙).
    /// </summary>
    [Serializable]
    public sealed class ContractInstance
    {
        public string ContractId { get; }

        public CrimeType PrimaryCrimeType;
        public LocationCategory LocationCategory;

        /// <summary>최종 장소 목록 확정 전까지 사용하는 스텁 장소.</summary>
        public StubLocation Location;

        public readonly List<CrimeType> SubCrimes = new List<CrimeType>();

        public ContractTruth Truth = new ContractTruth();
        public ContractExternalState ExternalState = new ContractExternalState();
        public ContractOfficialConclusion OfficialConclusion = new ContractOfficialConclusion();
        public OrganizationCondition OrgCondition = new OrganizationCondition();

        public ContractInstance(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            ContractId = contractId;
        }
    }
}
