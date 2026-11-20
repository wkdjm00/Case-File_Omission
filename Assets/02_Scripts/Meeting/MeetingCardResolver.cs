using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;

namespace CaseFileOmission.Meeting
{
    public enum CardQuestionState
    {
        Open,
        Deferred,
        AcceptedAfterVerification,
        RejectedResponseFailed,
        RejectedGaveUp,
        RejectedVerificationExhausted
    }

    [Serializable]
    public sealed class CardQuestion
    {
        public string QuestionId;
        public string JudgeId;
        public string PresenterId;
        public string ClueId;
        public bool PresenterIsLying;
        public bool PresenterUsedDistortion;
        public CardQuestionState State = CardQuestionState.Open;
        public int AdditionalVerificationsUsed;

        /// <summary>보류된 횟수(회의를 넘어 누적). 저장한다.</summary>
        public int DeferCount;

        public bool IsClosed => State != CardQuestionState.Open && State != CardQuestionState.Deferred;
    }

    public enum QuestionActionOutcome
    {
        Applied,
        Duplicate,
        UnknownQuestion,
        AlreadyClosed,
        VerificationLimitReached
    }

    public sealed class QuestionActionResult
    {
        public QuestionActionOutcome Outcome;
        public CardQuestionState State;
        public SuspicionDeltaOutcome SuspicionDelta;
        public string ActionEventId;
        public string PresenterId;
        public string JudgeId;
        public string ClueId;
        public bool PresenterIsLying;
        public bool PresenterUsedDistortion;
    }

    public sealed class CardPresentationResolution
    {
        public bool WasDuplicate;

        /// <summary>이전 회의에서 넘어온 의문이 아직 처리되지 않아 새 카드를 처리하지 않았다.</summary>
        public bool BlockedByCarriedOverQuestions;
        public readonly List<CardJudgement> Judgements = new List<CardJudgement>();
        public readonly List<SuspicionDeltaOutcome> SuspicionDeltas = new List<SuspicionDeltaOutcome>();
        public readonly List<CardQuestion> OpenedQuestions = new List<CardQuestion>();
    }

    [Serializable]
    public sealed class MeetingAcceptedLieCountSnapshot
    {
        public string PresenterId;
        public int Count;
    }

    [Serializable]
    public sealed class MeetingCardResolverSnapshot
    {
        public CardQuestion[] Questions;
        public string[] CarriedOverQuestionIds;
        public MeetingAcceptedLieCountSnapshot[] AcceptedLieCounts;
        public string[] ProcessedActionIds;
    }

    /// <summary>
    /// 회의에서 카드가 제시되었을 때 판정자들의 판단을 모으고, 그 결과를 기존 <see cref="MeetingSession"/>의
    /// 확정 수치 기록(수용/대응 실패/포기)에 연결한다. 의심도 수치를 다시 계산하지 않는다.
    ///
    /// 확정 규칙:
    /// - 제시자 본인은 자기 카드를 판정하지 않는다.
    /// - 연속 수용 감소는 "플레이어가 제시한 카드"의 수용에만 적용한다(06 §20-6).
    /// - 의문 제기 후 검증을 거쳐 최종 수용하면 검증 보너스 -8을 추가한다(MeetingSession.RecordAccepted verified).
    /// - 대응 포기는 포기 표, 대응 실패는 실패 표를 쓴다.
    /// - 추가 검증은 최대 2회, 모두 실패하면 카드가 받아들여지지 않지만 그 실패만으로 의심도는 오르지 않는다.
    /// - 의문은 보류할 수 있으며 다음 회의에서 가장 먼저 처리해야 한다.
    ///
    /// 해석(기존 MeetingSuspicionCoordinator와 같은 해석): 거짓말이 발각되면 대응 실패 표를 적용한다.
    /// 미정: 보류한 의문을 다음 회의에서도 넘겼을 때의 의심도 상승량 — 수치가 없어 적용하지 않고 표시만 한다.
    /// </summary>
    public sealed class MeetingCardResolver
    {
        public const int MaxAdditionalVerifications = 2;

        private readonly MeetingSession _session;
        private readonly string _playerId;
        private readonly GameEventLog _eventLog = new GameEventLog();
        private readonly Dictionary<string, CardQuestion> _questions = new Dictionary<string, CardQuestion>();
        private readonly List<CardJudgement> _judgementLog = new List<CardJudgement>();
        private readonly Dictionary<string, int> _acceptedLieCount = new Dictionary<string, int>();

        private readonly HashSet<string> _carriedOverQuestionIds = new HashSet<string>();

