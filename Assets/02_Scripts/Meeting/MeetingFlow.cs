using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Meeting
{
    public enum MeetingParticipantKind
    {
        Player,
        Police,
        Npc
    }

    public enum ConclusionOutcome
    {
        /// <summary>참가자 과반수가 받아들여 결론이 채택되었다.</summary>
        Adopted,

        /// <summary>과반에 못 미쳐 검증을 이어 간다(추가 검증 최대 2회).</summary>
        NeedsVerification,

        /// <summary>추가 검증 뒤에도 결론이 나지 않아 미제로 끝난다. 미제는 실패가 아니다.</summary>
        Unsolved
    }

    public sealed class ConclusionResult
    {
        public ConclusionOutcome Outcome;
        public string AccusedId;
        public int AcceptCount;
        public int RejectCount;
        public int VerificationsUsed;
    }

    /// <summary>
    /// 회의 한 번의 차례·제시 한도·최종 결론 진행(06 확정).
    /// - 한 차례 제시 한도: 플레이어·경찰 5장, 기본 NPC 3장. 의문에 대응하는 카드는 한도에 세지 않는다.
    /// - 회의 시작 시 플레이어에게 개인 단서 카드 5장을 무작위로 준다(<see cref="CardDrawService"/>).
    /// - 최종 결론: 플레이어가 결론을 제안하고 참가자 과반수가 받아들이면 종결한다. 과반에 못 미치면 검증을 이어 가며
    ///   추가 검증은 최대 2회, 그 뒤에도 동률이면 미제(실패 아님).
    /// 원문에 없는 부분(임시): 차례 순서는 등록 순서, 추가 검증 뒤 동률이 아닌데 과반도 없는 경우도 미제로 끝낸다.
    /// </summary>
    public sealed class MeetingFlow
    {
        private readonly List<(string Id, MeetingParticipantKind Kind)> _participants = new List<(string, MeetingParticipantKind)>();
        private readonly Dictionary<string, int> _presentedThisTurn = new Dictionary<string, int>();
        private readonly List<string> _playerHand = new List<string>();
        private int _turnIndex = -1;

        public string PlayerId { get; }
        public int ConclusionVerificationsUsed { get; private set; }
        public ConclusionResult LastConclusion { get; private set; }

        public MeetingFlow(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId는 비어 있을 수 없습니다.", nameof(playerId));
            PlayerId = playerId;
            _participants.Add((playerId, MeetingParticipantKind.Player));
        }

        public IReadOnlyList<string> ParticipantIds => _participants.Select(p => p.Id).ToList();

        /// <summary>회의 시작 시 무작위로 받은 개인 단서 카드.</summary>
        public IReadOnlyList<string> PlayerHand => _playerHand;

        public string CurrentTurnId => _turnIndex >= 0 ? _participants[_turnIndex].Id : null;

        public void AddParticipant(string id, MeetingParticipantKind kind)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id는 비어 있을 수 없습니다.", nameof(id));
            if (kind == MeetingParticipantKind.Player) throw new ArgumentException("플레이어는 이미 참가자입니다.", nameof(kind));
            if (_participants.Any(p => p.Id == id)) return;
            _participants.Add((id, kind));
        }

        public void DealPlayerHand(IReadOnlyList<string> heldClueIds, Random random)
        {
            _playerHand.Clear();
            _playerHand.AddRange(CardDrawService.DrawCards(heldClueIds, CardPresentationLimits.PlayerNormalTurnLimit, random));
        }

        public static int GetTurnLimit(MeetingParticipantKind kind) => kind switch
        {
            MeetingParticipantKind.Player => CardPresentationLimits.PlayerNormalTurnLimit,
            MeetingParticipantKind.Police => CardPresentationLimits.PoliceNormalTurnLimit,
            _ => CardPresentationLimits.BasicNpcNormalTurnLimit
        };

        /// <summary>다음 참가자 차례로 넘어간다. 차례가 바뀌면 그 참가자의 제시 횟수는 새로 센다.</summary>
        public string AdvanceTurn()
        {
            _turnIndex = (_turnIndex + 1) % _participants.Count;
            _presentedThisTurn[CurrentTurnId] = 0;
            return CurrentTurnId;
        }

        public int PresentedThisTurn(string participantId) =>
            _presentedThisTurn.TryGetValue(participantId, out int n) ? n : 0;

        /// <summary>
        /// 일반 차례 카드 제시가 가능한지. 자기 차례여야 하고 한도 안이어야 한다. 의문 대응 카드는 이 검사를 거치지 않는다.
        /// 플레이어는 회의 시작 때 받은 카드만 낼 수 있다.
        /// </summary>
        public bool CanPresentNormalCard(string participantId, string clueId = null)
        {
            if (participantId != CurrentTurnId)
                return false;
            if (PresentedThisTurn(participantId) >= GetTurnLimit(KindOf(participantId)))
                return false;
            return participantId != PlayerId || clueId == null || _playerHand.Contains(clueId);
        }

        public void RecordNormalCardPresented(string participantId)
        {
            if (!CanPresentNormalCard(participantId))
                throw new InvalidOperationException("지금은 일반 카드를 더 낼 수 없습니다.");
            _presentedThisTurn[participantId] = PresentedThisTurn(participantId) + 1;
        }

        /// <summary>
        /// 최종 결론을 판정한다. votes는 결론을 판단하는 참가자(플레이어 제외, 살아 있는 참가자)의 수용 여부다.
        /// 처음 제안은 검증 횟수에 세지 않고, 과반에 못 미친 뒤 다시 판정할 때마다 추가 검증 1회로 센다(최대 2회).
        /// </summary>
        public ConclusionResult EvaluateConclusion(string accusedId, IReadOnlyDictionary<string, bool> votes)
        {
            if (votes == null) throw new ArgumentNullException(nameof(votes));

            bool isVerification = LastConclusion != null && LastConclusion.Outcome == ConclusionOutcome.NeedsVerification;
            if (isVerification)
            {
                if (ConclusionVerificationsUsed >= CardPresentationLimits.MaxAdditionalVerificationsPerCardOrClaim)
                    throw new InvalidOperationException("결론 추가 검증은 최대 2회입니다.");
                ConclusionVerificationsUsed++;
            }

            var counted = votes.Where(v => v.Key != PlayerId).ToList();
            int accept = counted.Count(v => v.Value);
            int reject = counted.Count - accept;

            ConclusionOutcome outcome;
            if (counted.Count > 0 && accept * 2 > counted.Count)
                outcome = ConclusionOutcome.Adopted;
            else if (ConclusionVerificationsUsed < CardPresentationLimits.MaxAdditionalVerificationsPerCardOrClaim)
                outcome = ConclusionOutcome.NeedsVerification;
            else
                outcome = ConclusionOutcome.Unsolved;

            LastConclusion = new ConclusionResult
            {
                Outcome = outcome,
                AccusedId = accusedId,
                AcceptCount = accept,
                RejectCount = reject,
                VerificationsUsed = ConclusionVerificationsUsed
            };
            return LastConclusion;
        }

        private MeetingParticipantKind KindOf(string id)
        {
            foreach (var p in _participants)
                if (p.Id == id) return p.Kind;
            throw new KeyNotFoundException($"회의 참가자 '{id}'가 아닙니다.");
        }
    }
}
