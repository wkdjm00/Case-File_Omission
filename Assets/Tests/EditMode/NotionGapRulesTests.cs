using System;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>2026-11-20 체크리스트 감사로 채운 규칙: 보류 의문 재보류·저장, 정보 전달 보정 최신표, 조직 조건 2종.</summary>
    public class NotionGapRulesTests
    {
        private const string Player = "player";
        private const string ContractId = "contract-x";

        // ───────── 보류 의문 ─────────

        private static CardQuestion CarriedQuestion() => new CardQuestion
        {
            QuestionId = "q-1", JudgeId = "npc-1", PresenterId = Player, ClueId = "card", State = CardQuestionState.Deferred, DeferCount = 1
        };

        private static MeetingCardResolver NextMeeting(params CardQuestion[] carried)
        {
            var clues = new ClueRepository();
            clues.Discover(new ClueDefinition("card", ContractId));
            var session = new MeetingSession(ContractId, new EvidenceManipulationService(clues, new Random(1)));
            return new MeetingCardResolver(session, Player, carried);
        }

        [Test]
        public void DeferringCarriedOverQuestionAgain_RaisesSuspicionByTen_Once()
        {
            var resolver = NextMeeting(CarriedQuestion());
            var result = resolver.Defer("q-1");

            Assert.AreEqual(QuestionActionOutcome.Applied, result.Outcome);
            Assert.IsNotNull(result.SuspicionDelta);
            Assert.AreEqual(MeetingSuspicionTable.CarriedOverDeferIncrease, result.SuspicionDelta.Delta);
            Assert.AreEqual(10f, result.SuspicionDelta.Delta);
            Assert.AreEqual("npc-1", result.SuspicionDelta.ObserverId, "의문 제기자가 제시자를 더 의심한다.");
            Assert.AreEqual(2, resolver.Questions.Single().DeferCount);
            Assert.AreEqual(QuestionActionOutcome.Duplicate, resolver.Defer("q-1").Outcome, "같은 회의에서 다시 보류해도 중복 적용하지 않는다.");
        }

        [Test]
        public void NoCarriedOverQuestions_LeavingAddsNothing()
        {
            var resolver = NextMeeting();
            Assert.AreEqual(0, resolver.DeferUnhandledCarriedOver().Count);
        }

        [Test]
        public void LeavingWithoutHandlingCarriedOverQuestion_CountsAsDeferringAgain()
        {
            var resolver = NextMeeting(CarriedQuestion());
            var deltas = resolver.DeferUnhandledCarriedOver();

            Assert.AreEqual(1, deltas.Count);
            Assert.AreEqual(10f, deltas[0].Delta);
            Assert.AreEqual(CardQuestionState.Deferred, resolver.Questions.Single().State);
        }

        [Test]
        public void CarriedOverQuestions_SurviveSaveLoad_AndSkipUnknownJudge()
        {
            var targets = new SaveLoad.WorldStateTargets { CarriedOverQuestions = new System.Collections.Generic.List<CardQuestion> { CarriedQuestion() } };
            var data = SaveLoad.WorldStateSaveService.Capture(targets);
            var json = UnityEngine.JsonUtility.ToJson(data);

            var restoredTargets = new SaveLoad.WorldStateTargets
            {
                CarriedOverQuestions = new System.Collections.Generic.List<CardQuestion>(),
                Npcs = new NpcRegistry()
            };
            restoredTargets.Npcs.Add(new NpcState("npc-1", ContractId, NpcActualRole.Witness, NpcPersonality.Guilty));
            SaveLoad.WorldStateSaveService.Restore(UnityEngine.JsonUtility.FromJson<SaveLoad.WorldSaveData>(json), restoredTargets);

            var q = restoredTargets.CarriedOverQuestions.Single();
            Assert.AreEqual("q-1", q.QuestionId);
            Assert.AreEqual(1, q.DeferCount);
            Assert.AreEqual(CardQuestionState.Deferred, q.State);

            var unknownJudgeTargets = new SaveLoad.WorldStateTargets { CarriedOverQuestions = new System.Collections.Generic.List<CardQuestion>(), Npcs = new NpcRegistry() };
            var logger = new Core.GameLogger();
            SaveLoad.WorldStateSaveService.Restore(UnityEngine.JsonUtility.FromJson<SaveLoad.WorldSaveData>(json), unknownJudgeTargets, logger);
            Assert.AreEqual(0, unknownJudgeTargets.CarriedOverQuestions.Count, "판정자가 없는 의문은 건너뛴다.");
            Assert.That(logger.Entries.Any());
        }

        // ───────── 정보 전달 보정(07 2026-11-12 최종) ─────────

        private static float Chance(NpcState sender, bool trusted = false, bool exposure = false, InfoSenderRelation? relation = null)
        {
            sender.LearnInfo("info", NpcJudgement.Believe);
            var service = new NpcInfoTransferService(new Random(1));
            if (relation.HasValue) service.RelationOf = (_, __) => relation.Value;
            var receiver = new NpcState("rx", ContractId, NpcActualRole.CaseRelatedA, NpcPersonality.Indifferent);
            return service.TryTransfer(sender, receiver, "info", true, SuspicionLevel.None, false, exposure, trusted).ProbabilityUsed;
        }

        private static NpcState Sender(NpcPersonality p, NpcActualRole role = NpcActualRole.CaseRelatedA) => new NpcState("tx", ContractId, role, p);

        [Test]
        public void InfoTransfer_PersonalityTable_Matches20261112()
        {
            Assert.AreEqual(50f, Chance(Sender(NpcPersonality.Indifferent)), "무관심한 0");
            Assert.AreEqual(50f, Chance(Sender(NpcPersonality.Greedy)), "욕심이 많은 성격 자체 0");
            Assert.AreEqual(40f, Chance(Sender(NpcPersonality.FrequentLiar)), "거짓말을 자주 하는 -10");
            Assert.AreEqual(50f, Chance(Sender(NpcPersonality.BlindlyTrusting)), "맹목적으로 믿는은 신뢰 대상이 아니면 0");
            Assert.AreEqual(65f, Chance(Sender(NpcPersonality.BlindlyTrusting), trusted: true));
        }

        [Test]
        public void InfoTransfer_RoleModifier_AppliesOnlyByRelation_AndNotDoubleCountedWithExposure()
        {
            Assert.AreEqual(50f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Perpetrator), relation: InfoSenderRelation.None),
                "가해자라도 자기 범행과 무관한 정보면 역할 보정 없음");
            Assert.AreEqual(35f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Perpetrator), relation: InfoSenderRelation.IncriminatesSender));
            Assert.AreEqual(35f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Perpetrator), exposure: true, relation: InfoSenderRelation.IncriminatesSender),
                "자기 범행 노출 위험은 역할 보정에 이미 반영 — 상황 보정 -15를 다시 더하지 않는다");
            Assert.AreEqual(60f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Witness), relation: InfoSenderRelation.DirectlyWitnessed));
            Assert.AreEqual(60f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Victim), relation: InfoSenderRelation.OwnExperience));
            Assert.AreEqual(50f, Chance(Sender(NpcPersonality.Indifferent, NpcActualRole.Perpetrator), relation: InfoSenderRelation.DirectlyWitnessed),
                "가해자는 목격자 보정을 받지 않는다");
        }

        // ───────── 조직 조건 2종(05) ─────────

        private static ContractInstance Contract(OrganizationConditionType type)
        {
            var c = new ContractInstance(ContractId);
            c.OrgCondition.Type = type;
            return c;
        }

        [Test]
        public void DisguiseAsSuicide_AchievedOnlyWhenOfficialConclusionIsSuicide()
        {
            var c = Contract(OrganizationConditionType.DisguiseAsSuicide);
            Assert.IsNull(OrganizationConditionEvaluator.Evaluate(c, new ClueRepository()), "결론 전에는 판정하지 않는다");
            c.OfficialConclusion.IsFinalized = true;
            c.OfficialConclusion.Outcome = ContractOutcome.Resolved;
            Assert.IsFalse(OrganizationConditionEvaluator.Evaluate(c, new ClueRepository()));
            c.OfficialConclusion.Outcome = ContractOutcome.ResolvedAsSuicide;
            Assert.IsTrue(OrganizationConditionEvaluator.Evaluate(c, new ClueRepository()));
        }

        [Test]
        public void ConcealCulpritClues_RequiresBothTargetsConcealed()
        {
            var clues = new ClueRepository();
            clues.Discover(new ClueDefinition("a", ContractId));
            clues.Discover(new ClueDefinition("b", ContractId));
            var c = Contract(OrganizationConditionType.ConcealCulpritClues);
            c.OrgCondition.SetTargetIds(new[] { "a", "b" });

            Assert.IsFalse(OrganizationConditionEvaluator.Evaluate(c, clues));
            clues.GetRecord("a").ApplyConcealment(true, detected: false);
            Assert.IsFalse(OrganizationConditionEvaluator.Evaluate(c, clues), "하나만 은폐하면 미달성");
            clues.GetRecord("b").ApplyConcealment(true, detected: false);
            Assert.IsTrue(OrganizationConditionEvaluator.Evaluate(c, clues));
        }

        [Test]
        public void ConditionTargetIds_AreLockedAfterAccept()
        {
            var c = Contract(OrganizationConditionType.ConcealCulpritClues);
            c.OrgCondition.SetTargetIds(new[] { "a", "b" });
            c.OrgCondition.Lock();
            Assert.Throws<InvalidOperationException>(() => c.OrgCondition.SetTargetIds(new[] { "c" }));
        }

        [Test]
        public void GeneratedContracts_BindValidTargets_ForNewConditions()
        {
            int suicide = 0, conceal = 0;
            for (int seed = 1; seed <= 80; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                var offer = s.OfferContract();
                if (offer == null) continue;
                var cond = offer.Contract.OrgCondition;
                if (cond.Type == OrganizationConditionType.DisguiseAsSuicide)
                {
                    suicide++;
                    Assert.AreEqual(CrimeType.Murder, offer.Contract.PrimaryCrimeType);
                    Assert.AreEqual(offer.Contract.Truth.VictimId, cond.TargetId, "자살 위장 대상은 숨진 핵심 피해자");
                }
                if (cond.Type == OrganizationConditionType.ConcealCulpritClues)
                {
                    conceal++;
                    Assert.AreEqual(2, cond.TargetIds.Distinct().Count());
                    Assert.That(cond.TargetIds.All(id => offer.GetMaterial(id).IdentifiesPerpetrator));
                }
            }
            Assert.That(suicide + conceal, Is.GreaterThan(0), "80개 시드에서 새 조건이 한 번도 나오지 않았다.");
        }
    }
}