        /// <param name="carriedOverQuestions">이전 회의에서 보류된 의문. 이번 회의에서 가장 먼저 처리해야 한다(06 확정).</param>
        public MeetingCardResolver(MeetingSession session, string playerId, IEnumerable<CardQuestion> carriedOverQuestions = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId는 비어 있을 수 없습니다.", nameof(playerId));
            _playerId = playerId;

            foreach (var q in carriedOverQuestions ?? Enumerable.Empty<CardQuestion>())
            {
                if (q == null || q.IsClosed) continue;
                q.State = CardQuestionState.Open;
                _questions[q.QuestionId] = q;
                _carriedOverQuestionIds.Add(q.QuestionId);
            }
        }

        /// <summary>이전 회의에서 넘어와 아직 처리되지 않은 의문. 이것이 남아 있으면 새 카드를 처리하지 않는다.</summary>
        public IReadOnlyList<CardQuestion> PendingCarriedOverQuestions =>
            _carriedOverQuestionIds.Select(id => _questions[id]).Where(q => !q.IsClosed && q.State != CardQuestionState.Deferred).ToList();

        /// <summary>지금까지 내려진 모든 판단(근거 포함). 디버그·검수용.</summary>
        public IReadOnlyList<CardJudgement> JudgementLog => _judgementLog;

        public IReadOnlyCollection<CardQuestion> Questions => _questions.Values;

        /// <summary>
        /// 거짓말이 다른 참가자에게 수락된 횟수. 직관 중심형 경찰의 단서 확인 요청 보정(2회당 +5%p)에 넘긴다.
        /// </summary>
        public int GetAcceptedLieCount(string presenterId) =>
            _acceptedLieCount.TryGetValue(presenterId, out int c) ? c : 0;

        /// <summary>보류된 의문 — 다음 회의에서 가장 먼저 처리해야 한다(확정).</summary>
        public IReadOnlyList<CardQuestion> DeferredQuestions =>
            _questions.Values.Where(q => q.State == CardQuestionState.Deferred).ToList();

        public CardPresentationResolution Resolve(
            string actionEventId,
            PresentedCard card,
            bool presenterIsLying,
            bool hasSupportingEvidence,
            IEnumerable<ICardJudge> judges)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (judges == null) throw new ArgumentNullException(nameof(judges));

            var resolution = new CardPresentationResolution();
            if (PendingCarriedOverQuestions.Count > 0)
            {
                resolution.BlockedByCarriedOverQuestions = true;
                return resolution;
            }

            if (!ActionGuards.TryBeginOnce(_eventLog, "resolve:" + actionEventId, GameEventType.Custom, _session.ContractId))
            {
                resolution.WasDuplicate = true;
                return resolution;
            }

            var input = new CardJudgementInput
            {
                PresenterId = card.PresenterId,
                ClueId = card.ClueId,
                PresenterIsLying = presenterIsLying,
                HasSupportingEvidence = hasSupportingEvidence
            };

            bool presenterIsPlayer = card.PresenterId == _playerId;

            foreach (var judge in judges)
            {
                if (judge == null || judge.JudgeId == card.PresenterId)
                    continue;

                var judgement = judge.Judge(input);
                _judgementLog.Add(judgement);
                resolution.Judgements.Add(judgement);

                if (!judgement.Participated)
                    continue;

                string judgeEventId = $"{actionEventId}:{judge.JudgeId}";

                if (judgement.LieDetected)
                {
                    resolution.SuspicionDeltas.Add(_session.RecordResponseFailed(judgeEventId, judge.JudgeId, card.PresenterId));
                    continue;
                }

                if (judgement.RaisesQuestion)
                {
                    var question = new CardQuestion
                    {
                        QuestionId = judgeEventId,
                        JudgeId = judge.JudgeId,
                        PresenterId = card.PresenterId,
                        ClueId = card.ClueId,
                        PresenterIsLying = presenterIsLying,
                        PresenterUsedDistortion = card.Manipulation == CardManipulation.Distort
                    };
                    _questions[question.QuestionId] = question;
                    resolution.OpenedQuestions.Add(question);
                    continue;
                }

                if (judgement.Accepts)
                {
                    if (presenterIsLying)
                        _acceptedLieCount[card.PresenterId] = GetAcceptedLieCount(card.PresenterId) + 1;

                    if (presenterIsPlayer)
                        resolution.SuspicionDeltas.Add(_session.RecordAccepted(judgeEventId, judge.JudgeId, card.PresenterId, verified: false));
                }
            }

            return resolution;
        }

        /// <summary>의문에 대응한다. 성공하면 검증 후 최종 수용, 실패하면 대응 실패 표를 적용한다.</summary>
        public QuestionActionResult Respond(string actionEventId, string questionId, bool success)
        {
            if (!TryGetActionable(actionEventId, questionId, out var question, out var rejected))
                return rejected;

            if (success)
                return AcceptAfterVerification(actionEventId, question);

            question.State = CardQuestionState.RejectedResponseFailed;
            return new QuestionActionResult
            {
                Outcome = QuestionActionOutcome.Applied,
                State = question.State,
                SuspicionDelta = _session.RecordResponseFailed(actionEventId, question.JudgeId, question.PresenterId)
            };
        }

