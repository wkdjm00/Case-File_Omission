using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 확정된 규칙 안에서 의뢰 후보 데이터를 만든다. 검증은 <see cref="ContractValidator"/>가 담당한다
    /// (04페이지 "AI·게임 시스템·검증기 책임 분리" 원칙).
    ///
    /// 생성 순서(04 2026-11-06 통합 설계 §1): 1) 실제 진실(가해자·피해자·동기·도구·과정·인물 관계) 확정 →
    /// 2) 대표 사건 유형 → 3) 부수 범죄 → 4) 조직 조건 40% 판정. 인물·공간·시간표·단서는 ContractContent 파이프라인이 이어서 만든다.
    /// </summary>
    public sealed class ContractGenerator
    {
        /// <summary>
        /// 05페이지는 조건 부여 확률(40%)만 확정하고, 개별 조건 간 선택 비중은 아직 명시하지 않았다.
        /// 전체 조건 목록·가중치가 확정되기 전까지 예시 4종 중 실행 가능한 조건을 균등 확률로 임시 배정한다.
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

        public static string PerpetratorIdFor(string contractId) => $"{contractId}-perpetrator";
        public static string VictimIdFor(string contractId) => $"{contractId}-victim";
        public static string WitnessIdFor(string contractId) => $"{contractId}-witness-1";

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
        /// 실제 진실을 먼저 고정한다. 인물 ID와 인물 관계를 정하고, 동기·방법·도구는 선택지 목록이 기획에서 미정이라
        /// 임시 값을 둔다. 재시도 중에도 이 진실은 바꾸지 않는다(04 2026-11-09 확정).
        /// </summary>
        public ContractTruth CreateTruth(string contractId)
        {
            return new ContractTruth
            {
                PerpetratorId = PerpetratorIdFor(contractId),
                VictimId = VictimIdFor(contractId),
                RelationshipType = "관계(임시)",
                PrimaryMotive = "원한(임시)",
                Method = "둔기로 가격(임시)",
                Tool = "둔기(임시)"
            };
        }

        /// <summary>
        /// 진실을 고정한 의뢰 후보를 만든다. previousConditionType은 직전 의뢰의 조직 조건 — 대안이 있으면 같은 조건이
        /// 바로 반복되지 않게 한다(05 확정).
        /// </summary>
        public ContractInstance CreateCandidate(string contractId, OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            var instance = new ContractInstance(contractId) { Truth = CreateTruth(contractId) };
            AssignElements(instance, previousConditionType);
            return instance;
        }

        /// <summary>
        /// 후보의 사건 핵심 설정(유형·장소·은폐·조직 조건)을 정한다. 후보를 만들 때 한 번만 호출한다 —
        /// 같은 후보의 검증 재시도에서는 다시 호출하지 않는다(사용자 확정 2026-11-10: 재시도 중 핵심 설정 불변).
        /// </summary>
        public void AssignElements(ContractInstance instance, OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            instance.PrimaryCrimeType = PickPrimaryCrimeType();

            // 사용자 지시(2026-11-09): 의뢰 사건 공간에서 실외를 제외했으므로 장소는 항상 실내(주택)만 배정한다.
            instance.LocationCategory = LocationCategory.Indoor;
            instance.Location = StubLocation.House;
            instance.StartTimeBlock = _config.DefaultStartTimeBlock;

            instance.Truth.CoverUp = PickCoverUp(instance.PrimaryCrimeType);

            instance.OrgCondition = new OrganizationCondition();
            if (RollOrganizationCondition())
                instance.OrgCondition.Type = PickFeasibleCondition(instance, previousConditionType);

            // 사건 경위는 ContractContent의 시간표 생성기가 사건 ID로 채운다. 기본 검증기만 쓰는 경로를 위해 표시용 기록을 둔다.
            if (instance.Truth.TimelineEvents.Count == 0)
                instance.Truth.TimelineEvents.Add("사건 발생");
        }

        /// <summary>
        /// 은폐 방식은 별도 항목이며 "은폐 없음"도 허용한다(04 확정). 포함 비율·방식별 비중은 원문에 없어
        /// <see cref="ContractGenerationConfig.CoverUpChance"/>(임시)로 정하고, 사건 유형에 적용 가능한 방식만 고른다.
        /// </summary>
        public CoverUpMethod PickCoverUp(CrimeType crime)
        {
            if (_random.NextDouble() >= _config.CoverUpChance)
                return CoverUpMethod.None;

            var applicable = GetApplicableCoverUps(crime);
            return applicable.Count == 0 ? CoverUpMethod.None : applicable[_random.Next(applicable.Count)];
        }

        public static IReadOnlyList<CoverUpMethod> GetApplicableCoverUps(CrimeType crime)
        {
            var list = new List<CoverUpMethod> { CoverUpMethod.RecordOrTestimonyTampering };
            if (crime == CrimeType.Murder)
            {
                list.Add(CoverUpMethod.AccidentalDeathDisguise);
                list.Add(CoverUpMethod.SuicideDisguise);
            }
            if (crime == CrimeType.Missing)
                list.Add(CoverUpMethod.DisappearanceDisguise);
            return list;
        }

        /// <summary>
        /// 05 확정: 양립할 수 없는 조건은 고르지 않는다. 대안이 있으면 직전 의뢰와 같은 조건이 바로 반복되지 않게 하고,
        /// 대안이 없으면 반복을 허용한다. 실행 가능한 조건이 하나도 없을 때의 처리는 원문에 없어 조건 없음으로 둔다.
        /// </summary>
        public OrganizationConditionType PickFeasibleCondition(ContractInstance instance, OrganizationConditionType previousConditionType)
        {
            var feasible = ExampleConditionTypes.Where(t => IsConditionFeasible(t, instance)).ToList();
            if (feasible.Count == 0)
                return OrganizationConditionType.None;

            var withoutRepeat = feasible.Where(t => t != previousConditionType).ToList();
            var pool = withoutRepeat.Count > 0 ? withoutRepeat : feasible;
            return pool[_random.Next(pool.Count)];
        }

        /// <summary>조건이 사건과 양립하는지. 회수 대상의 실제 존재 여부는 단서 생성 뒤 검증기가 다시 확인한다.</summary>
        public static bool IsConditionFeasible(OrganizationConditionType type, ContractInstance instance) => type switch
        {
            // 시신이 남는 사건은 살인뿐이다(실종·폭행·절도·협박에는 시신이 없다).
            OrganizationConditionType.RecoverBody => instance.PrimaryCrimeType == CrimeType.Murder,
            // 회수할 물건(범행 도구)이 있어야 한다.
            OrganizationConditionType.RecoverItem => !string.IsNullOrEmpty(instance.Truth?.Tool),
            OrganizationConditionType.DesignateOfficialCulprit => true,
            OrganizationConditionType.KeepUnsolved => true,
            _ => false
        };
    }
}
