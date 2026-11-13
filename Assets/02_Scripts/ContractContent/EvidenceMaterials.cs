using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    public enum MaterialAcquisition
    {
        /// <summary>출처 NPC와 대화해 얻는다(증언).</summary>
        TalkToNpc,

        /// <summary>방을 조사해 얻는다(현장 흔적·문서·물건).</summary>
        InvestigateRoom,

        /// <summary>NPC끼리의 대화를 엿들어 얻는다(조사 중 실제로 일어난 정보 전달).</summary>
        Overheard
    }

    /// <summary>
    /// 04 "단서·문서·증언 데이터" 공통 필드를 구조로만 담는다. 플레이어에게 보이는 문장(표시 내용)은 생성하지 않는다 —
    /// 원본은 "AI가 확정된 진실과 데이터 조건에 맞춰 문서·증언을 생성"하도록 했고 문장 템플릿은 정하지 않았다.
    /// </summary>
    public sealed class EvidenceMaterial
    {
        public string MaterialId;
        public string ContractId;
        public ClueFormat Format;
        public ClueTruthStatus TruthStatus = ClueTruthStatus.Factual;

        /// <summary>증언이면 진술한 NPC. 현장 자료면 null.</summary>
        public string SourceNpcId;

        /// <summary>조사로 얻는 자료가 놓인 방. 대화로 얻는 자료면 null(NPC가 있는 곳에서 얻는다).</summary>
        public string RoomId;

        public MaterialAcquisition Acquisition;

        /// <summary>자료가 다루는 실제 사건(시간순 경위의 사건 ID)과 그 시각·장소.</summary>
        public string AboutEventId;
        public int AboutMinute;
        public string AboutRoomId;

        /// <summary>자료가 주장하는 정보 ID. 사실 자료는 실제 정보, 거짓 자료는 실제에 없는 주장 ID.</summary>
        public readonly List<string> StatedInfoIds = new List<string>();

        /// <summary>거짓 알리바이처럼 특정 인물에 관한 주장일 때 그 인물.</summary>
        public string SubjectNpcId;

        /// <summary>이 자료를 반박·검증할 수 있는 다른 자료 ID(04 "반박 또는 검증에 사용할 수 있는 근거").</summary>
        public readonly List<string> RebuttedByMaterialIds = new List<string>();

        /// <summary>실제 진실과 다른 부분이 생긴 원인(04 확정 "거짓 증언은 생성 원인이 설명 가능해야 한다").</summary>
        public string FalsehoodReason;

        public bool IsFalseOrFabricated => TruthStatus == ClueTruthStatus.False || TruthStatus == ClueTruthStatus.Fabricated;

        /// <summary>언어적 자료(증언) 여부. 나머지 형태(문서·녹음·물건)는 물리적 자료로 센다(해석).</summary>
        public bool IsVerbal => Format == ClueFormat.Testimony;

        public bool IdentifiesPerpetrator;

        /// <summary>
        /// 조사로 얻는 자료가 놓인 조사 오브젝트(04 2026-11-11 방별 조사 오브젝트 후보). null이면 방 자체에서 얻는다.
        /// 조사 오브젝트 ID는 "{room_id}:{오브젝트 이름}" 형식이다.
        /// </summary>
        public string ObjectId;

        /// <summary>이 자료를 얻으려면 먼저 보유해야 하는 자료(예: 진술을 들은 뒤에야 보관 중인 서신을 확인). null이면 조건 없음.</summary>
        public string RequiresClueId;

        /// <summary>표시 제목·본문(TEST_TEMP 템플릿 문장 — AI 생성 연동 전 결정적 생성). 판정에는 쓰지 않는다.</summary>
        public string Title;
        public string Content;

        /// <summary>
        /// 기존 단서 저장소에 넣을 정의로 바꾼다. 발견 처리(Discover)는 하지 않는다 — 플레이어가 실제로 얻을 때만 등록한다
        /// (04 확정 "보고서 정답만으로 현장에 없는 단서를 자동 지급하지 않는다").
        /// </summary>
        public ClueDefinition ToClueDefinition() => new ClueDefinition(MaterialId, ContractId)
        {
            Format = Format,
            TruthStatus = TruthStatus,
            RelatedTruthNote = AboutEventId,
            SourceDescription = SourceNpcId ?? RoomId,
            AcquisitionCondition = Acquisition == MaterialAcquisition.TalkToNpc ? $"talk:{SourceNpcId}" : $"investigate:{ObjectId ?? RoomId}",
            IdentifiesPerpetrator = IdentifiesPerpetrator,
            DisplayName = Title ?? MaterialId,
            DisplayedContent = Content
        };
    }

    /// <summary>04 2026-11-06 확정: 거짓·조작 자료는 물리적 최대 3개, 언어적 최대 3개, 합계 최대 6개, 0개 허용.</summary>
    public static class FalseMaterialLimits
    {
        public const int MaxPhysical = 3;
        public const int MaxVerbal = 3;
        public const int MaxTotal = 6;
    }

    public sealed class MaterialGenerationResult
    {
        public readonly List<EvidenceMaterial> Materials = new List<EvidenceMaterial>();
        public readonly List<ContractValidationErrorCode> Errors = new List<ContractValidationErrorCode>();

        /// <summary>반박 근거를 만들 수 없어 거짓 자료를 넣지 않은 경우 등, 생성 중 내린 결정의 기록.</summary>
        public readonly List<string> Notes = new List<string>();

        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// 검증된 시간표에서 조사 자료를 만든다. 모든 자료는 시간표의 사건·지식 근거를 가진다.
    /// - 현장 흔적: 핵심 사건 현장 방을 조사해 얻는 물리적 자료(사실).
    /// - 목격 증언: 핵심 사건을 직접 목격한 생존 인물의 증언(사실). 가해자를 본 증언이므로 가해자 특정 자료다.
    /// - 거짓 알리바이: 생존한 실제 가해자가 핵심 사건 시각에 다른 방에 있었다고 말하는 거짓 증언(선택, 0개 허용).
    ///   그 방에 실제로 있던 인물의 위치 증언이나 목격 증언으로 반박할 수 있을 때만 만든다. 반박할 수 없으면 넣지 않는다.
    /// 가해자 외 인물의 거짓 증언은 07 2026-11-10 확정 조건(은폐 방식이 '기록·증언 조작'이고 그 NPC가 은폐에 관여)에서만
    /// 최대 1개 만든다. 현재 시간표의 은폐 사건 참여자는 가해자뿐이라 그 조건을 만족하는 NPC가 생기지 않는다.
    /// 조작 문서는 생성 조건이 원본에서 미정이라 만들지 않는다.
    /// </summary>
    public sealed class EvidenceMaterialGenerator
    {
        private readonly Random _random;
        private readonly int _maxRetriesPerElement;

        /// <summary>null이면 가해자 성격에 따른 확정 확률(<see cref="FalseAlibiChance"/>)로 정한다. 테스트에서 고정할 때만 쓴다.</summary>
        public bool? IncludePerpetratorFalseAlibi;

        /// <summary>
        /// 07 2026-11-10 확정: 가해자 거짓 알리바이 생성 확률 — 거짓말을 자주 하는 70%, 비밀을 감추는 60%,
        /// 의심이 많은·욕심이 많은 50%, 그 외 40%. 조건이 겹치면 가장 높은 값 하나만 쓴다(성격은 하나라 겹치지 않는다).
        /// </summary>
        public static double FalseAlibiChance(NpcPersonality personality) => personality switch
        {
            NpcPersonality.FrequentLiar => 0.70,
            NpcPersonality.Secretive => 0.60,
            NpcPersonality.Suspicious => 0.50,
            NpcPersonality.Greedy => 0.50,
            _ => 0.40
        };

        public EvidenceMaterialGenerator(Random random = null, int maxRetriesPerElement = 5)
        {
            _random = random ?? new Random();
            _maxRetriesPerElement = maxRetriesPerElement;
        }

        public MaterialGenerationResult Generate(ContractTimeline timeline, NpcRegistry registry, RoomGraph rooms)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));

            var result = new MaterialGenerationResult();
            var core = timeline.CoreIncident;
            if (core == null)
            {
                result.Errors.Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                return result;
            }

            int index = 0;
            string NextId(string kind) => $"{timeline.ContractId}-mat-{++index}-{kind}";

            result.Materials.Add(new EvidenceMaterial
            {
                MaterialId = NextId("scene-trace"),
                ContractId = timeline.ContractId,
                Format = ClueFormat.PhysicalObject,
                RoomId = core.RoomId,
                Acquisition = MaterialAcquisition.InvestigateRoom,
                AboutEventId = core.EventId,
                AboutMinute = core.StartMinute,
                AboutRoomId = core.RoomId,
                StatedInfoIds = { core.InfoId }
            });

            var witnessTestimonies = new List<EvidenceMaterial>();
            foreach (var grant in timeline.Knowledge
                         .Where(k => k.SourceEventId == core.EventId && k.Path == InfoAcquisitionPath.DirectWitness)
                         .OrderBy(k => k.NpcId, StringComparer.Ordinal))
            {
                if (!IsAliveAfterIncident(timeline, registry, grant.NpcId))
                    continue;

                var testimony = new EvidenceMaterial
                {
                    MaterialId = NextId("witness-testimony"),
                    ContractId = timeline.ContractId,
                    Format = ClueFormat.Testimony,
                    SourceNpcId = grant.NpcId,
                    Acquisition = MaterialAcquisition.TalkToNpc,
                    AboutEventId = core.EventId,
                    AboutMinute = core.StartMinute,
                    AboutRoomId = core.RoomId,
                    IdentifiesPerpetrator = true,
                    StatedInfoIds = { core.InfoId }
                };
                witnessTestimonies.Add(testimony);
                result.Materials.Add(testimony);
            }

            bool includeAlibi = IncludePerpetratorFalseAlibi
                ?? (registry.Exists(timeline.PerpetratorId)
                    && _random.NextDouble() < FalseAlibiChance(registry.Get(timeline.PerpetratorId).Personality));
            if (includeAlibi && IsAliveAfterIncident(timeline, registry, timeline.PerpetratorId))
                TryAddFalseAlibi(timeline, registry, rooms, core, witnessTestimonies, result, NextId);

            return result;
        }

        private void TryAddFalseAlibi(
            ContractTimeline timeline,
            NpcRegistry registry,
            RoomGraph rooms,
            TimelineEvent core,
            List<EvidenceMaterial> witnessTestimonies,
            MaterialGenerationResult result,
            Func<string, string> nextId)
        {
            int slot = timeline.SlotOf(core.StartMinute);
            var claimable = rooms.AllRoomIds.Where(r => r != core.RoomId).OrderBy(r => r, StringComparer.Ordinal).ToList();
            if (claimable.Count == 0)
            {
                result.Notes.Add("거짓 알리바이 생략: 주장할 다른 방이 없음");
                return;
            }

            for (int attempt = 0; attempt <= _maxRetriesPerElement; attempt++)
            {
                string claimedRoom = claimable[_random.Next(claimable.Count)];

                // 그 시각 그 방에 실제로 있던 생존 인물은 가해자가 없었다는 것을 안다.
                var occupants = timeline.GetPresentAt(claimedRoom, core.StartMinute)
                    .Where(id => id != timeline.PerpetratorId && IsAliveAfterIncident(timeline, registry, id))
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();

                if (occupants.Count == 0 && witnessTestimonies.Count == 0)
                    continue;

                var presenceTestimonies = occupants.Select(id =>
                {
                    var m = new EvidenceMaterial
                    {
                        MaterialId = nextId("presence-testimony"),
                        ContractId = timeline.ContractId,
                        Format = ClueFormat.Testimony,
                        SourceNpcId = id,
                        Acquisition = MaterialAcquisition.TalkToNpc,
                        AboutMinute = core.StartMinute,
                        AboutRoomId = claimedRoom
                    };
                    m.StatedInfoIds.AddRange(timeline.GetPresentAt(claimedRoom, core.StartMinute)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .Select(x => ContractTimeline.PresenceInfoId(timeline.ContractId, x, claimedRoom, slot)));
                    return m;
                }).ToList();

                var alibi = new EvidenceMaterial
                {
                    MaterialId = nextId("false-alibi"),
                    ContractId = timeline.ContractId,
                    Format = ClueFormat.Testimony,
                    TruthStatus = ClueTruthStatus.False,
                    SourceNpcId = timeline.PerpetratorId,
                    SubjectNpcId = timeline.PerpetratorId,
                    Acquisition = MaterialAcquisition.TalkToNpc,
                    AboutEventId = core.EventId,
                    AboutMinute = core.StartMinute,
                    AboutRoomId = claimedRoom,
                    FalsehoodReason = "실제 가해자가 핵심 사건 시각의 자기 위치를 숨김",
                    StatedInfoIds = { ContractTimeline.PresenceInfoId(timeline.ContractId, timeline.PerpetratorId, claimedRoom, slot) }
                };
                alibi.RebuttedByMaterialIds.AddRange(presenceTestimonies.Select(m => m.MaterialId));
                alibi.RebuttedByMaterialIds.AddRange(witnessTestimonies.Select(m => m.MaterialId));

                result.Materials.AddRange(presenceTestimonies);
                result.Materials.Add(alibi);
                return;
            }

            result.Notes.Add("거짓 알리바이 생략: 반박 근거가 되는 인물이 없음");
        }

        internal static bool IsAliveAfterIncident(ContractTimeline timeline, NpcRegistry registry, string npcId)
        {
            if (!registry.Exists(npcId) || !registry.Get(npcId).IsAlive)
                return false;
            return !(npcId == timeline.VictimId && timeline.VictimLeavesStoryAfterIncident);
        }

        /// <summary>
        /// 회의 카드 판단용 모순 색인으로 옮긴다. 거짓 자료마다 반박 자료 ID와 반박 자료가 담은 정보 ID를 '명확한 모순'으로
        /// 등록한다 — 판정자가 그 자료를 보유했거나 그 정보를 실제로 알 때만 모순으로 센다(06 확정).
        /// </summary>
        public static CardContradictionIndex BuildContradictionIndex(IEnumerable<EvidenceMaterial> materials, CardContradictionIndex index = null)
        {
            index ??= new CardContradictionIndex();
            var list = materials.ToList();
            var byId = list.ToDictionary(m => m.MaterialId);

            foreach (var falseMaterial in list.Where(m => m.IsFalseOrFabricated))
            {
                foreach (var rebuttalId in falseMaterial.RebuttedByMaterialIds)
                {
                    index.AddRebuttal(falseMaterial.MaterialId, rebuttalId, ContradictionLevel.Clear);
                    if (byId.TryGetValue(rebuttalId, out var rebuttal))
                    {
                        foreach (var info in rebuttal.StatedInfoIds)
                            index.AddRebuttal(falseMaterial.MaterialId, info, ContradictionLevel.Clear);
                    }
                }
            }
            return index;
        }
    }

    /// <summary>
    /// 04 "생성 검증 필수 항목" 중 조사 자료 관련: 목격 정보와 NPC 지식의 획득 경로, 거짓 자료를 반박할 근거,
    /// 단서 획득 조건과 공간 접근 가능성, 시간·장소 일관성, 거짓 자료 수량 제한.
    /// </summary>
    public sealed class EvidenceMaterialValidator
    {
        public ContractValidationResult Validate(
            IReadOnlyList<EvidenceMaterial> materials,
            ContractTimeline timeline,
            NpcRegistry registry,
            RoomGraph rooms)
        {
            var result = new ContractValidationResult();
            void Add(ContractValidationErrorCode code)
            {
                if (!result.Errors.Contains(code)) result.Errors.Add(code);
            }

            var byId = new Dictionary<string, EvidenceMaterial>();
            foreach (var m in materials)
            {
                if (string.IsNullOrEmpty(m.MaterialId) || byId.ContainsKey(m.MaterialId) || m.ContractId != timeline.ContractId)
                    Add(ContractValidationErrorCode.CONTRACT_INVALID);
                else
                    byId[m.MaterialId] = m;
            }

            int falsePhysical = materials.Count(m => m.IsFalseOrFabricated && !m.IsVerbal);
            int falseVerbal = materials.Count(m => m.IsFalseOrFabricated && m.IsVerbal);
            if (falsePhysical > FalseMaterialLimits.MaxPhysical || falseVerbal > FalseMaterialLimits.MaxVerbal
                || falsePhysical + falseVerbal > FalseMaterialLimits.MaxTotal)
                Add(ContractValidationErrorCode.CONTRACT_INVALID);

            var events = timeline.Events.ToDictionary(e => e.EventId);

            foreach (var m in materials)
            {
                // 획득 가능성: 대화는 생존한 출처 NPC, 조사는 존재하는 방.
                if (m.Acquisition == MaterialAcquisition.TalkToNpc)
                {
                    if (string.IsNullOrEmpty(m.SourceNpcId) || !EvidenceMaterialGenerator.IsAliveAfterIncident(timeline, registry, m.SourceNpcId))
                        Add(ContractValidationErrorCode.CLUE_UNREACHABLE);
                }
                else if (string.IsNullOrEmpty(m.RoomId) || !rooms.RoomExists(m.RoomId))
                {
                    Add(ContractValidationErrorCode.CLUE_UNREACHABLE);
                }

                // 시간·장소: 사건에 관한 사실 자료는 그 사건의 시각·장소와 일치해야 한다.
                if (m.AboutEventId != null)
                {
                    if (!events.TryGetValue(m.AboutEventId, out var e))
                        Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                    else if (!m.IsFalseOrFabricated && (e.StartMinute != m.AboutMinute || e.RoomId != m.AboutRoomId))
                        Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                }

                // 지식 경로: 사실 증언은 출처 NPC가 실제로 아는 정보만 말한다.
                if (!m.IsFalseOrFabricated && m.SourceNpcId != null
                    && m.StatedInfoIds.Any(info => !timeline.HasGrant(m.SourceNpcId, info)))
                    Add(ContractValidationErrorCode.KNOWLEDGE_LEAK);

                if (m.IsFalseOrFabricated)
                {
                    if (string.IsNullOrEmpty(m.FalsehoodReason))
                        Add(ContractValidationErrorCode.CONTRADICTION);
                    if (!HasValidRebuttal(m, byId, timeline))
                        Add(ContractValidationErrorCode.CONTRADICTION);
                }
            }

            return result;
        }

        /// <summary>
        /// 반박 자료는 사실 자료여야 하고, 실제로 주장과 어긋나야 한다:
        /// 주장한 방에 그 시각 실제로 있던 인물의 증언(주장 인물이 그 방에 없었음) 또는
        /// 주장 인물이 실제로 있던 방·시각을 직접 본 자료.
        /// </summary>
        private static bool HasValidRebuttal(EvidenceMaterial m, Dictionary<string, EvidenceMaterial> byId, ContractTimeline timeline)
        {
            foreach (var id in m.RebuttedByMaterialIds)
            {
                if (!byId.TryGetValue(id, out var r) || r.IsFalseOrFabricated)
                    continue;

                if (m.SubjectNpcId == null)
                    return true;

                string actualRoom = timeline.GetRoomAt(m.SubjectNpcId, m.AboutMinute);
                if (actualRoom == m.AboutRoomId)
                    continue; // 주장이 사실과 같다면 반박 대상이 아니다.

                // 인물 위치에 관한 주장은 그 자리에 있던 사람의 진술만 반박할 수 있다. 현장 흔적은 누가 있었는지 보여 주지 않는다.
                if (r.SourceNpcId == null)
                    continue;

                string rebuttalSourceRoom = timeline.GetRoomAt(r.SourceNpcId, m.AboutMinute);
                bool sawClaimedRoomWithoutSubject = rebuttalSourceRoom == m.AboutRoomId;
                var aboutEvent = r.AboutEventId == null ? null : timeline.Events.FirstOrDefault(e => e.EventId == r.AboutEventId);
                bool sawSubjectElsewhere = rebuttalSourceRoom == actualRoom
                    && aboutEvent != null && aboutEvent.ParticipantIds.Contains(m.SubjectNpcId);
                if (sawClaimedRoomWithoutSubject || sawSubjectElsewhere)
                    return true;
            }
            return false;
        }
    }
}
