using System;
using System.Collections.Generic;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Meeting
{
    public enum CardManipulation
    {
        None,
        Distort,
        Conceal
    }

    public sealed class PresentedCard
    {
        public string PresenterId;
        public string ClueId;
        public CardManipulation Manipulation;

        /// <summary>왜곡/은폐를 실제로 적용한 결과. 조작하지 않았으면(None) null이다.</summary>
        public ManipulationRecord ManipulationRecordApplied;
    }

    /// <summary>저장/불러오기에서 회의 연속 카운터의 종류를 구분한다.</summary>
    public enum MeetingCounterKind
    {
        TurnSkip,
        Accept,
        ResponseFail,
        GiveUp
    }

    public readonly struct MeetingCounterSnapshot
    {
        public readonly MeetingCounterKind Kind;

        /// <summary>TurnSkip은 관찰자 구분 없이 본인 기준이므로 null이다.</summary>
        public readonly string ObserverId;
        public readonly string SuspectId;
        public readonly int Count;

        public MeetingCounterSnapshot(MeetingCounterKind kind, string observerId, string suspectId, int count)
        {
            Kind = kind;
            ObserverId = observerId;
            SuspectId = suspectId;
            Count = count;
        }
    }

    public sealed class SuspicionDeltaOutcome
    {
        public string ObserverId;
        public string SuspectId;
        public float Delta;
        public int ConsecutiveCountUsed;
        public bool WasDuplicateAction;
    }

    /// <summary>
    /// §7-1~7-3 회의 세션의 1차 구현. 회의 시작·종료, 카드 제시(왜곡/은폐 입력은 기존
    /// EvidenceManipulationService를 그대로 사용), 차례 넘기기·수용·대응 실패·포기의 연속 횟수를
    /// 추적해 MeetingSuspicionTable의 확정 수치를 그대로 돌려준다. 실제 PoliceSuspicionService에
    /// 반영하는 책임은 Police 모듈의 MeetingSuspicionCoordinator가 진다(순환 참조 방지 —
    /// Police는 Meeting을 참조하지만 Meeting은 Police를 참조하지 않는다).
    ///
    /// 범위 한정: NPC·경찰이 카드를 수용할지 의문을 제기할지 "결정"하는 판단 로직(§6-5의
    /// 수사/통제/권위/직관 성향별 전략)은 확정된 수치표가 없으므로 이 세션이 임의로 정하지 않는다.
    /// 그 판단 결과(수용/실패/포기 여부)는 호출자가 넘겨준다.
    /// </summary>
    public sealed class MeetingSession
    {
        public string ContractId { get; }
        public bool IsActive { get; private set; } = true;

        private readonly EvidenceManipulationService _evidenceManipulation;
        private readonly GameEventLog _eventLog = new GameEventLog();

        private readonly Dictionary<string, int> _consecutiveSkip = new Dictionary<string, int>();
        private readonly Dictionary<(string Observer, string Suspect), int> _consecutiveAccept =
            new Dictionary<(string, string), int>();
        private readonly Dictionary<(string Observer, string Suspect), int> _consecutiveFail =
            new Dictionary<(string, string), int>();
        private readonly Dictionary<(string Observer, string Suspect), int> _consecutiveGiveUp =
            new Dictionary<(string, string), int>();

        public MeetingSession(string contractId, EvidenceManipulationService evidenceManipulation)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            ContractId = contractId;
            _evidenceManipulation = evidenceManipulation ?? throw new ArgumentNullException(nameof(evidenceManipulation));
        }

        private void EnsureActive()
        {
            if (!IsActive)
                throw new InvalidOperationException("이미 종료된 회의 세션입니다.");
        }

        /// <summary>
        /// 카드를 제시한다. 왜곡/은폐는 기존 EvidenceManipulationService를 그대로 호출해 적용한다.
        /// 카드를 제시하면 본인의 연속 차례 넘기기 단계가 초기화된다(06페이지 확정).
        /// </summary>
        public PresentedCard PresentCard(
            string actionEventId,
            string presenterId,
            string clueId,
            CardManipulation manipulation,
            IReadOnlyList<string> presenterHeldClueIds = null)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.Custom, ContractId))
                return new PresentedCard { PresenterId = presenterId, ClueId = clueId, Manipulation = manipulation };

            ManipulationRecord applied = manipulation switch
            {
                CardManipulation.Distort => _evidenceManipulation.Distort(clueId, presenterId),
                CardManipulation.Conceal => _evidenceManipulation.Conceal(clueId, presenterId, presenterHeldClueIds),
                _ => null
            };

            _consecutiveSkip[presenterId] = 0;

            return new PresentedCard
            {
                PresenterId = presenterId,
                ClueId = clueId,
                Manipulation = manipulation,
                ManipulationRecordApplied = applied
            };
        }

        /// <summary>차례를 넘기면 본인(suspectId 관점에서 관찰자가 보는 대상)의 의심도가 상승한다.</summary>
        public SuspicionDeltaOutcome RecordTurnSkip(string actionEventId, string observerId, string suspectId)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.SuspicionChanged, ContractId))
                return Duplicate(observerId, suspectId);

            int count = _consecutiveSkip.TryGetValue(suspectId, out var c) ? c + 1 : 1;
            _consecutiveSkip[suspectId] = count;

            float delta = MeetingSuspicionTable.ConsecutiveTurnSkipIncrease(count);
            return new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = delta, ConsecutiveCountUsed = count };
        }

        /// <summary>제시한 카드(또는 거짓말)가 최종적으로 수용되었을 때 호출한다.</summary>
        public SuspicionDeltaOutcome RecordAccepted(string actionEventId, string observerId, string suspectId, bool verified)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.SuspicionChanged, ContractId))
                return Duplicate(observerId, suspectId);

            var key = (observerId, suspectId);
            int count = _consecutiveAccept.TryGetValue(key, out var c) ? c + 1 : 1;
            _consecutiveAccept[key] = count;
            _consecutiveFail[key] = 0;

            float delta = MeetingSuspicionTable.ConsecutiveAcceptDecrease(count);
            if (verified)
                delta += MeetingSuspicionTable.VerifiedAcceptBonus;

            return new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = delta, ConsecutiveCountUsed = count };
        }

        /// <summary>대응(검증)에 실패했을 때 호출한다. 수용 연속 단계는 초기화된다.</summary>
        public SuspicionDeltaOutcome RecordResponseFailed(string actionEventId, string observerId, string suspectId)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.SuspicionChanged, ContractId))
                return Duplicate(observerId, suspectId);

            var key = (observerId, suspectId);
            int count = _consecutiveFail.TryGetValue(key, out var c) ? c + 1 : 1;
            _consecutiveFail[key] = count;
            _consecutiveAccept[key] = 0;

            float delta = MeetingSuspicionTable.ConsecutiveResponseFailIncrease(count);
            return new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = delta, ConsecutiveCountUsed = count };
        }

        /// <summary>이전 회의에서 보류된 의문을 이번 회의에서도 처리하지 않고 넘겼을 때 호출한다(+10, 연속 횟수 없음).</summary>
        public SuspicionDeltaOutcome RecordCarriedOverDeferred(string actionEventId, string observerId, string suspectId)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.SuspicionChanged, ContractId))
                return Duplicate(observerId, suspectId);
            return new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = MeetingSuspicionTable.CarriedOverDeferIncrease };
        }

        /// <summary>대응을 포기했을 때 호출한다.</summary>
        public SuspicionDeltaOutcome RecordGiveUp(string actionEventId, string observerId, string suspectId)
        {
            EnsureActive();
            if (!ActionGuards.TryBeginOnce(_eventLog, actionEventId, GameEventType.SuspicionChanged, ContractId))
                return Duplicate(observerId, suspectId);

            var key = (observerId, suspectId);
            int count = _consecutiveGiveUp.TryGetValue(key, out var c) ? c + 1 : 1;
            _consecutiveGiveUp[key] = count;
            _consecutiveAccept[key] = 0;

            float delta = MeetingSuspicionTable.ConsecutiveGiveUpIncrease(count);
            return new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = delta, ConsecutiveCountUsed = count };
        }

        /// <summary>
        /// 카드가 받아들여지지 않았을 때(추가 검증 소진 등) 연속 수용 단계만 초기화한다. 의심도 변화는 없다
        /// (06 확정: 상대가 한 번이라도 수용하지 않으면 연속 수용 단계는 초기화, 검증 실패만으로는 의심도가 오르지 않는다).
        /// </summary>
        public void ResetAcceptStreak(string observerId, string suspectId)
        {
            EnsureActive();
            _consecutiveAccept[(observerId, suspectId)] = 0;
        }

        /// <summary>§7-1: 회의가 끝나면 조사 단계로 돌아갈 수 있다. 이후 이 세션으로는 아무것도 처리할 수 없다.</summary>
        public void EndSession() => IsActive = false;

        /// <summary>저장/불러오기 전용. 현재 연속 카운터 전부.</summary>
        public IReadOnlyList<MeetingCounterSnapshot> CaptureCounters()
        {
            var result = new List<MeetingCounterSnapshot>();
            foreach (var pair in _consecutiveSkip)
                result.Add(new MeetingCounterSnapshot(MeetingCounterKind.TurnSkip, null, pair.Key, pair.Value));
            foreach (var pair in _consecutiveAccept)
                result.Add(new MeetingCounterSnapshot(MeetingCounterKind.Accept, pair.Key.Observer, pair.Key.Suspect, pair.Value));
            foreach (var pair in _consecutiveFail)
                result.Add(new MeetingCounterSnapshot(MeetingCounterKind.ResponseFail, pair.Key.Observer, pair.Key.Suspect, pair.Value));
            foreach (var pair in _consecutiveGiveUp)
                result.Add(new MeetingCounterSnapshot(MeetingCounterKind.GiveUp, pair.Key.Observer, pair.Key.Suspect, pair.Value));
            return result;
        }

        /// <summary>저장/불러오기 전용. 의심도 변화를 다시 계산하지 않고 카운터 값만 되살린다.</summary>
        public void RestoreCounter(MeetingCounterSnapshot counter)
        {
            switch (counter.Kind)
            {
                case MeetingCounterKind.TurnSkip:
                    _consecutiveSkip[counter.SuspectId] = counter.Count;
                    break;
                case MeetingCounterKind.Accept:
                    _consecutiveAccept[(counter.ObserverId, counter.SuspectId)] = counter.Count;
                    break;
                case MeetingCounterKind.ResponseFail:
                    _consecutiveFail[(counter.ObserverId, counter.SuspectId)] = counter.Count;
                    break;
                case MeetingCounterKind.GiveUp:
                    _consecutiveGiveUp[(counter.ObserverId, counter.SuspectId)] = counter.Count;
                    break;
            }
        }

        /// <summary>저장/불러오기 전용. 이미 처리한 행동 eventId 목록(중복 처리 방지용).</summary>
        public IReadOnlyList<GameEvent> ProcessedActions => _eventLog.GetOrdered();

        /// <summary>저장/불러오기 전용. 이 eventId로 들어오는 행동은 불러온 뒤에도 다시 처리되지 않는다.</summary>
        public void RestoreProcessedAction(GameEvent processed) => _eventLog.TryRecord(processed);

        private static SuspicionDeltaOutcome Duplicate(string observerId, string suspectId) =>
            new SuspicionDeltaOutcome { ObserverId = observerId, SuspectId = suspectId, Delta = 0f, ConsecutiveCountUsed = 0, WasDuplicateAction = true };
    }
}
