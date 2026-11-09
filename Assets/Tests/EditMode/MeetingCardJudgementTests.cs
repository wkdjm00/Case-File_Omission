using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MeetingCardJudgementTests
    {
        private const string Player = "player";
        private const string ContractId = "c1";

        /// <summary>NextDouble이 항상 같은 값을 돌려주는 난수. 0.0이면 모든 확률 판정 성공, 0.99면 실패.</summary>
        private sealed class FixedRandom : Random
        {
            private readonly double _value;
            public FixedRandom(double value) { _value = value; }
            public override double NextDouble() => _value;
        }

        /// <summary>테스트용 판정자 목: 미리 정한 판단을 그대로 돌려준다.</summary>
        private sealed class ScriptedCardJudge : ICardJudge
        {
            private readonly Func<CardJudgementInput, CardJudgement> _script;
            public readonly List<CardJudgementInput> Received = new List<CardJudgementInput>();

            public ScriptedCardJudge(string id, Func<CardJudgementInput, CardJudgement> script)
            {
                JudgeId = id;
                _script = script;
            }

            public string JudgeId { get; }

            public CardJudgement Judge(CardJudgementInput input)
            {
                Received.Add(input);
                var j = _script(input);
                j.JudgeId = JudgeId;
                return j;
            }
        }

        private sealed class AlwaysQuestionPolicy : ICardQuestionPolicy
        {
            public bool IsProvisional => false;
            public bool ShouldRaiseQuestion(float suspicion, ContradictionLevel contradiction) => true;
        }

        private ClueRepository _clues;
        private MeetingSession _session;
        private MeetingCardResolver _resolver;
        private NpcRelationshipStore _relations;

        [SetUp]
        public void SetUp()
        {
            _clues = new ClueRepository();
            _clues.Discover(new ClueDefinition("card", ContractId));
            _clues.Discover(new ClueDefinition("rebuttal", ContractId));
            _session = new MeetingSession(ContractId, new EvidenceManipulationService(_clues, new Random(1)));
            _resolver = new MeetingCardResolver(_session, Player);
            _relations = new NpcRelationshipStore();
        }

        private PresentedCard PlayerCard(string clueId = "card") =>
            new PresentedCard { PresenterId = Player, ClueId = clueId, Manipulation = CardManipulation.None };

        private NpcCardJudge Npc(NpcState npc, double roll = 0.0, CardContradictionIndex index = null, ICardQuestionPolicy policy = null) =>
            new NpcCardJudge(npc, _relations, new LieDetectionService(_clues, new FixedRandom(roll)), index, policy);

        private static NpcState NewNpc(string id = "npc-1") =>
            new NpcState(id, ContractId, NpcActualRole.Witness, NpcPersonality.Indifferent);

        [Test]
        public void Police_LieAboutConfirmedClue_IsForcedDetection_AndAppliesResponseFailed()
        {
            _clues.GetRecord("card").MarkConfirmedByPolice(PoliceRoster.PoliceA);
            var police = new PoliceCardJudge(PoliceRoster.PoliceA, new PoliceSuspicionService(), new PoliceLifeStateService(),
                _clues, new EvidenceCapacityManager(), new LieDetectionService(_clues, new FixedRandom(0.0)), null);

            var result = _resolver.Resolve("e1", PlayerCard(), presenterIsLying: true, hasSupportingEvidence: true, new[] { police });

            var j = result.Judgements.Single();
            Assert.IsTrue(j.LieDetected);
            CollectionAssert.Contains(j.Reasons, CardJudgementReason.ForcedDetectionJudgeKnowsClueContent);
            Assert.AreEqual(MeetingSuspicionTable.ConsecutiveResponseFailIncrease(1), result.SuspicionDeltas.Single().Delta);
        }

        [Test]
        public void Npc_TruthfulCardWithoutContradiction_IsAccepted_AndPlayerGetsAcceptDecrease()
        {
            var result = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { Npc(NewNpc()) });

            var j = result.Judgements.Single();
            Assert.IsTrue(j.Accepts);
            Assert.IsTrue(j.DecidedByProvisionalPolicy, "수용/의문 경계는 임시 정책으로 표시된다.");
            Assert.AreEqual(MeetingSuspicionTable.ConsecutiveAcceptDecrease(1), result.SuspicionDeltas.Single().Delta);
        }

        [Test]
        public void Npc_ContradictionCountsOnlyInfoTheNpcActuallyKnows()
        {
            var index = new CardContradictionIndex();
            index.AddRebuttal("card", "rebuttal", ContradictionLevel.Clear);

            var unaware = NewNpc("npc-unaware");
            var aware = NewNpc("npc-aware");
            aware.LearnInfo("rebuttal", NpcJudgement.Believe);

            var result = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { Npc(unaware, index: index), Npc(aware, index: index) });

            var unawareJ = result.Judgements.Single(j => j.JudgeId == "npc-unaware");
            var awareJ = result.Judgements.Single(j => j.JudgeId == "npc-aware");
            Assert.AreEqual(ContradictionLevel.None, unawareJ.Contradiction);
            Assert.IsTrue(unawareJ.Accepts);
            Assert.AreEqual(ContradictionLevel.Clear, awareJ.Contradiction);
            Assert.IsTrue(awareJ.RaisesQuestion);
            Assert.AreEqual(1, result.OpenedQuestions.Count);
        }

        [Test]
        public void Npc_MaximumSuspicion_ActivelyVerifies_ByConfirmedRule()
        {
            var npc = NewNpc();
            _relations.SetSuspicion(npc.NpcId, Player, 100f);

            var j = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { Npc(npc) }).Judgements.Single();

            Assert.IsTrue(j.RaisesQuestion);
            Assert.IsFalse(j.DecidedByProvisionalPolicy);
            CollectionAssert.Contains(j.Reasons, CardJudgementReason.MaximumSuspicionActiveVerification);
        }

        [Test]
        public void DeceasedJudges_DoNotParticipate()
        {
            var npc = NewNpc();
            npc.MarkDeceased();
            var life = new PoliceLifeStateService();
            life.MarkDeceased(PoliceRoster.PoliceC);
            var police = new PoliceCardJudge(PoliceRoster.PoliceC, new PoliceSuspicionService(), life, _clues, null,
                new LieDetectionService(_clues), null);

            var result = _resolver.Resolve("e1", PlayerCard(), true, false, new ICardJudge[] { Npc(npc), police });

            Assert.IsTrue(result.Judgements.All(j => !j.Participated));
            Assert.AreEqual(0, result.SuspicionDeltas.Count);
        }

        [Test]
        public void Presenter_DoesNotJudgeOwnCard()
        {
            var self = new ScriptedCardJudge(Player, _ => new CardJudgement { Accepts = true });
            var other = new ScriptedCardJudge("npc-1", _ => new CardJudgement { Accepts = true });

            var result = _resolver.Resolve("e1", PlayerCard(), false, false, new ICardJudge[] { self, other });

            Assert.AreEqual(0, self.Received.Count);
            Assert.AreEqual(1, result.Judgements.Count);
        }

        [Test]
        public void UndetectedLie_IsAccepted_AndCountsTowardAcceptedLies()
        {
            var j = _resolver.Resolve("e1", PlayerCard(), true, false, new[] { Npc(NewNpc(), roll: 0.0) }).Judgements.Single();

            Assert.IsFalse(j.LieDetected);
            Assert.IsTrue(j.Accepts);
            CollectionAssert.Contains(j.Reasons, CardJudgementReason.LieNotDetectedByRoll);
            Assert.AreEqual(1, _resolver.GetAcceptedLieCount(Player));
        }

        [Test]
        public void DetectedLieByRoll_UsesConfirmedProbability()
        {
            var index = new CardContradictionIndex();
            index.AddRebuttal("card", "rebuttal", ContradictionLevel.Weak);
            var npc = NewNpc();
            npc.LearnInfo("rebuttal", NpcJudgement.Believe);

            var result = _resolver.Resolve("e1", PlayerCard(), true, false, new[] { Npc(npc, roll: 0.99, index: index) });
            var j = result.Judgements.Single();

            Assert.IsTrue(j.LieDetected);
            Assert.AreEqual(35f, j.LieProbabilityUsed, 0.001f, "50 - 15(약한 모순)");
            Assert.AreEqual(0, _resolver.GetAcceptedLieCount(Player));
        }

        [Test]
        public void QuestionThenSuccessfulResponse_AppliesVerifiedAcceptBonus()
        {
            var q = new ScriptedCardJudge("npc-1", _ => new CardJudgement { RaisesQuestion = true });
            var question = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();

            var r = _resolver.Respond("r1", question.QuestionId, success: true);

            Assert.AreEqual(CardQuestionState.AcceptedAfterVerification, r.State);
            Assert.AreEqual(MeetingSuspicionTable.ConsecutiveAcceptDecrease(1) + MeetingSuspicionTable.VerifiedAcceptBonus, r.SuspicionDelta.Delta);
            Assert.AreEqual(QuestionActionOutcome.AlreadyClosed, _resolver.GiveUp("g1", question.QuestionId).Outcome);
        }

        [Test]
        public void QuestionGiveUp_AndResponseFail_UseTheirTables()
        {
            var q = new ScriptedCardJudge("npc-1", _ => new CardJudgement { RaisesQuestion = true });
            var q1 = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();
            var q2 = _resolver.Resolve("e2", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();

            Assert.AreEqual(MeetingSuspicionTable.ConsecutiveGiveUpIncrease(1), _resolver.GiveUp("g1", q1.QuestionId).SuspicionDelta.Delta);
            Assert.AreEqual(MeetingSuspicionTable.ConsecutiveResponseFailIncrease(1), _resolver.Respond("r2", q2.QuestionId, false).SuspicionDelta.Delta);
        }

        [Test]
        public void AdditionalVerification_MaxTwo_ExhaustedRejectsWithoutSuspicionChange()
        {
            var q = new ScriptedCardJudge("npc-1", _ => new CardJudgement { RaisesQuestion = true });
            var question = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();

            var first = _resolver.AdditionalVerification("v1", question.QuestionId, false);
            Assert.AreEqual(CardQuestionState.Open, first.State);
            Assert.IsNull(first.SuspicionDelta);

            var second = _resolver.AdditionalVerification("v2", question.QuestionId, false);
            Assert.AreEqual(CardQuestionState.RejectedVerificationExhausted, second.State);
            Assert.IsNull(second.SuspicionDelta, "추가 검증 실패만으로 의심도는 오르지 않는다.");

            Assert.AreEqual(QuestionActionOutcome.AlreadyClosed, _resolver.AdditionalVerification("v3", question.QuestionId, true).Outcome);
        }

        [Test]
        public void DuplicateResolve_IsIgnored()
        {
            var npc = Npc(NewNpc());
            _resolver.Resolve("e1", PlayerCard(), false, false, new[] { npc });
            var again = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { npc });

            Assert.IsTrue(again.WasDuplicate);
            Assert.AreEqual(1, _resolver.JudgementLog.Count);
        }

        [Test]
        public void DuplicateQuestionAction_IsIgnored()
        {
            var q = new ScriptedCardJudge("npc-1", _ => new CardJudgement { RaisesQuestion = true });
            var q1 = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();
            _resolver.Defer(q1.QuestionId);
            _resolver.AdditionalVerification("v1", q1.QuestionId, false);

            Assert.AreEqual(QuestionActionOutcome.Duplicate, _resolver.AdditionalVerification("v1", q1.QuestionId, false).Outcome);
            Assert.AreEqual(1, q1.AdditionalVerificationsUsed);
        }

        [Test]
        public void NonPlayerPresenterAccepted_DoesNotApplyPlayerAcceptDecrease()
        {
            var card = new PresentedCard { PresenterId = "npc-2", ClueId = "card" };
            var result = _resolver.Resolve("e1", card, false, false, new[] { Npc(NewNpc()) });

            Assert.IsTrue(result.Judgements.Single().Accepts);
            Assert.AreEqual(0, result.SuspicionDeltas.Count);
        }

        [Test]
        public void Policy_IsSwappable()
        {
            var j = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { Npc(NewNpc(), policy: new AlwaysQuestionPolicy()) })
                .Judgements.Single();

            Assert.IsTrue(j.RaisesQuestion);
            Assert.IsFalse(j.DecidedByProvisionalPolicy);
        }

        [Test]
        public void DeferredQuestion_IsListed_AndCanBeResolvedLater()
        {
            var q = new ScriptedCardJudge("npc-1", _ => new CardJudgement { RaisesQuestion = true });
            var question = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { q }).OpenedQuestions.Single();

            _resolver.Defer(question.QuestionId);
            Assert.AreEqual(1, _resolver.DeferredQuestions.Count);

            _resolver.Respond("r1", question.QuestionId, true);
            Assert.AreEqual(0, _resolver.DeferredQuestions.Count);
        }

        [Test]
        public void Police_KnowsHeldClues_ForContradiction()
        {
            var capacity = new EvidenceCapacityManager();
            capacity.TryAcquire(PoliceRoster.PoliceA, HolderType.Police, "rebuttal");
            var index = new CardContradictionIndex();
            index.AddRebuttal("card", "rebuttal", ContradictionLevel.Clear);

            var police = new PoliceCardJudge(PoliceRoster.PoliceA, new PoliceSuspicionService(), new PoliceLifeStateService(),
                _clues, capacity, new LieDetectionService(_clues), index);

            Assert.IsTrue(police.Knows("rebuttal"));
            Assert.IsFalse(police.Knows("card"));
            var j = _resolver.Resolve("e1", PlayerCard(), false, false, new[] { police }).Judgements.Single();
            Assert.AreEqual(ContradictionLevel.Clear, j.Contradiction);
            Assert.IsTrue(j.RaisesQuestion);
        }
    }
}
