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
            OrganizationConditionType.DisguiseAsSuicide,
            OrganizationConditionType.ConcealCulpritClues,
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

        private static readonly CrimeType[] CrimeOrder =
            { CrimeType.Murder, CrimeType.Missing, CrimeType.AssaultInjury, CrimeType.TheftRobbery, CrimeType.Blackmail };

        /// <summary>04 2026-11-12 5×5 상대 가중치(행=대표, 열=실제: 살인/실종/폭행/절도/협박). 같은 유형 8, 조건부 1, 불가 0.</summary>
        private static readonly int[,] ActualTypeWeights =
        {
            { 8, 1, 1, 0, 1 },
            { 1, 8, 1, 0, 1 },
            { 1, 1, 8, 0, 0 },
            { 1, 1, 1, 8, 0 },
            { 1, 1, 1, 0, 8 },
        };

        /// <summary>
        /// 조건부 조합 중 지금 생성기가 원문의 필수 성립 조건을 지킬 수 있는 것만 허용한다("상대 가중치가 1이라는 이유만으로 자동 허용하지 않는다").
        /// - 살인→실종: 경찰은 혈흔 등으로 사망을 의심했지만 시신·사망 확인 없음. 실제 대상은 살아 있고 소재 미확인.
        /// - 살인→폭행·상해: 치명적 공격으로 오인했지만 피해자는 살아 있고 비치명적 상해.
        /// - 절도→폭행·상해: 물품 부재·어질러진 현장으로 절도로 분류, 경찰은 상해를 아직 모름.
        /// - 협박→폭행·상해 / 협박→실종: 협박을 먼저 접수했고 실제 부상·소재 미확인은 인계 시점에 알려지지 않음.
        /// - 실종·절도·협박→살인: 시신 은닉으로 인계 시점까지 사망·시신 미확인(<see cref="RequiresHiddenBody"/>).
        /// - 폭행→실종, 실종→폭행, 실종→협박, 살인→협박: 핵심 사건 뒤 피해자가 스스로 떠나·숨어 소재 미확인(<see cref="RequiresVictimDeparture"/>).
        /// - 폭행→살인: 공격 뒤 다른 방에서 늦게 숨짐, 인계 시점에 사망 미확인(<see cref="RequiresDelayedDeath"/>).
        /// - 절도→실종: 가해자의 유인·강제 이동으로 소유자가 실종되고 물건은 소유자와 함께 사라짐(타인의 실제 절도 없음).
        /// </summary>
        public static bool IsSupportedCombination(CrimeType representative, CrimeType actual) =>
            representative == actual
            || (representative == CrimeType.Murder && (actual == CrimeType.Missing || actual == CrimeType.AssaultInjury || actual == CrimeType.Blackmail))
            || (representative == CrimeType.Missing && (actual == CrimeType.Murder || actual == CrimeType.AssaultInjury || actual == CrimeType.Blackmail))
            || (representative == CrimeType.AssaultInjury && (actual == CrimeType.Missing || actual == CrimeType.Murder))
            || (representative == CrimeType.TheftRobbery && (actual == CrimeType.Murder || actual == CrimeType.AssaultInjury || actual == CrimeType.Missing))
            || (representative == CrimeType.Blackmail && (actual == CrimeType.Murder || actual == CrimeType.AssaultInjury || actual == CrimeType.Missing));

        /// <summary>
        /// 조합별 필수 성립 조건에 필요한 경위(04 §5):
        /// - 피해자 자발적 이탈: 폭행→실종(폭행 뒤 스스로 떠남), 실종→폭행(상해 뒤 소재 미확인), 실종→협박·살인→협박(협박을 피해 스스로 은신).
        /// - 시신 은닉: 실종→살인·절도→살인·협박→살인(인계 시점까지 사망·시신 미확인).
        /// </summary>
        public static bool RequiresVictimDeparture(CrimeType representative, CrimeType actual) =>
            (representative == CrimeType.AssaultInjury && actual == CrimeType.Missing)
            || (representative == CrimeType.Missing && (actual == CrimeType.AssaultInjury || actual == CrimeType.Blackmail))
            || (representative == CrimeType.Murder && actual == CrimeType.Blackmail);

        public static bool RequiresHiddenBody(CrimeType representative, CrimeType actual) =>
            actual == CrimeType.Murder && representative != CrimeType.Murder && representative != CrimeType.AssaultInjury;

        /// <summary>폭행→살인: 경찰 분류 당시 상해만 확인, 피해자는 이후 다른 방에서 숨졌고 인계 시점에 사망이 확인되지 않았다.</summary>
        public static bool RequiresDelayedDeath(CrimeType representative, CrimeType actual) =>
            representative == CrimeType.AssaultInjury && actual == CrimeType.Murder;

        public CrimeType PickActualCrimeType(CrimeType representative)
        {
            int row = Array.IndexOf(CrimeOrder, representative);
            var pool = new List<(CrimeType Type, int Weight)>();
            for (int col = 0; col < CrimeOrder.Length; col++)
            {
                int w = ActualTypeWeights[row, col];
                if (w > 0 && IsSupportedCombination(representative, CrimeOrder[col]))
                    pool.Add((CrimeOrder[col], w));
            }
            int roll = _random.Next(pool.Sum(p => p.Weight));
            foreach (var (type, weight) in pool)
            {
                if (roll < weight) return type;
                roll -= weight;
            }
            return representative;
        }

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

            // 04 2026-11-12 최종 순서: 대표 유형(경찰 분류)을 확정 가중치로 고른 뒤, 5×5 상대 가중치로 실제 유형을 고른다.
            var representative = PickPrimaryCrimeType();
            instance.RepresentativeCrimeType = representative;
            instance.PrimaryCrimeType = PickActualCrimeType(representative);

            // 사용자 지시(2026-11-09): 의뢰 사건 공간에서 실외를 제외했으므로 장소는 항상 실내(주택)만 배정한다.
            instance.LocationCategory = LocationCategory.Indoor;
            instance.Location = StubLocation.House;
            instance.StartTimeBlock = _config.DefaultStartTimeBlock;

            instance.Truth.CoverUp = PickCoverUp(instance.PrimaryCrimeType);
            instance.Truth.VictimDeparted = RequiresVictimDeparture(representative, instance.PrimaryCrimeType);
            instance.Truth.BodyHidden = RequiresHiddenBody(representative, instance.PrimaryCrimeType);
            instance.Truth.DelayedDeath = RequiresDelayedDeath(representative, instance.PrimaryCrimeType);
            if (instance.Truth.BodyHidden) instance.Truth.CoverUp = CoverUpMethod.BodyConcealment;
            else if (instance.Truth.VictimDeparted) instance.Truth.CoverUp = CoverUpMethod.None;

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
            // 05: 사망 사건이고 자살로 유도할 경로(숨진 피해자)가 있을 때만.
            OrganizationConditionType.DisguiseAsSuicide => instance.PrimaryCrimeType == CrimeType.Murder,
            // 05: 서로 다른 범인 특정 단서 2개 이상이 있어야 한다 — 자료 생성 뒤 대상 지정·검증에서 확인한다.
            OrganizationConditionType.ConcealCulpritClues => true,
            OrganizationConditionType.KeepUnsolved => true,
            _ => false
        };
    }
}
