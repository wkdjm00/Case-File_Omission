using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 의뢰 인물(가해자·피해자·목격자) 구성. 실제 진실에서 정한 인물 ID를 그대로 쓴다.
    /// 단서는 여기서 만들지도, 지급하지도 않는다 — 단서는 시간표에서 <see cref="EvidenceMaterialGenerator"/>가 만들고,
    /// 플레이어가 실제로 조사하거나 대화할 때만 저장소에 등록된다(04 확정 "현장에 없는 단서를 자동 지급하지 않는다").
    /// 인물의 위치도 여기서 정하지 않는다 — 시간표 적용(<see cref="ContractTimelineApplier"/>)이 조사 시작 시각의 일정대로 배치한다.
    /// 인원 구성(3명)은 임시값이다.
    /// </summary>
    public sealed class ContractContentGenerator
    {
        private readonly Random _random;

        public ContractContentGenerator(Random random = null)
        {
            _random = random ?? new Random();
        }

        public void CreateCast(ContractInstance contract, NpcRegistry npcRegistry)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (npcRegistry == null) throw new ArgumentNullException(nameof(npcRegistry));

            string victimId = contract.Truth.VictimId ?? ContractGenerator.VictimIdFor(contract.ContractId);
            string perpetratorId = contract.Truth.PerpetratorId ?? ContractGenerator.PerpetratorIdFor(contract.ContractId);
            string witnessId = ContractGenerator.WitnessIdFor(contract.ContractId);

            contract.Truth.VictimId = victimId;
            contract.Truth.PerpetratorId = perpetratorId;
            // 진실 세부(방법·도구·동기·관계·세부 종류·날짜)는 후보마다 한 번만 정하고 검증 재시도 중에는 바꾸지 않는다.
            if (contract.Truth.MethodId == null)
                CaseDetailGenerator.AssignTruthDetails(contract, _random);

            var cast = new List<NpcState>
            {
                new NpcState(victimId, contract.ContractId, NpcActualRole.Victim, PickPersonality()),
                new NpcState(perpetratorId, contract.ContractId, NpcActualRole.Perpetrator, PickPersonality()),
                new NpcState(witnessId, contract.ContractId, NpcActualRole.Witness, PickPersonality())
            };

            // 04 2026-11-12: 사건 참여 NPC 2~6명. 사건 관계자는 관계·동기·정보 접근을 설명할 때만 둔다(인원 채우기 금지) —
            // 피해자와 관계(REL)가 있는 인물로만 만들고, 그 관계를 진술·문서 자료의 근거로 쓴다. 0~2명(TEST_TEMP 범위).
            int related = _random.Next(3);
            if (related >= 1) cast.Add(new NpcState(RelatedIdFor(contract.ContractId, 'a'), contract.ContractId, NpcActualRole.CaseRelatedA, PickPersonality()));
            if (related >= 2) cast.Add(new NpcState(RelatedIdFor(contract.ContractId, 'b'), contract.ContractId, NpcActualRole.CaseRelatedB, PickPersonality()));

            var names = CaseCatalog.TempNames.OrderBy(_ => _random.Next()).ToList();
            for (int i = 0; i < cast.Count; i++)
            {
                cast[i].DisplayName = names[i % names.Count];
                cast[i].Occupation = CaseCatalog.Occupations[_random.Next(CaseCatalog.Occupations.Length)];
                npcRegistry.Add(cast[i]);
            }
        }

        public static string RelatedIdFor(string contractId, char key) => $"{contractId}-related-{key}";

        private NpcPersonality PickPersonality()
        {
            var values = (NpcPersonality[])Enum.GetValues(typeof(NpcPersonality));
            return values[_random.Next(values.Length)];
        }
    }

    /// <summary>한 의뢰의 생성 결과 묶음. 검증을 통과한 것만 만들어진다.</summary>
    public sealed class GeneratedContract
    {
        public ContractInstance Contract;
        public NpcRegistry Npcs;
        public RoomGraph Rooms;
        public RoomOccupancyTracker Occupancy;
        public ContractTimeline Timeline;
        public readonly List<EvidenceMaterial> Materials = new List<EvidenceMaterial>();

        public EvidenceMaterial GetMaterial(string materialId) => Materials.FirstOrDefault(m => m.MaterialId == materialId);

        /// <summary>그 방을 조사해 얻을 수 있는 자료.</summary>
        public IEnumerable<EvidenceMaterial> MaterialsInRoom(string roomId) =>
            Materials.Where(m => m.Acquisition == MaterialAcquisition.InvestigateRoom && m.RoomId == roomId);

        /// <summary>그 NPC와 대화해 얻을 수 있는 자료.</summary>
        public IEnumerable<EvidenceMaterial> MaterialsFromNpc(string npcId) =>
            Materials.Where(m => m.Acquisition == MaterialAcquisition.TalkToNpc && m.SourceNpcId == npcId);
    }

    /// <summary>
    /// 04 2026-11-06 통합 설계 §1 생성 파이프라인 전체를 잇는다.
    /// 1) 실제 진실 → 2) 유형·외부 상태 → 3) 부수 범죄 → 4) 조직 조건 → 5) 인물·공간·시간표·지식 → 6) 단서·증언 → 7) 검증 → 8) 재생성.
    /// 재시도·폐기는 <see cref="ContractRetryPolicy"/>(후보당 3회, 폐기 후 새 후보)와 시간표·자료 생성기(요소당 5회)가 맡는다.
    /// 부수 범죄는 추가 비율이 원문에 없어 자동으로 붙이지 않는다(붙일 때는 <see cref="ContractInstance.AddSubCrime"/>로 연결 근거와 함께).
    /// </summary>
    public sealed class ContractPipeline
    {
        private readonly ContractGenerationConfig _config;
        private readonly Random _random;
        private readonly GameLogger _logger;
        private readonly ContractGenerator _generator;

        public ContractPipeline(ContractGenerationConfig config, Random random = null, GameLogger logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random = random ?? new Random();
            _logger = logger;
            _generator = new ContractGenerator(_config, _random);
        }

        /// <summary>실패하면 <see cref="ContractGenerationFailedException"/>을 던진다.</summary>
        public GeneratedContract Generate(
            Func<string> contractIdFactory,
            OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            var generated = TryGenerate(contractIdFactory, out var result, previousConditionType);
            if (generated == null)
                throw new ContractGenerationFailedException(
                    $"후보 {result.CandidatesUsed}개, 검증 {result.ValidationAttempts}회 시도 후 생성 실패: {string.Join(", ", result.LastErrors)}");
            return generated;
        }

        /// <summary>
        /// 후보 최대 10개 × 후보당 검증 최대 3회. 모두 실패하면 예외 없이 null을 돌려주고 result에 실패 내용을 담는다.
        /// </summary>
        public GeneratedContract TryGenerate(
            Func<string> contractIdFactory,
            out ContractGenerationResult result,
            OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            GeneratedContract built = null;
            var policy = new ContractRetryPolicy(_generator, new ContractValidator(),
                _config.MaxAttemptsPerCandidate, _config.MaxCandidates, _logger);

            result = policy.TryGenerate(contractIdFactory, candidate =>
            {
                var validation = BuildAndValidate(candidate, out var attempt);
                built = validation.IsValid ? attempt : null;
                return validation;
            }, previousConditionType);

            if (!result.Success)
                return null;

            built.Contract = result.Contract;
            return built;
        }

        private ContractValidationResult BuildAndValidate(ContractInstance candidate, out GeneratedContract built)
        {
            var result = new ContractValidationResult();
            void Merge(IEnumerable<ContractValidationErrorCode> codes)
            {
                foreach (var code in codes)
                    if (!result.Errors.Contains(code)) result.Errors.Add(code);
            }

            built = new GeneratedContract
            {
                Contract = candidate,
                Npcs = new NpcRegistry(),
                Rooms = HouseLayoutFactory.CreateStandardHouse()
            };
            built.Occupancy = new RoomOccupancyTracker(built.Rooms);

            // 이전 시도의 사건 ID 기록을 지운다(진실의 인물·관계·동기는 그대로).
            candidate.Truth.TimelineEvents.Clear();

            new ContractContentGenerator(_random).CreateCast(candidate, built.Npcs);

            var timelineResult = new ContractTimelineGenerator(_random,
                new TimelineGenerationConfig { MaxRetriesPerElement = _config.MaxRetriesPerElement })
                .Generate(candidate, built.Npcs, built.Rooms);
            if (!timelineResult.Success)
            {
                Merge(timelineResult.Errors);
                return result;
            }
            built.Timeline = timelineResult.Timeline;

            // 3단계 — 부수 범죄(구현 명세 4.7). 이전 시도의 부수 범죄를 지우고 다시 판정한다. 실패해도 "부수 범죄 없음"으로 진행한다.
            candidate.SubCrimes.Clear();
            candidate.SubCrimeLinks.Clear();
            SubCrimeGenerator.TryAdd(built, _generator, _random);

            var materialResult = new EvidenceMaterialGenerator(_random, _config.MaxRetriesPerElement)
                .Generate(built.Timeline, built.Npcs, built.Rooms);
            Merge(materialResult.Errors);
            built.Materials.AddRange(materialResult.Materials);

            AddBodyMaterialIfNeeded(built);
            CaseDetailGenerator.ApplySubtypeToTimeline(built);
            CaseDetailGenerator.Enrich(built, _random);
            BindOrganizationTarget(built);
            FillExternalState(built);

            ContractTimelineApplier.Apply(built.Timeline, candidate, built.Npcs, built.Occupancy);
            CaseDetailGenerator.PlaceBody(built);
            CaseDetailGenerator.BuildPoliceSnapshot(built);

            var timeline = built.Timeline;
            var assignments = timeline.Schedules.ToDictionary(
                p => p.Key, p => p.Value.GetScheduledRoom(timeline.CoreIncident.StartMinute) ?? timeline.SceneRoomId);

            Merge(new ContractValidator().Validate(candidate).Errors);
            Merge(new ContractValidator().ValidateSpatial(candidate, built.Rooms, assignments).Errors);
            Merge(new EvidenceMaterialValidator().Validate(built.Materials, timeline, built.Npcs, built.Rooms).Errors);
            Merge(new ContractTimelineValidator().ValidateNpcKnowledge(timeline, built.Npcs).Errors);
            Merge(ValidateOrganizationTarget(built));

            return result;
        }

        public static string BodyMaterialId(string contractId) => $"{contractId}-mat-body";

        /// <summary>살인 사건이면 현장에 남은 시신을 조사 자료로 둔다(시신 회수 조건의 회수 대상).</summary>
        private static void AddBodyMaterialIfNeeded(GeneratedContract built)
        {
            var core = built.Timeline.CoreIncident;
            if (built.Contract.PrimaryCrimeType != CrimeType.Murder || core == null)
                return;

            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = BodyMaterialId(built.Contract.ContractId),
                ContractId = built.Contract.ContractId,
                Format = ClueFormat.PhysicalObject,
                RoomId = built.Timeline.SceneRoomId,
                Acquisition = MaterialAcquisition.InvestigateRoom,
                AboutEventId = core.EventId,
                AboutMinute = core.StartMinute,
                AboutRoomId = core.RoomId,
                StatedInfoIds = { core.InfoId }
            });
        }

        /// <summary>
        /// 05 확정: 회수 대상을 특정하는 조건이면 의뢰를 받을 때 대상 정보를 공개한다. 대상은 생성된 자료·인물 중에서 고른다.
        /// - 시신 회수: 현장의 시신 자료
        /// - 특정 물건 회수: 현장 흔적(범행 도구) 자료
        /// - 공식 범인 지정: 대상 선정 규칙이 원문에 없어, 살아 있는 비가해자 중 하나를 임시로 지정한다.
        /// 회수만/조직 전달 구분의 판정 세부는 05에서 미정이라 회수만 요구하는 것으로 둔다.
        /// </summary>
        private void BindOrganizationTarget(GeneratedContract built)
        {
            var condition = built.Contract.OrgCondition;
            switch (condition.Type)
            {
                case OrganizationConditionType.RecoverBody:
                    condition.TargetId = built.GetMaterial(BodyMaterialId(built.Contract.ContractId))?.MaterialId;
                    break;
                case OrganizationConditionType.RecoverItem:
                    condition.TargetId = built.Materials
                        .FirstOrDefault(m => m.Acquisition == MaterialAcquisition.InvestigateRoom
                                             && m.Format == ClueFormat.PhysicalObject
                                             && m.MaterialId != BodyMaterialId(built.Contract.ContractId))?.MaterialId;
                    break;
                case OrganizationConditionType.DesignateOfficialCulprit:
                {
                    var candidates = built.Npcs.GetByContract(built.Contract.ContractId)
                        .Where(n => n.NpcId != built.Contract.Truth.PerpetratorId
                                    && EvidenceMaterialGenerator.IsAliveAfterIncident(built.Timeline, built.Npcs, n.NpcId))
                        .OrderBy(n => n.NpcId, StringComparer.Ordinal)
                        .ToList();
                    condition.TargetId = candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)].NpcId;
                    break;
                }
            }
            condition.RequiresDeliveryToOrganization = false;
        }

        private static IEnumerable<ContractValidationErrorCode> ValidateOrganizationTarget(GeneratedContract built)
        {
            var condition = built.Contract.OrgCondition;
            bool valid = condition.Type switch
            {
                OrganizationConditionType.RecoverBody or OrganizationConditionType.RecoverItem =>
                    condition.TargetId != null && built.GetMaterial(condition.TargetId) != null,
                OrganizationConditionType.DesignateOfficialCulprit =>
                    condition.TargetId != null && built.Npcs.Exists(condition.TargetId),
                _ => true
            };
            if (!valid)
                yield return ContractValidationErrorCode.CONTRACT_INVALID;
        }

        /// <summary>
        /// 04 확정: 외부에 알려진 상태(신고 내용·초기 설명·진실과의 차이)는 실제 진실과 따로 둔다. 문장은 표시용 임시 문구다.
        /// </summary>
        private static void FillExternalState(GeneratedContract built)
        {
            var external = built.Contract.ExternalState;
            var discovery = built.Timeline.Discovery;
            string reporter = discovery?.ParticipantIds.FirstOrDefault();
            string reporterName = reporter != null && built.Npcs.Exists(reporter) ? built.Npcs.Get(reporter).DisplayName ?? reporter : "주민";
            external.ReportedSummary = $"{reporterName}의 신고(임시 문구)";

            var coverUp = built.Contract.Truth.CoverUp;
            external.DiffersFromTruth = coverUp != CoverUpMethod.None;
            external.InitialExplanation = coverUp switch
            {
                CoverUpMethod.SuicideDisguise => "자살로 보이는 상황(임시 문구)",
                CoverUpMethod.AccidentalDeathDisguise => "사고사로 보이는 상황(임시 문구)",
                CoverUpMethod.DisappearanceDisguise => "단순 가출로 보이는 상황(임시 문구)",
                CoverUpMethod.RecordOrTestimonyTampering => "기록·증언이 실제와 다른 상황(임시 문구)",
                _ => "신고 내용 그대로(임시 문구)"
            };
            external.DifferenceNote = external.DiffersFromTruth ? coverUp.ToString() : null;
        }
    }
}
