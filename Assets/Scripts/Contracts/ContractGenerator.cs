using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 확정된 규칙 안에서 의뢰 후보 데이터를 만든다. 검증은 <see cref="ContractValidator"/>가 담당한다
    /// (04페이지 "AI·게임 시스템·검증기 책임 분리" 원칙).
    /// </summary>
    public sealed class ContractGenerator
    {
        /// <summary>
        /// 05페이지는 조건 부여 확률(40%)만 확정하고, 개별 조건 간 선택 비중은 아직 명시하지 않았다.
        /// 전체 조건 목록·가중치가 확정되기 전까지 예시 4종 중 균등 확률로 임시 배정한다.
        /// </summary>
        private static readonly OrganizationConditionType[] ExampleConditionTypes =
        {
            OrganizationConditionType.RecoverBody,
            OrganizationConditionType.RecoverItem,
            OrganizationConditionType.DesignateOfficialCulprit,
            OrganizationConditionType.KeepUnsolved,
        };

        private readonly ContractGenerationConfig _config;
        private readonly Random _random;

        public ContractGenerator(ContractGenerationConfig config, Random random = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random = random ?? new Random();
        }

        public CrimeType PickPrimaryCrimeType(IEnumerable<CrimeType> excluded = null) =>
            PickWeighted(_config.PrimaryCrimeWeights, excluded);

        public CrimeType PickSubCrimeType(IEnumerable<CrimeType> excluded = null) =>
            PickWeighted(_config.SubCrimeWeights, excluded);

        public bool RollOrganizationCondition() => _random.NextDouble() < _config.OrganizationConditionChance;

        private CrimeType PickWeighted(List<WeightedEntry<CrimeType>> entries, IEnumerable<CrimeType> excluded)
        {
            var excludedSet = excluded != null ? new HashSet<CrimeType>(excluded) : null;
            var pool = entries.Where(e => excludedSet == null || !excludedSet.Contains(e.Value)).ToList();

            if (pool.Count == 0)
                throw new InvalidOperationException("선택 가능한 사건 유형 후보가 없습니다.");

            float total = pool.Sum(e => e.Weight);
            double roll = _random.NextDouble() * total;
            double cumulative = 0;

            foreach (var entry in pool)
            {
                cumulative += entry.Weight;
                if (roll <= cumulative)
                    return entry.Value;
            }

            return pool[pool.Count - 1].Value; // 부동소수 누적 오차 대비 폴백
        }

        /// <summary>
        /// 대표 사건 유형과 (40% 확률로) 조직 조건을 배정한 후보를 생성한다.
        /// 사용자 지시(2026-11-09)로 의뢰 사건 공간에서 실외를 제외했으므로, 장소는 항상
        /// 실내(주택)만 배정한다. 공간·NPC·단서 배치는 여전히 포함하지 않는다.
        /// </summary>
        public ContractInstance CreateCandidate(string contractId)
        {
            var instance = new ContractInstance(contractId)
            {
                PrimaryCrimeType = PickPrimaryCrimeType()
            };

            instance.LocationCategory = LocationCategory.Indoor;
            instance.Location = StubLocation.House;

            if (RollOrganizationCondition())
            {
                int index = _random.Next(ExampleConditionTypes.Length);
                instance.OrgCondition.Type = ExampleConditionTypes[index];

                // 11. 조직 조건과 사건 성립 가능성이 충돌하면 안 된다 (예: 실종 사건에 시신 회수 조건).
                // 조건 후보 전체 목록·개별 적용 가능 범위가 확정되기 전까지는 명백히 불가능한 조합만 배제한다.
                if (instance.OrgCondition.Type == OrganizationConditionType.RecoverBody &&
                    instance.PrimaryCrimeType == CrimeType.Missing)
                {
                    instance.OrgCondition.Type = OrganizationConditionType.None;
                }
            }

            // 7. 시간순서 검증을 통과하려면 최소 1건의 타임라인 기록이 필요하다.
            // 실제 사건 경위 생성은 2단계(단서 저장소·보고서)에서 구현하며, 지금은 스텁 1건만 넣는다.
            instance.Truth.TimelineEvents.Add("사건 발생");

            return instance;
        }
    }
}
