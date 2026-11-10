using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 04 "실제 사건 경위"의 단계. 모든 사건에 강제하지 않고 필요한 단계만 쓴다(확정).
    /// 서사가 필요한 단계(사건 이전 상황, 외부 설명 형성 과정)는 생성기가 만들지 않는다 — 원본에 생성 규칙이 없다.
    /// </summary>
    public enum TimelineEventKind
    {
        PriorSituation,
        PositionOrMeeting,
        CoreIncident,
        Aftermath,
        DiscoveryOrReport,
        CoverUp,
        ExternalExplanation
    }

    /// <summary>04 "정보 데이터 — 정보가 획득된 경로: 직접 목격, 조사, 대화, 다른 NPC에게 전달받음".</summary>
    public enum InfoAcquisitionPath
    {
        OwnAction,
        DirectWitness,
        Investigation,
        Conversation,
        TransferredFromNpc
    }

    public sealed class TimelineEvent
    {
        public string EventId;
        public TimelineEventKind Kind;
        public int StartMinute;
        public int EndMinute;
        public string RoomId;

        /// <summary>이 사건이 실제로 일어났다는 정보 ID. NPC 지식·단서·카드 반박이 같은 ID 공간을 쓴다.</summary>
        public string InfoId;

        public readonly List<string> ParticipantIds = new List<string>();
        public readonly List<string> CauseEventIds = new List<string>();
    }

    /// <summary>NPC가 어떤 정보를 언제, 어떤 경로로 알게 되었는지. 근거 없는 지식(KNOWLEDGE_LEAK)을 검사하는 기준이다.</summary>
    public sealed class KnowledgeGrant
    {
        public string NpcId;
        public string InfoId;
        public InfoAcquisitionPath Path;
        public int Minute;
        public string SourceEventId;
    }

    /// <summary>
    /// 생성 설정. 재시도 횟수는 04 2026-11-09 확정값(요소 5회)이다.
    /// 06 2026-11-10 확정: 사건 경위 시간표는 조사 시간 칸(아침·저녁 12칸)과 별개이며, 사건·목격·이동마다 발생 시각을
    /// 직접 기록하고 정밀도는 10분 단위가 기본이다. SlotMinutes는 그 정밀도이고, SlotCount는 경위가 차지하는 길이(임시값)다.
    /// </summary>
    public sealed class TimelineGenerationConfig
    {
        /// <summary>확정: 개별 생성 요소 최대 5회 재시도.</summary>
        public int MaxRetriesPerElement = 5;

        /// <summary>확정(06 2026-11-10): 기본 시각 정밀도 10분. 한 단위 동안 NPC는 머물거나 인접한 방 하나로 이동한다.</summary>
        public int SlotMinutes = 10;

        /// <summary>임시값(확정 아님). 사건 경위가 차지하는 10분 단위 개수.</summary>
        public int SlotCount = 12;
    }

    /// <summary>
    /// 구조화된 실제 사건 경위. ContractTruth.TimelineEvents(문자열 목록)는 그대로 두고 사건 ID만 채운다.
    /// 일정(NpcSchedule)은 "사건 당시 누가 어디에 있었는가"이며 목격 가능 여부의 근거가 된다.
    /// </summary>
    public sealed class ContractTimeline
    {
        public string ContractId;
        public CrimeType CrimeType;
        public int SlotMinutes;
        public int SlotCount;
        public string SceneRoomId;
        public string PerpetratorId;
        public string VictimId;

        /// <summary>사건 직후 피해자가 현장에서 직접 행동하지 않게 되는지(살인 → 사망, 실종 → 부재).</summary>
        public bool VictimLeavesStoryAfterIncident;

        public readonly List<TimelineEvent> Events = new List<TimelineEvent>();
        public readonly Dictionary<string, NpcSchedule> Schedules = new Dictionary<string, NpcSchedule>();
        public readonly List<KnowledgeGrant> Knowledge = new List<KnowledgeGrant>();

        public int DayEndMinute => SlotCount * SlotMinutes;

        public TimelineEvent CoreIncident => Events.FirstOrDefault(e => e.Kind == TimelineEventKind.CoreIncident);

        public TimelineEvent Discovery => Events.FirstOrDefault(e => e.Kind == TimelineEventKind.DiscoveryOrReport);

        /// <summary>조사 시작 시점 — 발견·신고가 끝난 시각.</summary>
        public int InvestigationStartMinute => Discovery?.EndMinute ?? DayEndMinute;

        public string GetRoomAt(string npcId, int minute) =>
            Schedules.TryGetValue(npcId, out var s) ? s.GetScheduledRoom(minute) : null;

        public IEnumerable<string> GetPresentAt(string roomId, int minute) =>
            Schedules.Values.Where(s => s.GetScheduledRoom(minute) == roomId).Select(s => s.NpcId);

        public bool HasGrant(string npcId, string infoId) => Knowledge.Any(k => k.NpcId == npcId && k.InfoId == infoId);

        public static string PresenceInfoId(string contractId, string npcId, string roomId, int slot) =>
            $"{contractId}-presence-{npcId}-{roomId}-{slot}";

        public int SlotOf(int minute) => minute / SlotMinutes;
    }

    public sealed class TimelineGenerationResult
    {
        public ContractTimeline Timeline;
        public int AttemptsUsed;
        public readonly List<ContractValidationErrorCode> Errors = new List<ContractValidationErrorCode>();
        public bool Success => Timeline != null && Errors.Count == 0;
    }

    /// <summary>
    /// 04 "실제 진실을 먼저 고정 → 시간순 경위 → 검증" 중 시간순 경위와 인물 일정을 만든다. 실제 진실(가해자·피해자·
    /// 범죄 유형·은폐 여부)은 입력으로 받기만 하고 재시도 중에도 바꾸지 않는다(확정).
    ///
    /// 생성 규칙:
    /// - 핵심 사건: 가해자는 현장 방에 있어야 한다. 살인·폭행·상해는 피해자도 같은 방에 있어야 한다(물리적으로 필요).
    ///   그 밖의 유형은 피해자와의 직접 만남을 강제하지 않는다(04 확정 "모든 절도 사건에 직접 만남을 강제하지 않는다").
    /// - 목격자 역할 NPC는 실제로 목격해야 하므로 핵심 사건 시각에 현장 방에 둔다(07 "목격 여부는 실제로 목격했는지").
    /// - 은폐가 있으면 가해자가 사건 직후 현장에서 은폐 행위를 한다.
    /// - 발견: 사건 이후 현장에 들어온 생존 인물(가해자 제외)이 발견·신고한다.
    /// - 일정은 한 칸마다 머물거나 인접한 방으로만 이동한다(시간상 가능한 위치만).
    /// - 살인 피해자는 사망, 실종 피해자는 부재 — 사건 이후 일정이 없다.
    /// 실패하면 경위 요소만 다시 만든다(최대 5회 재시도). 끝까지 실패하면 결과에 오류 코드를 남긴다.
    /// </summary>
    public sealed class ContractTimelineGenerator
    {
        private readonly Random _random;
        private readonly TimelineGenerationConfig _config;
        private readonly ContractTimelineValidator _validator = new ContractTimelineValidator();

        public ContractTimelineGenerator(Random random = null, TimelineGenerationConfig config = null)
        {
            _random = random ?? new Random();
            _config = config ?? new TimelineGenerationConfig();
            if (_config.SlotCount < 6) throw new ArgumentException("SlotCount는 6 이상이어야 합니다.", nameof(config));
            if (_config.SlotMinutes <= 0) throw new ArgumentException("SlotMinutes는 1 이상이어야 합니다.", nameof(config));
        }

        public TimelineGenerationResult Generate(ContractInstance contract, NpcRegistry registry, RoomGraph rooms)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));

            var result = new TimelineGenerationResult();
            string perpId = contract.Truth?.PerpetratorId;
            string victimId = contract.Truth?.VictimId;
            if (string.IsNullOrEmpty(perpId) || string.IsNullOrEmpty(victimId) || perpId == victimId
                || !registry.Exists(perpId) || !registry.Exists(victimId) || rooms.AllRoomIds.Count == 0)
            {
                result.Errors.Add(ContractValidationErrorCode.CONTRACT_INVALID);
                return result;
            }

            int maxAttempts = 1 + Math.Max(0, _config.MaxRetriesPerElement);
            List<ContractValidationErrorCode> lastErrors = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result.AttemptsUsed = attempt;
                var timeline = TryBuild(contract, registry, rooms, out var buildError);
                if (timeline == null)
                {
                    lastErrors = new List<ContractValidationErrorCode> { buildError };
                    continue;
                }

                var validation = _validator.Validate(timeline, contract, registry, rooms);
                if (validation.IsValid)
                {
                    result.Timeline = timeline;
                    return result;
                }
                lastErrors = validation.Errors;
            }

            result.Errors.AddRange(lastErrors.Distinct());
            return result;
        }

        private ContractTimeline TryBuild(ContractInstance contract, NpcRegistry registry, RoomGraph rooms, out ContractValidationErrorCode error)
        {
            error = ContractValidationErrorCode.TIMELINE_CONFLICT;
            int slots = _config.SlotCount;
            var roomIds = rooms.AllRoomIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
            var distance = BuildDistances(rooms, roomIds);

            var cast = registry.GetByContract(contract.ContractId).OrderBy(n => n.NpcId, StringComparer.Ordinal).ToList();
            string perpId = contract.Truth.PerpetratorId;
            string victimId = contract.Truth.VictimId;
            var crime = contract.PrimaryCrimeType;

            bool victimMustBePresent = crime == CrimeType.Murder || crime == CrimeType.AssaultInjury;
            bool victimPresent = victimMustBePresent || _random.Next(2) == 0; // 비필수 유형은 균등 선택(가중치 미정)
            bool victimLeaves = crime == CrimeType.Murder || crime == CrimeType.Missing;
            bool hasCoverUp = contract.Truth.CoverUp != CoverUpMethod.None;

            string scene = roomIds[_random.Next(roomIds.Count)];
            int incidentSlot = _random.Next(2, slots - (hasCoverUp ? 3 : 2));
            int coverUpSlot = hasCoverUp ? incidentSlot + 1 : -1;

            // 발견자: 사건 이후 현장에 갈 수 있는 생존 인물(가해자 제외, 사망·실종 피해자 제외).
            var discovererCandidates = cast
                .Where(n => n.NpcId != perpId && !(n.NpcId == victimId && victimLeaves))
                .ToList();
            if (discovererCandidates.Count == 0)
            {
                error = ContractValidationErrorCode.CONTRADICTION;
                return null;
            }
            var discoverer = discovererCandidates[_random.Next(discovererCandidates.Count)];
            int firstDiscoverySlot = (hasCoverUp ? coverUpSlot : incidentSlot) + 1;
            int discoverySlot = _random.Next(firstDiscoverySlot, slots);

            var required = cast.ToDictionary(n => n.NpcId, n => new Dictionary<int, string>());
            required[perpId][incidentSlot] = scene;
            if (hasCoverUp) required[perpId][coverUpSlot] = scene;
            if (victimPresent) required[victimId][incidentSlot] = scene;
            foreach (var witness in cast.Where(n => n.ActualRole == NpcActualRole.Witness))
                required[witness.NpcId][incidentSlot] = scene;
            required[discoverer.NpcId][discoverySlot] = scene;

            // 발견 이전에 현장에 들어온 인물이 있으면 그 사람이 먼저 발견한 셈이 되므로 막는다.
            var forbidden = cast.ToDictionary(n => n.NpcId, n => new Dictionary<int, string>());
            foreach (var npc in cast.Where(n => n.NpcId != perpId))
            {
                for (int s = (hasCoverUp ? coverUpSlot : incidentSlot) + 1; s < discoverySlot; s++)
                    forbidden[npc.NpcId][s] = scene;
            }

            var timeline = new ContractTimeline
            {
                ContractId = contract.ContractId,
                CrimeType = crime,
                SlotMinutes = _config.SlotMinutes,
                SlotCount = slots,
                SceneRoomId = scene,
                PerpetratorId = perpId,
                VictimId = victimId,
                VictimLeavesStoryAfterIncident = victimLeaves
            };

            var walks = new Dictionary<string, string[]>();
            foreach (var npc in cast)
            {
                int lastSlot = npc.NpcId == victimId && victimLeaves ? incidentSlot : slots - 1;
                var walk = RandomWalk(roomIds, rooms, distance, lastSlot, required[npc.NpcId], forbidden[npc.NpcId]);
                if (walk == null)
                    return null;
                walks[npc.NpcId] = walk;
                timeline.Schedules[npc.NpcId] = ToSchedule(npc.NpcId, walk);
            }

            int M(int slot) => slot * _config.SlotMinutes;

            var core = AddEvent(timeline, TimelineEventKind.CoreIncident, M(incidentSlot), M(incidentSlot + 1), scene);
            core.ParticipantIds.Add(perpId);
            if (victimPresent) core.ParticipantIds.Add(victimId);

            TimelineEvent lastPerpEvent = core;
            if (hasCoverUp)
            {
                var cover = AddEvent(timeline, TimelineEventKind.CoverUp, M(coverUpSlot), M(coverUpSlot + 1), scene);
                cover.ParticipantIds.Add(perpId);
                cover.CauseEventIds.Add(core.EventId);
                lastPerpEvent = cover;
            }
            else if (incidentSlot + 1 < slots && walks[perpId][incidentSlot + 1] != scene)
            {
                var after = AddEvent(timeline, TimelineEventKind.Aftermath, M(incidentSlot + 1), M(incidentSlot + 2), walks[perpId][incidentSlot + 1]);
                after.ParticipantIds.Add(perpId);
                after.CauseEventIds.Add(core.EventId);
            }

            var discovery = AddEvent(timeline, TimelineEventKind.DiscoveryOrReport, M(discoverySlot), M(discoverySlot + 1), scene);
            discovery.ParticipantIds.Add(discoverer.NpcId);
            discovery.CauseEventIds.Add(lastPerpEvent.EventId);

            GrantKnowledge(timeline, walks);
            GrantPresence(timeline, walks, incidentSlot);
            return timeline;
        }

        private static TimelineEvent AddEvent(ContractTimeline timeline, TimelineEventKind kind, int start, int end, string room)
        {
            string id = $"{timeline.ContractId}-evt-{timeline.Events.Count + 1}-{kind}";
            var e = new TimelineEvent { EventId = id, Kind = kind, StartMinute = start, EndMinute = end, RoomId = room, InfoId = id };
            timeline.Events.Add(e);
            return e;
        }

        /// <summary>참여자는 자기 행위로, 같은 방에 있던 다른 인물은 직접 목격으로 사건 정보를 안다.</summary>
        private static void GrantKnowledge(ContractTimeline timeline, Dictionary<string, string[]> walks)
        {
            foreach (var e in timeline.Events)
            {
                int slot = timeline.SlotOf(e.StartMinute);
                foreach (var pair in walks)
                {
                    if (slot >= pair.Value.Length || pair.Value[slot] != e.RoomId)
                        continue;

                    timeline.Knowledge.Add(new KnowledgeGrant
                    {
                        NpcId = pair.Key,
                        InfoId = e.InfoId,
                        Path = e.ParticipantIds.Contains(pair.Key) ? InfoAcquisitionPath.OwnAction : InfoAcquisitionPath.DirectWitness,
                        Minute = e.StartMinute,
                        SourceEventId = e.EventId
                    });
                }
            }
        }

        /// <summary>핵심 사건 시각에 같은 방에 있던 인물끼리는 서로의 위치를 안다(알리바이 확인·반박 근거).</summary>
        private static void GrantPresence(ContractTimeline timeline, Dictionary<string, string[]> walks, int slot)
        {
            var present = walks.Where(p => slot < p.Value.Length).ToList();
            foreach (var observer in present)
            {
                foreach (var other in present.Where(o => o.Value[slot] == observer.Value[slot]))
                {
                    timeline.Knowledge.Add(new KnowledgeGrant
                    {
                        NpcId = observer.Key,
                        InfoId = ContractTimeline.PresenceInfoId(timeline.ContractId, other.Key, other.Value[slot], slot),
                        Path = observer.Key == other.Key ? InfoAcquisitionPath.OwnAction : InfoAcquisitionPath.DirectWitness,
                        Minute = slot * timeline.SlotMinutes
                    });
                }
            }
        }

        /// <summary>요구 위치를 모두 지키면서 한 칸에 최대 한 방씩 움직이는 무작위 경로. 불가능하면 null.</summary>
        private string[] RandomWalk(
            List<string> roomIds,
            RoomGraph rooms,
            Dictionary<(string, string), int> distance,
            int lastSlot,
            Dictionary<int, string> required,
            Dictionary<int, string> forbidden)
        {
            var walk = new string[lastSlot + 1];
            for (int s = 0; s <= lastSlot; s++)
            {
                IEnumerable<string> options = s == 0
                    ? roomIds
                    : new[] { walk[s - 1] }.Concat(rooms.GetAdjacentRooms(walk[s - 1]).OrderBy(id => id, StringComparer.Ordinal));

                int slot = s;
                var valid = options.Where(room =>
                {
                    if (required.TryGetValue(slot, out var must) && must != room) return false;
                    if (forbidden.TryGetValue(slot, out var banned) && banned == room) return false;
                    foreach (var req in required.Where(r => r.Key > slot && r.Key <= lastSlot))
                    {
                        if (!distance.TryGetValue((room, req.Value), out int d) || d > req.Key - slot)
                            return false;
                    }
                    return true;
                }).ToList();

                if (valid.Count == 0)
                    return null;
                walk[s] = valid[_random.Next(valid.Count)];
            }
            return walk;
        }

        private NpcSchedule ToSchedule(string npcId, string[] walk)
        {
            var schedule = new NpcSchedule(npcId);
            int start = 0;
            for (int s = 1; s <= walk.Length; s++)
            {
                if (s == walk.Length || walk[s] != walk[start])
                {
                    schedule.TryAdd(start * _config.SlotMinutes, s * _config.SlotMinutes, walk[start]);
                    start = s;
                }
            }
            return schedule;
        }

        private static Dictionary<(string, string), int> BuildDistances(RoomGraph rooms, List<string> roomIds)
        {
            var result = new Dictionary<(string, string), int>();
            foreach (var from in roomIds)
            {
                var queue = new Queue<string>();
                result[(from, from)] = 0;
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var next in rooms.GetAdjacentRooms(current))
                    {
                        if (result.ContainsKey((from, next)))
                            continue;
                        result[(from, next)] = result[(from, current)] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 04 "생성 검증 필수 항목" 중 시간표 관련 항목: NPC 위치·행동 시간표의 성립, 목격 정보와 NPC 지식의 획득 경로,
    /// 시간 순서·인과, 장소 존재. 오류 코드는 04 확정 코드만 쓴다.
    /// </summary>
    public sealed class ContractTimelineValidator
    {
        public ContractValidationResult Validate(ContractTimeline timeline, ContractInstance contract, NpcRegistry registry, RoomGraph rooms)
        {
            var result = new ContractValidationResult();
            void Add(ContractValidationErrorCode code)
            {
                if (!result.Errors.Contains(code)) result.Errors.Add(code);
            }

            if (timeline == null)
            {
                Add(ContractValidationErrorCode.CONTRACT_INVALID);
                return result;
            }

            // 일정: 존재하는 방, 빈틈 없는 연속, 칸 사이 이동은 인접한 방으로만.
            foreach (var schedule in timeline.Schedules.Values)
            {
                var entries = schedule.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (!rooms.RoomExists(entries[i].RoomId))
                        Add(ContractValidationErrorCode.LOCATION_INVALID);
                    if (i > 0 && (entries[i - 1].EndMinute != entries[i].StartMinute
                                  || !rooms.AreConnected(entries[i - 1].RoomId, entries[i].RoomId)))
                        Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                }
            }

            var byId = timeline.Events.ToDictionary(e => e.EventId);
            foreach (var e in timeline.Events)
            {
                if (e.EndMinute <= e.StartMinute || e.StartMinute < 0 || e.EndMinute > timeline.DayEndMinute)
                    Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                if (!rooms.RoomExists(e.RoomId))
                    Add(ContractValidationErrorCode.LOCATION_INVALID);

                foreach (var cause in e.CauseEventIds)
                {
                    if (!byId.TryGetValue(cause, out var c) || c.EndMinute > e.StartMinute)
                        Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                }

                // 참여자는 그 시각에 그 방에 있어야 한다.
                foreach (var participant in e.ParticipantIds)
                {
                    if (timeline.GetRoomAt(participant, e.StartMinute) != e.RoomId)
                        Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                }
            }

            var core = timeline.CoreIncident;
            if (timeline.Events.Count(e => e.Kind == TimelineEventKind.CoreIncident) != 1
                || core == null || !core.ParticipantIds.Contains(timeline.PerpetratorId))
            {
                Add(ContractValidationErrorCode.CONTRADICTION);
                return result;
            }

            if ((timeline.CrimeType == CrimeType.Murder || timeline.CrimeType == CrimeType.AssaultInjury)
                && !core.ParticipantIds.Contains(timeline.VictimId))
                Add(ContractValidationErrorCode.CONTRADICTION);

            // 사망·실종 피해자는 사건 이후 직접 행동하지 않는다.
            if (timeline.VictimLeavesStoryAfterIncident
                && timeline.Schedules.TryGetValue(timeline.VictimId, out var victimSchedule)
                && victimSchedule.Entries.Any(en => en.EndMinute > core.EndMinute))
                Add(ContractValidationErrorCode.CONTRADICTION);

            // 목격자 역할 인물은 실제로 목격해야 한다.
            if (registry != null)
            {
                foreach (var witness in registry.GetByContract(timeline.ContractId).Where(n => n.ActualRole == NpcActualRole.Witness))
                {
                    if (timeline.GetRoomAt(witness.NpcId, core.StartMinute) != core.RoomId)
                        Add(ContractValidationErrorCode.CONTRADICTION);
                }
            }

            var discovery = timeline.Discovery;
            if (discovery == null || discovery.StartMinute < core.EndMinute)
                Add(ContractValidationErrorCode.TIMELINE_CONFLICT);

            // 지식 획득 경로: 자기 행위는 참여자, 직접 목격은 그 시각 그 방에 있던 인물만.
            foreach (var grant in timeline.Knowledge)
            {
                if (grant.SourceEventId == null)
                    continue;
                if (!byId.TryGetValue(grant.SourceEventId, out var source))
                {
                    Add(ContractValidationErrorCode.KNOWLEDGE_LEAK);
                    continue;
                }

                bool present = timeline.GetRoomAt(grant.NpcId, source.StartMinute) == source.RoomId;
                bool valid = grant.Path == InfoAcquisitionPath.OwnAction
                    ? source.ParticipantIds.Contains(grant.NpcId) && present
                    : grant.Path != InfoAcquisitionPath.DirectWitness || present;
                if (!valid)
                    Add(ContractValidationErrorCode.KNOWLEDGE_LEAK);
            }

            if (contract != null && contract.PrimaryCrimeType != timeline.CrimeType)
                Add(ContractValidationErrorCode.CONTRADICTION);

            return result;
        }

        /// <summary>
        /// 적용 후 NPC가 실제로 아는 사건 정보가 모두 획득 경로(시간표 근거 또는 정보 전달 기록)를 갖는지 검사한다.
        /// </summary>
        public ContractValidationResult ValidateNpcKnowledge(
            ContractTimeline timeline,
            NpcRegistry registry,
            Func<string, string, bool> receivedByTransfer = null)
        {
            var result = new ContractValidationResult();
            var timelineInfoIds = new HashSet<string>(timeline.Knowledge.Select(k => k.InfoId).Concat(timeline.Events.Select(e => e.InfoId)));

            foreach (var npc in registry.GetByContract(timeline.ContractId))
            {
                foreach (var info in npc.KnownInfoIds.Where(timelineInfoIds.Contains))
                {
                    bool grounded = timeline.HasGrant(npc.NpcId, info)
                        || (receivedByTransfer != null && receivedByTransfer(npc.NpcId, info));
                    if (!grounded && !result.Errors.Contains(ContractValidationErrorCode.KNOWLEDGE_LEAK))
                        result.Errors.Add(ContractValidationErrorCode.KNOWLEDGE_LEAK);
                }
            }
            return result;
        }
    }

    public static class ContractTimelineApplier
    {
        /// <summary>
        /// 검증된 시간표를 런타임 상태에 반영한다: 사건 ID를 ContractTruth.TimelineEvents에 기록(중복 없음),
        /// 시간표 근거가 있는 정보만 NPC 지식에 추가, 살인 피해자 사망 처리, 조사 시작 시각의 위치에 인물 배치.
        /// 직접 겪거나 본 일은 사실로 믿는 것으로 둔다(해석 — 목격 내용의 믿음 판정 규칙은 원본에 없음).
        /// </summary>
        public static void Apply(ContractTimeline timeline, ContractInstance contract, NpcRegistry registry, RoomOccupancyTracker occupancy)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));

            foreach (var e in timeline.Events)
            {
                if (!contract.Truth.TimelineEvents.Contains(e.EventId))
                    contract.Truth.TimelineEvents.Add(e.EventId);
            }

            foreach (var grant in timeline.Knowledge)
            {
                var npc = registry.Get(grant.NpcId);
                if (!npc.Knows(grant.InfoId))
                    npc.LearnInfo(grant.InfoId, NpcJudgement.Believe);

                // 목격 여부는 실제 목격 사실로 기록한다(07 확정) — 역할과 무관하게 직접 본 사건이면 목격자다.
                if (grant.Path == InfoAcquisitionPath.DirectWitness && grant.SourceEventId != null)
                    npc.MarkWitnessed(grant.SourceEventId);
            }

            if (timeline.CrimeType == CrimeType.Murder)
                registry.Get(timeline.VictimId).MarkDeceased();

            int start = timeline.InvestigationStartMinute;
            foreach (var pair in timeline.Schedules)
            {
                string room = pair.Value.GetScheduledRoom(Math.Min(start, timeline.DayEndMinute - 1));
                if (room != null)
                    occupancy.PlaceAt(pair.Key, room);
                else if (pair.Key == timeline.VictimId && timeline.CrimeType == CrimeType.Murder)
                    occupancy.PlaceAt(pair.Key, timeline.SceneRoomId); // 시신은 현장에 남는다.
            }
        }
    }
}
