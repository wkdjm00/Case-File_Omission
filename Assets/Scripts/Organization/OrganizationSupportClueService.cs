using System.Collections.Generic;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Organization
{
    /// <summary>
    /// 05페이지 2026-11-09 구현 명세가 확정한 두 지원 단서(조직_01, 조직_02)만 구현한다.
    /// 신뢰도 구간별로 실제 제공할 개별 단서의 종류와 세부 습득 조건은 05페이지 자체가
    /// "미정"으로 남긴 범위라 여기서 임의로 만들지 않는다.
    /// </summary>
    public sealed class OrganizationSupportClueService
    {
        public const string Org01ClueId = "org_01";
        public const string Org02ClueId = "org_02";

        private readonly HashSet<string> _grantedClueIds = new HashSet<string>();

        /// <summary>조직_01: 자살 위장 조건이 포함된 의뢰와 함께(의뢰 시작 시점) 지급.</summary>
        public bool TryGrantOrg01(ContractInstance contract)
        {
            if (_grantedClueIds.Contains(Org01ClueId))
                return false;

            if (contract.Truth.CoverUp != CoverUpMethod.SuicideDisguise)
                return false;

            _grantedClueIds.Add(Org01ClueId);
            return true;
        }

        /// <summary>조직_02: 가해자를 특정할 단서 2개를 은폐한 채 의뢰를 완료하면 종료 시점에 지급.</summary>
        public bool TryGrantOrg02(ClueRepository repository, IReadOnlyList<ManipulationRecord> manipulationHistory)
        {
            if (_grantedClueIds.Contains(Org02ClueId))
                return false;

            int concealedPerpetratorIdentifyingClues = 0;
            foreach (var record in manipulationHistory)
            {
                if (record.Type != ManipulationType.Conceal)
                    continue;

                if (!repository.Exists(record.OriginalClueId))
                    continue;

                if (repository.GetDefinition(record.OriginalClueId).IdentifiesPerpetrator)
                    concealedPerpetratorIdentifyingClues++;
            }

            if (concealedPerpetratorIdentifyingClues < 2)
                return false;

            _grantedClueIds.Add(Org02ClueId);
            return true;
        }

        public bool HasGranted(string supportClueId) => _grantedClueIds.Contains(supportClueId);
    }
}
