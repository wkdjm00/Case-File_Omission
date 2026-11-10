using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Organization
{
    /// <summary>
    /// 05페이지 2026-11-09 구현 명세가 확정한 두 지원 단서(조직_01, 조직_02).
    /// - 신뢰도 구간을 적용한다: 1–20은 지원 단서를 주지 않는다(0은 게임 오버라 판정 대상이 아니다).
    /// - 의뢰 시작 전 자료(조직_01)는 의뢰 시작 시점에, 종료 후 자료(조직_02)는 종료 시점에 검사하며 같은 단서는 중복 지급하지 않는다.
    /// - 21–39의 "조건 1개 추가", 70 이상의 "조건 완화"는 조정 가능한 조건에만 적용하고 필수 전제는 없애지 않는다(05 확정).
    ///   조직_01(자살 위장 조건 의뢰)과 조직_02(가해자 특정 단서 2개 은폐)의 조건은 필수 전제라 구간 조정을 적용하지 않는다(해석).
    /// 신뢰도 구간별 개별 단서의 종류는 05 자체가 미정이라 만들지 않는다.
    /// </summary>
    public sealed class OrganizationSupportClueService
    {
        public const string Org01ClueId = "org_01";
        public const string Org02ClueId = "org_02";
        public const int RequiredConcealedPerpetratorClues = 2;

        private readonly HashSet<string> _grantedClueIds = new HashSet<string>();

        /// <summary>조직_01: 자살 위장 조건이 포함된 의뢰와 함께(의뢰 시작 시점) 지급.</summary>
        public bool TryGrantOrg01(ContractInstance contract, float organizationTrust)
        {
            if (_grantedClueIds.Contains(Org01ClueId) || !TierAllowsSupport(organizationTrust))
                return false;

            if (contract.Truth.CoverUp != CoverUpMethod.SuicideDisguise)
                return false;

            _grantedClueIds.Add(Org01ClueId);
            return true;
        }

        /// <summary>
        /// 조직_02: 가해자를 특정할 단서 2개를 은폐한 채 의뢰를 끝내면 종료 시점에 지급한다.
        /// 같은 단서를 여러 번 은폐한 것은 한 개로 센다.
        /// </summary>
        public bool TryGrantOrg02(ClueRepository repository, IReadOnlyList<ManipulationRecord> manipulationHistory, float organizationTrust)
        {
            if (_grantedClueIds.Contains(Org02ClueId) || !TierAllowsSupport(organizationTrust))
                return false;

            int concealed = manipulationHistory
                .Where(r => r.Type == ManipulationType.Conceal && repository.Exists(r.OriginalClueId)
                            && repository.GetDefinition(r.OriginalClueId).IdentifiesPerpetrator)
                .Select(r => r.OriginalClueId)
                .Distinct()
                .Count();

            if (concealed < RequiredConcealedPerpetratorClues)
                return false;

            _grantedClueIds.Add(Org02ClueId);
            return true;
        }

        public static bool TierAllowsSupport(float organizationTrust) =>
            organizationTrust > 0f && OrganizationSupportTierResolver.Resolve(organizationTrust) != SupportTier.NoSupport;

        public bool HasGranted(string supportClueId) => _grantedClueIds.Contains(supportClueId);

        /// <summary>저장/불러오기 전용.</summary>
        public IReadOnlyCollection<string> GrantedClueIds => _grantedClueIds;

        /// <summary>저장/불러오기 전용. 지급 판정을 다시 하지 않고 "이미 지급함"만 되살린다.</summary>
        public void RestoreGranted(string supportClueId)
        {
            if (!string.IsNullOrEmpty(supportClueId))
                _grantedClueIds.Add(supportClueId);
        }
    }
}
