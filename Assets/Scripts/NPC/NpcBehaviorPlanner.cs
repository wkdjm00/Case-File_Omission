using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.NPC
{
    /// <summary>
    /// 07 "행동 반영: 질문·조사·회피·신고 등 가능한 행동"과 일정 이동·정보 전달·대기.
    /// 질문·조사·회피·신고는 효과가 원본 기획에 정의되지 않았으므로 내장 실행이 없고, 외부 후보 소스가
    /// 실행 콜백과 함께 제공할 때만 선택된다.
    /// </summary>
    public enum NpcBehaviorKind
    {
        Wait,
        MoveBySchedule,
        ShareInfo,
        Investigate,
        Question,
        Avoid,
        Report
    }

    public enum NpcBehaviorStatus
    {
        Executed,
        DuplicateTurn,
        NotAlive,
        NoPosition,
        BlockedWaiting,
        ScheduleGoalAbandoned
    }

    public sealed class NpcBehaviorCandidate
    {
        public NpcBehaviorKind Kind;

        /// <summary>MoveBySchedule면 목적지 방 ID, ShareInfo면 수신 NPC ID.</summary>
        public string TargetId;
        public string InfoId;

        /// <summary>설정되어 있으면 이 방에 있을 때만 가능한 행동(위치 가능성 검사).</summary>
        public string RequiredRoomId;

        /// <summary>설정되어 있으면 이 정보를 실제로 알 때만 가능한 행동(지식 가능성 검사).</summary>
        public string RequiredInfoId;

        /// <summary>외부 후보 전용 실행 콜백. 내장 종류(대기·일정 이동·정보 전달)는 플래너가 직접 실행한다.</summary>
        public Action Execute;
    }

    public interface INpcBehaviorCandidateSource
    {
        IEnumerable<NpcBehaviorCandidate> GetCandidates(NpcState actor, string currentRoomId, int currentMinute);
    }

    /// <summary>가능한 후보 중 하나를 고른다. 성격별 우선순위 수치는 원본 기획에 없으므로 교체 가능한 정책으로 둔다.</summary>
    public interface INpcBehaviorPolicy
    {
        bool IsProvisional { get; }
        NpcBehaviorCandidate Choose(NpcState actor, IReadOnlyList<NpcBehaviorCandidate> feasible);
    }

    /// <summary>
    /// 임시 기본 정책(확정 아님). 기존 NpcActionLoop와 같은 순서 — 같은 방 상대에게 전달할 정보가 있으면 전달,
    /// 그 다음 외부 후보(등록 순서), 그 다음 일정 이동, 마지막으로 대기. 성격에 따른 가중치는 적용하지 않는다.
    /// </summary>
    public sealed class ProvisionalNpcBehaviorPolicy : INpcBehaviorPolicy
    {
        public bool IsProvisional => true;

        public NpcBehaviorCandidate Choose(NpcState actor, IReadOnlyList<NpcBehaviorCandidate> feasible)
        {
            return feasible.FirstOrDefault(c => c.Kind == NpcBehaviorKind.ShareInfo)
                ?? feasible.FirstOrDefault(c => c.Kind != NpcBehaviorKind.MoveBySchedule && c.Kind != NpcBehaviorKind.Wait)
                ?? feasible.FirstOrDefault(c => c.Kind == NpcBehaviorKind.MoveBySchedule)
                ?? feasible.FirstOrDefault();
        }
    }

    /// <summary>
    /// 07 "NPC 성격별 행동 선택 규칙" 표에서 행동 종류로 옮길 수 있는 경향만 담는다(정성 기준, 수치 없음).
    /// - 의심이 많은: 추가 조사·대조를 우선할 수 있음 → Investigate 선호
    /// - 겁이 많은: 위험을 줄이고 안전을 확보하는 행동 선호 → Avoid 선호
    /// 나머지 성격의 경향(이익 기회·신뢰 인물의 요청·직접 영향 여부 등)은 상황 데이터가 있어야 판단할 수 있어
    /// 행동 종류 선호로 옮기지 않았다. 정보 전달 경향은 NpcInfoTransferService의 확정 보정값이 이미 반영한다.
    /// 질투하는·의존하는(08 성격 오브젝트)은 07 표에 없다 — 07 표의 경멸하는·이익을 좇는과 목록이 다르다.
    /// </summary>
    public static class NpcPersonalityTendencies
    {
        public static IReadOnlyCollection<NpcBehaviorKind> GetPreferredKinds(NpcPersonality personality) => personality switch
        {
            NpcPersonality.Suspicious => new[] { NpcBehaviorKind.Investigate },
            NpcPersonality.Timid => new[] { NpcBehaviorKind.Avoid },
            _ => Array.Empty<NpcBehaviorKind>()
        };

        /// <summary>07 "성격별 행동 선택 규칙" 표에 설명이 있는 성격인지.</summary>
        public static bool IsDescribedInBehaviorTable(NpcPersonality personality) =>
            personality != NpcPersonality.Jealous && personality != NpcPersonality.Dependent;
    }

    /// <summary>
    /// 성격 경향을 확률로 반영하는 정책. "성격은 행동을 강제하지 않고 선택 후보의 우선순위와 가능성에 영향을 준다"(확정)를
    /// 따르되, 가중치 값은 원본 기획에 없으므로 생성자에서 반드시 주입받는다(기본값 없음).
    /// </summary>
    public sealed class TendencyWeightedNpcBehaviorPolicy : INpcBehaviorPolicy
    {
        private readonly float _baseWeight;
        private readonly float _preferredWeight;
        private readonly Random _random;

        public TendencyWeightedNpcBehaviorPolicy(float baseWeight, float preferredWeight, Random random)
        {
            if (baseWeight <= 0f) throw new ArgumentOutOfRangeException(nameof(baseWeight));
            if (preferredWeight <= 0f) throw new ArgumentOutOfRangeException(nameof(preferredWeight));
            _baseWeight = baseWeight;
            _preferredWeight = preferredWeight;
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public bool IsProvisional => true;

        public NpcBehaviorCandidate Choose(NpcState actor, IReadOnlyList<NpcBehaviorCandidate> feasible)
        {
            // 대기는 다른 할 일이 없을 때만 고른다(07 확정 "할 일이 없으면 대기").
            var active = feasible.Where(c => c.Kind != NpcBehaviorKind.Wait).ToList();
            if (active.Count == 0)
                return feasible.FirstOrDefault();

            var preferred = NpcPersonalityTendencies.GetPreferredKinds(actor.Personality);
            var weights = active.Select(c => preferred.Contains(c.Kind) ? _preferredWeight : _baseWeight).ToList();

            double roll = _random.NextDouble() * weights.Sum();
            for (int i = 0; i < active.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                    return active[i];
            }
            return active[active.Count - 1];
        }
    }

    public sealed class NpcBehaviorResult
    {
        public string NpcId;
        public NpcBehaviorKind Kind = NpcBehaviorKind.Wait;
        public NpcBehaviorStatus Status;
        public string TargetId;
        public string InfoId;
        public InfoTransferResult TransferResult;
        public bool DecidedByProvisionalPolicy;

        /// <summary>이번 행동 중 떨어진 단서를 발견해 자동 획득한 단서.</summary>
        public readonly List<string> PickedUpClueIds = new List<string>();

        /// <summary>발견했지만 보유 한도(3개)라 획득하지 않고 지나친 단서.</summary>
        public readonly List<string> PassedClueIds = new List<string>();
    }

    /// <summary>
    /// 07 "NPC 행동 결정 구조"의 공통 루프. 기존 NpcActionLoop는 그대로 두고, 그 위에 다음을 더한다.
    /// - 가능성 검사: 생존, 위치, 같은 방 접촉, 실제 보유 정보, 일정, 이동 경로.
    /// - 한 턴에 한 NPC는 한 번만 행동한다. 정보 전달 상대도 대화에 참여한 것으로 보고 그 턴의 행동을 쓴다.
    /// - 할 일이 없으면 대기(07 확정). 기존 NpcActionLoop의 "무작위 인접 이동"은 이 확정 규칙과 다르다.
    /// - 경로가 막히면 다른 경로를 먼저 찾고, 없으면 기다리며, 일정 횟수 이상 막히면 그 일정 목표를 포기한다(무한 반복 금지 — 확정).
    /// - 기본 NPC는 떨어진 단서를 발견하면 자동 획득하고, 이미 3개면 획득하지 않고 지나간다(06 확정). 교체하지 않는다.
    ///
    /// 미정(교체 가능): 행동 우선순위 정책, 의심도 수치→강/약 분류 경계, 막힘 허용 횟수, 전달 이익·노출 위험 판단.
    /// </summary>
    public sealed class NpcBehaviorPlanner
    {
        /// <summary>임시값(확정 아님). 원본은 "무한정 반복하지 않도록"까지만 정했다.</summary>
        public int MaxBlockedWaitTurns = 2;

        /// <summary>막힌 방 판정(플레이어·장애물). 기본은 막힌 방 없음.</summary>
        public Func<string, bool> IsRoomBlocked;

        /// <summary>전달자→수신자 의심도 수치를 확정 보정표의 강/약 분류로 바꾼다. 기본은 임시 분류.</summary>
        public Func<float, SuspicionLevel> SuspicionClassifier = ClassifySuspicionProvisional;

        /// <summary>정보 전달의 구체적 이익·노출 위험 여부. 판단할 상황 데이터가 없으면 둘 다 false.</summary>
        public Func<NpcState, NpcState, string, (bool HasConcreteBenefit, bool HasExposureRisk)> TransferContext;

        private readonly NpcRegistry _registry;
        private readonly RoomOccupancyTracker _occupancy;
        private readonly RoomGraph _rooms;
        private readonly NpcInfoTransferService _transfer;
        private readonly NpcRelationshipStore _relationships;
        private readonly ClueRepository _clues;
        private readonly EvidenceCapacityManager _capacity;
        private readonly INpcBehaviorPolicy _policy;

        private readonly GameEventLog _turnLog = new GameEventLog();
        private readonly Dictionary<string, NpcSchedule> _schedules = new Dictionary<string, NpcSchedule>();
        private readonly List<INpcBehaviorCandidateSource> _sources = new List<INpcBehaviorCandidateSource>();
        private readonly Dictionary<string, int> _blockedTurns = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _abandonedEntryStart = new Dictionary<string, int>();

        public NpcBehaviorPlanner(
            NpcRegistry registry,
            RoomOccupancyTracker occupancy,
            RoomGraph rooms,
            NpcInfoTransferService transfer,
            NpcRelationshipStore relationships,
            ClueRepository clues = null,
            EvidenceCapacityManager capacity = null,
            INpcBehaviorPolicy policy = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _clues = clues;
            _capacity = capacity;
            _policy = policy ?? new ProvisionalNpcBehaviorPolicy();
        }

        /// <summary>
        /// 임시 분류(확정 아님). 06 의심도 단계표를 빌려 '높음'(35) 이상을 강한 의심, '낮음'(10)~'보통'을 약한 의심으로 본다.
        /// </summary>
        public static SuspicionLevel ClassifySuspicionProvisional(float suspicion)
        {
            if (suspicion >= 35f) return SuspicionLevel.Strong;
            if (suspicion >= 10f) return SuspicionLevel.Weak;
            return SuspicionLevel.None;
        }

        public void SetSchedule(NpcSchedule schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            _schedules[schedule.NpcId] = schedule;
        }

        public void AddCandidateSource(INpcBehaviorCandidateSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _sources.Add(source);
        }

        /// <summary>의뢰의 모든 NPC가 이번 턴에 한 번씩 행동한다(ID 순서로 결정적 처리).</summary>
        public IReadOnlyList<NpcBehaviorResult> ExecuteTurn(string turnId, string contractId, int currentMinute)
        {
            return _registry.GetByContract(contractId)
                .OrderBy(n => n.NpcId, StringComparer.Ordinal)
                .Select(n => Act(turnId, n.NpcId, currentMinute))
                .ToList();
        }

        public NpcBehaviorResult Act(string turnId, string npcId, int currentMinute)
        {
            if (string.IsNullOrEmpty(turnId)) throw new ArgumentException("turnId는 비어 있을 수 없습니다.", nameof(turnId));

            var actor = _registry.Get(npcId);
            var result = new NpcBehaviorResult { NpcId = npcId };

            // §5-1: 사망한 인물은 직접 행동하지 않는다.
            if (!actor.IsAlive)
            {
                result.Status = NpcBehaviorStatus.NotAlive;
                return result;
            }

            if (!ActionGuards.TryBeginOnce(_turnLog, TurnKey(turnId, npcId), GameEventType.Custom, actor.ContractId))
            {
                result.Status = NpcBehaviorStatus.DuplicateTurn;
                return result;
            }

            if (!_occupancy.HasPosition(npcId))
            {
                result.Status = NpcBehaviorStatus.NoPosition;
                return result;
            }

            string room = _occupancy.GetRoom(npcId);
            AutoPickUpDroppedClues(actor, room, result);

            var feasible = GatherCandidates(turnId, actor, room, currentMinute)
                .Where(c => IsFeasible(c, actor, room))
                .ToList();

            var chosen = _policy.Choose(actor, feasible) ?? new NpcBehaviorCandidate { Kind = NpcBehaviorKind.Wait };
            result.Kind = chosen.Kind;
            result.TargetId = chosen.TargetId;
            result.InfoId = chosen.InfoId;
            result.DecidedByProvisionalPolicy = _policy.IsProvisional && feasible.Count > 1;
            result.Status = NpcBehaviorStatus.Executed;

            switch (chosen.Kind)
            {
                case NpcBehaviorKind.ShareInfo:
                    ExecuteShare(turnId, actor, chosen, result);
                    break;
                case NpcBehaviorKind.MoveBySchedule:
                    ExecuteScheduledMove(actor, room, chosen, currentMinute, result);
                    break;
                case NpcBehaviorKind.Wait:
                    break;
                default:
                    chosen.Execute?.Invoke();
                    break;
            }

            if (chosen.Kind != NpcBehaviorKind.MoveBySchedule)
                _blockedTurns.Remove(npcId);

            return result;
        }

        private static string TurnKey(string turnId, string npcId) => $"npc-turn:{turnId}:{npcId}";

        private IEnumerable<NpcBehaviorCandidate> GatherCandidates(string turnId, NpcState actor, string room, int minute)
        {
            var partners = _registry.GetByContract(actor.ContractId)
                .Where(n => n.NpcId != actor.NpcId && n.IsAlive
                            && _occupancy.HasPosition(n.NpcId) && _occupancy.GetRoom(n.NpcId) == room
                            && !_turnLog.HasProcessed(TurnKey(turnId, n.NpcId)))
                .OrderBy(n => n.NpcId, StringComparer.Ordinal);

            foreach (var partner in partners)
            {
                string info = actor.KnownInfoIds
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault(id => !partner.Knows(id) && !_transfer.HasTransferred(actor.NpcId, partner.NpcId, id));
                if (info != null)
                {
                    yield return new NpcBehaviorCandidate
                    {
                        Kind = NpcBehaviorKind.ShareInfo,
                        TargetId = partner.NpcId,
                        InfoId = info,
                        RequiredRoomId = room,
                        RequiredInfoId = info
                    };
                }
            }

            foreach (var source in _sources)
            {
                foreach (var candidate in source.GetCandidates(actor, room, minute) ?? Enumerable.Empty<NpcBehaviorCandidate>())
                {
                    if (candidate != null)
                        yield return candidate;
                }
            }

            if (_schedules.TryGetValue(actor.NpcId, out var schedule)
                && schedule.TryGetEntryAt(minute, out var entry)
                && entry.RoomId != room
                && !(_abandonedEntryStart.TryGetValue(actor.NpcId, out int abandoned) && abandoned == entry.StartMinute))
            {
                yield return new NpcBehaviorCandidate { Kind = NpcBehaviorKind.MoveBySchedule, TargetId = entry.RoomId };
            }

            yield return new NpcBehaviorCandidate { Kind = NpcBehaviorKind.Wait };
        }

        private bool IsFeasible(NpcBehaviorCandidate c, NpcState actor, string room)
        {
            if (c.RequiredRoomId != null && c.RequiredRoomId != room)
                return false;
            if (c.RequiredInfoId != null && !actor.Knows(c.RequiredInfoId))
                return false;

            switch (c.Kind)
            {
                case NpcBehaviorKind.Wait:
                    return true;
                case NpcBehaviorKind.ShareInfo:
                    return c.InfoId != null && actor.Knows(c.InfoId)
                        && _registry.Exists(c.TargetId) && _registry.Get(c.TargetId).IsAlive
                        && _occupancy.HasPosition(c.TargetId) && _occupancy.GetRoom(c.TargetId) == room;
                case NpcBehaviorKind.MoveBySchedule:
                    return c.TargetId != null && _rooms.CanReach(room, c.TargetId);
                default:
                    return c.Execute != null;
            }
        }

        private void ExecuteShare(string turnId, NpcState actor, NpcBehaviorCandidate chosen, NpcBehaviorResult result)
        {
            var receiver = _registry.Get(chosen.TargetId);
            var level = (SuspicionClassifier ?? ClassifySuspicionProvisional)(_relationships.GetSuspicion(actor.NpcId, receiver.NpcId));
            var context = TransferContext?.Invoke(actor, receiver, chosen.InfoId) ?? (false, false);

            // 같은 방 = 접촉 가능. 대화 상대도 이번 턴의 행동을 쓴 것으로 기록한다(대화가 끝난 뒤 각자 다음 행동).
            result.TransferResult = _transfer.TryTransfer(actor, receiver, chosen.InfoId, canContact: true,
                level, context.HasConcreteBenefit, context.HasExposureRisk);
            ActionGuards.TryBeginOnce(_turnLog, TurnKey(turnId, receiver.NpcId), GameEventType.Custom, receiver.ContractId);
        }

        private void ExecuteScheduledMove(NpcState actor, string room, NpcBehaviorCandidate chosen, int minute, NpcBehaviorResult result)
        {
            // 막힌 방을 피한 다른 경로를 먼저 찾는다.
            var path = _rooms.FindPath(room, chosen.TargetId, IsRoomBlocked);
            if (path == null || path.Count == 0)
            {
                int blocked = (_blockedTurns.TryGetValue(actor.NpcId, out int b) ? b : 0) + 1;
                if (blocked >= MaxBlockedWaitTurns)
                {
                    _blockedTurns.Remove(actor.NpcId);
                    if (_schedules[actor.NpcId].TryGetEntryAt(minute, out var entry))
                        _abandonedEntryStart[actor.NpcId] = entry.StartMinute;
                    result.Status = NpcBehaviorStatus.ScheduleGoalAbandoned;
                }
                else
                {
                    _blockedTurns[actor.NpcId] = blocked;
                    result.Status = NpcBehaviorStatus.BlockedWaiting;
                }
                return;
            }

            _blockedTurns.Remove(actor.NpcId);
            _occupancy.TryMove(actor.NpcId, path[0]);
            result.TargetId = path[0];
            AutoPickUpDroppedClues(actor, path[0], result);
        }

        private void AutoPickUpDroppedClues(NpcState actor, string room, NpcBehaviorResult result)
        {
            if (_clues == null || _capacity == null)
                return;

            var dropped = _occupancy.GetOccupants(room)
                .Where(id => _clues.Exists(id) && _clues.GetRecord(id).WorldState == ClueWorldState.DroppedInWorld)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            foreach (var clueId in dropped)
            {
                // 교체 판단 없이 시도 — 한도면 획득하지 않고 지나간다(기본 NPC는 교체하지 않음).
                var acquisition = _capacity.TryAcquire(actor.NpcId, HolderType.Npc, clueId);
                if (acquisition.Outcome == EvidenceAcquisitionOutcome.Acquired)
                {
                    _clues.GetRecord(clueId).MarkHeldByNpc();
                    _occupancy.Remove(clueId);
                    // 직접 조사로 알게 된 정보다. 믿음 판정 규칙은 없으므로 판단 보류로 둔다.
                    if (!actor.Knows(clueId))
                        actor.LearnInfo(clueId, NpcJudgement.Withhold);
                    result.PickedUpClueIds.Add(clueId);
                }
                else if (acquisition.Outcome == EvidenceAcquisitionOutcome.RejectedAtCapacity)
                {
                    result.PassedClueIds.Add(clueId);
                }
            }
        }
    }
}
