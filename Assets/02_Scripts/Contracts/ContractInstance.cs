using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 부수 범죄 하나가 대표 사건과 어떻게 연결되는지(04 확정: 인과관계나 인물 관계로 연결된 후보만,
    /// 시점·장소·인물·연결 이유를 경위에 반영하고 검증한다).
    /// </summary>
    [Serializable]
    public sealed class SubCrimeLink
    {
        public CrimeType Type;
        public string LinkReason;
        public readonly List<string> LinkedNpcIds = new List<string>();
    }

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

        /// <summary>사건이 시작되는 조사 구간. 의뢰 데이터에서 지정한다(06 2026-11-10: 모두 아침으로 고정하지 않는다).</summary>
        public TimeBlock StartTimeBlock = TimeBlock.Morning;

        /// <summary>이 의뢰를 맡은 담당 경찰 ID(경찰 5명 중 1명, 07 확정). 의뢰 수락 시 배정한다.</summary>
        public string HandlerPoliceId;

        public readonly List<CrimeType> SubCrimes = new List<CrimeType>();

        /// <summary><see cref="SubCrimes"/>의 각 항목이 대표 사건과 연결되는 근거.</summary>
        public readonly List<SubCrimeLink> SubCrimeLinks = new List<SubCrimeLink>();

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

        /// <summary>부수 범죄를 연결 근거와 함께 추가한다.</summary>
        public void AddSubCrime(CrimeType type, string linkReason, params string[] linkedNpcIds)
        {
            SubCrimes.Add(type);
            var link = new SubCrimeLink { Type = type, LinkReason = linkReason };
            if (linkedNpcIds != null)
                link.LinkedNpcIds.AddRange(linkedNpcIds);
            SubCrimeLinks.Add(link);
        }
    }
}