        /// <summary>대응을 포기한다. 카드 내용은 받아들여지지 않고 포기 표를 적용한다.</summary>
        public QuestionActionResult GiveUp(string actionEventId, string questionId)
        {
            if (!TryGetActionable(actionEventId, questionId, out var question, out var rejected))
                return rejected;

            question.State = CardQuestionState.RejectedGaveUp;
            return new QuestionActionResult
            {
                Outcome = QuestionActionOutcome.Applied,
                State = question.State,
                SuspicionDelta = _session.RecordGiveUp(actionEventId, question.JudgeId, question.PresenterId)
            };
        }

        /// <summary>추가 검증(최대 2회). 2회 모두 실패하면 카드는 받아들여지지 않으며 의심도 변화는 없다.</summary>
        public QuestionActionResult AdditionalVerification(string actionEventId, string questionId, bool success)
        {
            if (_questions.TryGetValue(questionId ?? string.Empty, out var existing)
                && !existing.IsClosed && existing.AdditionalVerificationsUsed >= MaxAdditionalVerifications)
            {
                return new QuestionActionResult { Outcome = QuestionActionOutcome.VerificationLimitReached, State = existing.State };
            }

            if (!TryGetActionable(actionEventId, questionId, out var question, out var rejected))
                return rejected;

            question.AdditionalVerificationsUsed++;

            if (success)
                return AcceptAfterVerification(actionEventId, question);

            if (question.AdditionalVerificationsUsed >= MaxAdditionalVerifications)
            {
                question.State = CardQuestionState.RejectedVerificationExhausted;
                _session.ResetAcceptStreak(question.JudgeId, question.PresenterId);
            }

            return new QuestionActionResult { Outcome = QuestionActionOutcome.Applied, State = question.State };
        }

        /// <summary>의문을 보류한다. 다음 회의에서 가장 먼저 처리해야 한다.</summary>
        public QuestionActionResult Defer(string questionId)
        {
            if (!_questions.TryGetValue(questionId ?? string.Empty, out var question))
                return new QuestionActionResult { Outcome = QuestionActionOutcome.UnknownQuestion };
            if (question.IsClosed)
                return new QuestionActionResult { Outcome = QuestionActionOutcome.AlreadyClosed, State = question.State };

            if (question.State == CardQuestionState.Deferred)
                return new QuestionActionResult { Outcome = QuestionActionOutcome.Duplicate, State = question.State };

            question.State = CardQuestionState.Deferred;
            question.DeferCount++;
            // 이전 회의에서 넘어온 의문을 이번에도 넘기면 +10(사용자 확정 2026-11-20). 의심하는 쪽은 의문 제기자, 대상은 제시자(플레이어만).
            SuspicionDeltaOutcome delta = _carriedOverQuestionIds.Contains(question.QuestionId) && question.PresenterId == _playerId
                ? _session.RecordCarriedOverDeferred($"defer-again:{question.QuestionId}", question.JudgeId, question.PresenterId)
                : null;
            return new QuestionActionResult { Outcome = QuestionActionOutcome.Applied, State = question.State, SuspicionDelta = delta };
        }

        /// <summary>
        /// 회의를 마칠 때: 이전 회의에서 넘어와 이번 회의에서 아무 처리도 하지 않은 의문은 다시 넘긴 것으로 보고 보류 처리한다(+10).
        /// 이번 회의에 새로 생긴 열린 의문은 그대로 다음 회의로 넘어간다.
        /// </summary>
        public List<SuspicionDeltaOutcome> DeferUnhandledCarriedOver()
        {
            var deltas = new List<SuspicionDeltaOutcome>();
            foreach (var id in _carriedOverQuestionIds.ToList())
            {
                var q = _questions[id];
                if (q.State != CardQuestionState.Open) continue;
                var r = Defer(id);
                if (r.SuspicionDelta != null) deltas.Add(r.SuspicionDelta);
            }
            return deltas;
        }

        public MeetingCardResolverSnapshot CaptureSnapshot()
        {
            return new MeetingCardResolverSnapshot
            {
                Questions = _questions.Values.OrderBy(q => q.QuestionId, StringComparer.Ordinal)
                    .Select(q => new CardQuestion
                    {
                        QuestionId = q.QuestionId,
                        JudgeId = q.JudgeId,
                        PresenterId = q.PresenterId,
                        ClueId = q.ClueId,
                        PresenterIsLying = q.PresenterIsLying,
                        PresenterUsedDistortion = q.PresenterUsedDistortion,
                        State = q.State,
                        AdditionalVerificationsUsed = q.AdditionalVerificationsUsed,
                        DeferCount = q.DeferCount
                    }).ToArray(),
                CarriedOverQuestionIds = _carriedOverQuestionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                AcceptedLieCounts = _acceptedLieCount.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new MeetingAcceptedLieCountSnapshot { PresenterId = p.Key, Count = p.Value }).ToArray(),
                ProcessedActionIds = _eventLog.GetOrdered().Select(e => e.EventId).Distinct(StringComparer.Ordinal).ToArray()
            };
        }

        public static bool TryRestoreSnapshot(
            MeetingSession session,
            string playerId,
            MeetingCardResolverSnapshot snapshot,
            out MeetingCardResolver restored)
        {
            restored = null;
            if (session == null || string.IsNullOrEmpty(playerId) || snapshot == null
                || snapshot.Questions == null || snapshot.CarriedOverQuestionIds == null
                || snapshot.AcceptedLieCounts == null || snapshot.ProcessedActionIds == null)
                return false;

            var resolver = new MeetingCardResolver(session, playerId);
            foreach (var q in snapshot.Questions)
            {
                if (q == null || string.IsNullOrEmpty(q.QuestionId) || string.IsNullOrEmpty(q.JudgeId)
                    || string.IsNullOrEmpty(q.PresenterId) || string.IsNullOrEmpty(q.ClueId)
                    || !Enum.IsDefined(typeof(CardQuestionState), q.State)
                    || q.AdditionalVerificationsUsed < 0 || q.AdditionalVerificationsUsed > MaxAdditionalVerifications
                    || q.DeferCount < 0 || resolver._questions.ContainsKey(q.QuestionId))
                    return false;
                resolver._questions.Add(q.QuestionId, new CardQuestion
                {
                    QuestionId = q.QuestionId,
                    JudgeId = q.JudgeId,
                    PresenterId = q.PresenterId,
                    ClueId = q.ClueId,
                    PresenterIsLying = q.PresenterIsLying,
                    PresenterUsedDistortion = q.PresenterUsedDistortion,
                    State = q.State,
                    AdditionalVerificationsUsed = q.AdditionalVerificationsUsed,
                    DeferCount = q.DeferCount
                });
            }

            foreach (var id in snapshot.CarriedOverQuestionIds)
            {
                if (string.IsNullOrEmpty(id) || !resolver._questions.ContainsKey(id))
                    return false;
                resolver._carriedOverQuestionIds.Add(id);
            }

            foreach (var c in snapshot.AcceptedLieCounts)
            {
                if (c == null || string.IsNullOrEmpty(c.PresenterId) || c.Count < 0
                    || resolver._acceptedLieCount.ContainsKey(c.PresenterId))
                    return false;
                resolver._acceptedLieCount.Add(c.PresenterId, c.Count);
            }

            foreach (var id in snapshot.ProcessedActionIds)
            {
                if (string.IsNullOrEmpty(id)) return false;
                if (!resolver._eventLog.TryRecord(new GameEvent(id, GameEventType.Custom, session.ContractId, "restored-meeting-action")))
                    continue;
            }

            restored = resolver;
            return true;
        }

        private QuestionActionResult AcceptAfterVerification(string actionEventId, CardQuestion question)
        {
            question.State = CardQuestionState.AcceptedAfterVerification;
            if (question.PresenterIsLying)
                _acceptedLieCount[question.PresenterId] = GetAcceptedLieCount(question.PresenterId) + 1;

            SuspicionDeltaOutcome delta = question.PresenterId == _playerId
                ? _session.RecordAccepted(actionEventId, question.JudgeId, question.PresenterId, verified: true)
                : null;

            return new QuestionActionResult
            {
                Outcome = QuestionActionOutcome.Applied,
                State = question.State,
                SuspicionDelta = delta,
                ActionEventId = actionEventId,
                PresenterId = question.PresenterId,
                JudgeId = question.JudgeId,
                ClueId = question.ClueId,
                PresenterIsLying = question.PresenterIsLying,
                PresenterUsedDistortion = question.PresenterUsedDistortion
            };
        }

        private bool TryGetActionable(string actionEventId, string questionId, out CardQuestion question, out QuestionActionResult rejected)
        {
            rejected = null;
            if (!_questions.TryGetValue(questionId ?? string.Empty, out question))
            {
                rejected = new QuestionActionResult { Outcome = QuestionActionOutcome.UnknownQuestion };
                return false;
            }

            if (question.IsClosed)
            {
                rejected = new QuestionActionResult { Outcome = QuestionActionOutcome.AlreadyClosed, State = question.State };
                return false;
            }

            if (!ActionGuards.TryBeginOnce(_eventLog, "question:" + actionEventId, GameEventType.Custom, _session.ContractId))
            {
                rejected = new QuestionActionResult { Outcome = QuestionActionOutcome.Duplicate, State = question.State };
                return false;
            }

            return true;
        }
    }
}
