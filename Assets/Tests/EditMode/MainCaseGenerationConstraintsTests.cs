using System;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>03 2026-11-12 "단서 우선 배치 → 의뢰 생성 제약"(피해자 다수·부부/연인 사망·직업·도구·성격).</summary>
    public class MainCaseGenerationConstraintsTests
    {
        private static GeneratedContract Generate(ContractConstraints constraints, int seed)
        {
            int counter = 0;
            var pipeline = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(seed));
            return pipeline.TryGenerate(() => $"c{seed}-{++counter}", out _, OrganizationConditionType.None, constraints);
        }

        private static readonly ContractConstraints[] AllPlans =
        {
            new ContractConstraints { PlannedClueName = MainCaseClueNames.GovernmentDepartmentContactLog, RequireMurder = true, VictimCount = 3 },
            new ContractConstraints { PlannedClueName = MainCaseClueNames.ResultReportRecording, RequireMurder = true, VictimOccupation = "비서", ToolId = "TOOL-01", PerpetratorPersonality = NpcPersonality.Suspicious },
            new ContractConstraints { PlannedClueName = MainCaseClueNames.SeniorOfficialLetter, PerpetratorPersonality = NpcPersonality.Greedy },
            new ContractConstraints { PlannedClueName = MainCaseClueNames.NumberNoteLeftForLover, RequireMurder = true, CoupleVictims = true },
        };

        [Test]
        public void EveryPlan_GeneratesContractsSatisfyingItsConstraints([Range(0, 3)] int plan, [Values(1, 2, 3, 4, 5)] int seed)
        {
            var constraints = AllPlans[plan];
            var built = Generate(constraints, seed);
            Assert.IsNotNull(built, $"{constraints.PlannedClueName} seed {seed}");
            Assert.IsTrue(constraints.IsSatisfiedBy(built.Contract, built.Npcs));
            Assert.AreEqual(constraints.PlannedClueName, built.Contract.PlannedMainCaseClue);
            Assert.LessOrEqual(built.Npcs.GetByContract(built.Contract.ContractId).Count(), ContractContentGenerator.MaxCastSize);
        }

        [Test]
        public void ThreeVictimMurder_AllVictimsDeadAtScene_AndContextMeetsContactLogRule([Values(1, 2, 3)] int seed)
        {
            var built = Generate(AllPlans[0], seed);
            var c = built.Contract;
            Assert.AreEqual(2, c.Truth.AdditionalVictimIds.Count);
            foreach (var id in new[] { c.Truth.VictimId }.Concat(c.Truth.AdditionalVictimIds))
            {
                Assert.IsFalse(built.Npcs.Get(id).IsAlive, id);
                Assert.AreEqual(built.Timeline.SceneRoomId, built.Occupancy.GetRoom(id), "시신은 현장에 남는다");
                Assert.IsTrue(built.Timeline.CoreIncident.ParticipantIds.Contains(id));
            }
            Assert.IsTrue(c.PoliceKnownFacts.Any(f => c.Truth.AdditionalVictimIds.All(id => f.Contains(built.Npcs.Get(id).DisplayName))),
                "경찰 초기 정보에 모든 사망자가 나온다");
            Assert.IsFalse(built.Timeline.Discovery.ParticipantIds.Intersect(c.Truth.AdditionalVictimIds).Any(), "숨진 사람은 발견자가 될 수 없다");

            var context = MainCaseClueContext.FromContract(c, built.Npcs);
            Assert.AreEqual(3, context.VictimCount);
            Assert.IsTrue(new MainCaseClueAcquisitionService().IsEligible(MainCaseClueCode.GovernmentDepartmentContactLog, context, new MainCaseProgressTracker()));
        }

        [Test]
        public void SecretaryKnifeSuspicious_IsEligibleForRecordingAndRoster([Values(1, 2, 3)] int seed)
        {
            var built = Generate(AllPlans[1], seed);
            var context = MainCaseClueContext.FromContract(built.Contract, built.Npcs);
            var service = new MainCaseClueAcquisitionService();
            Assert.IsTrue(service.IsEligible(MainCaseClueCode.ResultReportRecording, context, new MainCaseProgressTracker()));
            Assert.IsTrue(service.IsEligible(MainCaseClueCode.ParticipantRosterDeliveryConfirmation, context, new MainCaseProgressTracker()));
        }

        [Test]
        public void CoupleMurder_IsEligibleForNumberNote([Values(1, 2, 3)] int seed)
        {
            var built = Generate(AllPlans[3], seed);
            Assert.IsTrue(built.Contract.Truth.VictimsAreCouple);
            var context = MainCaseClueContext.FromContract(built.Contract, built.Npcs);
            Assert.IsTrue(new MainCaseClueAcquisitionService().IsEligible(MainCaseClueCode.NumberNoteLeftForLover, context, new MainCaseProgressTracker()));
        }

        [Test]
        public void Session_InSystemicStage_PlansContactLogCase_AndSceneInvestigationYieldsIt()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(11) });
            s.StartNewGame();
            s.CompletePrologue();
            Assert.AreEqual(MainCaseTruthStage.SystemicTruth, s.MainCase.CurrentStage, "프롤로그 자료로 1단계를 마친다(가족 통지 기록은 임시 지급)");

            var offer = s.OfferContract();
            Assert.IsNotNull(offer);
            Assert.AreEqual(MainCaseClueNames.GovernmentDepartmentContactLog, offer.Contract.PlannedMainCaseClue);
            Assert.AreEqual(2, offer.Contract.Truth.AdditionalVictimIds.Count);
            Assert.IsTrue(s.AcceptContract().Success);

            s.Occupancy.PlaceAt(s.PlayerId, s.Campaign.SceneRoomId);
            var obj = s.GetInvestigationObjects().First(o => o.RoomId == s.PlayerRoom);
            var result = s.InvestigateObject(obj.ObjectId);
            Assert.IsTrue(result.Success);
            Assert.IsTrue(s.MainCase.HasAcquired(MainCaseClueNames.GovernmentDepartmentContactLog));

            var loaded = GameSession.Load(s.CaptureSave(), new GameSessionConfig { Random = new Random(1) });
            CollectionAssert.AreEqual(s.CurrentContract.Truth.AdditionalVictimIds, loaded.CurrentContract.Truth.AdditionalVictimIds);
            Assert.AreEqual(MainCaseClueNames.GovernmentDepartmentContactLog, loaded.CurrentContract.PlannedMainCaseClue);
        }

        private static GameSession SessionAtStage(MainCaseTruthStage stage, int seed)
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
            s.StartNewGame();
            s.CompletePrologue();
            if (stage >= MainCaseTruthStage.PowerTruth)
                foreach (var clue in MainCaseClueNames.SystemicTruthClues) s.MainCase.AcquireClue(clue);
            if (stage >= MainCaseTruthStage.ExperimentTruth)
                foreach (var clue in MainCaseClueNames.PowerTruthClues) s.MainCase.AcquireClue(clue);
            while (s.MainCase.TryAdvanceStage()) { }
            Assert.AreEqual(stage, s.MainCase.CurrentStage);
            return s;
        }

        [Test]
        public void ExperimentTruthPlans_UseTheExactConfirmedMethodToolRelationshipOccupationDateAndObject()
        {
            var s = SessionAtStage(MainCaseTruthStage.ExperimentTruth, 31);
            var expected = new[]
            {
                new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.ParticipantOperatingRulesNotice,
                    RequireMurder = true, VictimOccupation = "학생", MethodId = "METHOD-03", ToolId = "TOOL-02",
                    RelationshipId = "REL-01", EarliestIncidentDate = "1984-10-19", HolderIsVictim = true,
                    DocumentHolderTag = "실제 실험 참가자·배포 안내문 보유자",
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Bedroom, "편지")
                },
                new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.StudentInfoAcquisitionMemo,
                    RequireMurder = true, VictimOccupation = "사무원", MethodId = "METHOD-06", ToolId = "TOOL-05",
                    RelationshipId = "REL-06", EarliestIncidentDate = "1984-10-01",
                    DocumentHolderTag = "학교 학생 정보 요청·전달 및 후보 선정 서류 접근자",
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "서류 더미")
                },
                new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.SurvivorHandoverRecord,
                    RequireMurder = true, VictimOccupation = "공무원", MethodId = "METHOD-02", ToolId = "TOOL-01",
                    RelationshipId = "REL-05", EarliestIncidentDate = "1984-11-20",
                    DocumentHolderTag = "제한 열람 기록·이전 회차 생존자 인계 기록 접근자",
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "서류 더미")
                }
            };

            int counter = 0;
            var pipeline = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(31));
            foreach (var plan in expected)
            {
                var actual = s.PlanMainCaseConstraints();
                Assert.IsNotNull(actual);
                Assert.AreEqual(plan.PlannedClueName, actual.PlannedClueName);
                Assert.AreEqual(plan.MethodId, actual.MethodId);
                Assert.AreEqual(plan.ToolId, actual.ToolId);
                Assert.AreEqual(plan.RelationshipId, actual.RelationshipId);
                Assert.AreEqual(plan.VictimOccupation, actual.VictimOccupation);
                Assert.AreEqual(plan.EarliestIncidentDate, actual.EarliestIncidentDate);
                Assert.AreEqual(plan.DocumentObjectId, actual.DocumentObjectId);
                Assert.AreEqual(plan.HolderIsVictim, actual.HolderIsVictim);
                Assert.AreEqual(plan.DocumentHolderTag, actual.DocumentHolderTag);

                var generated = pipeline.TryGenerate(() => $"experiment-{++counter}", out _, OrganizationConditionType.None, actual);
                Assert.IsNotNull(generated, $"{actual.PlannedClueName} 생성 실패");
                Assert.IsTrue(actual.IsSatisfiedBy(generated.Contract, generated.Npcs), actual.PlannedClueName);
                Assert.AreEqual(actual.MethodId, generated.Contract.Truth.MethodId);
                Assert.AreEqual(actual.ToolId, generated.Contract.Truth.ToolId);
                Assert.AreEqual(actual.RelationshipId, generated.Contract.Truth.RelationshipId);
                Assert.AreEqual(actual.VictimOccupation, generated.Npcs.Get(generated.Contract.Truth.VictimId).Occupation);
                Assert.GreaterOrEqual(DateTime.Parse(generated.Contract.IncidentDate), DateTime.Parse(actual.EarliestIncidentDate));
                Assert.AreEqual(actual.DocumentObjectId, generated.Contract.PlannedClueObjectId);
                Assert.AreEqual(actual.DocumentHolderTag, generated.Contract.DocumentHolderTag);
                if (actual.HolderIsVictim)
                    Assert.AreEqual(generated.Contract.Truth.VictimId, generated.Contract.DocumentHolderNpcId);
                else
                    Assert.IsTrue(generated.Npcs.Exists(generated.Contract.DocumentHolderNpcId));

                s.MainCase.AcquireClue(actual.PlannedClueName);
            }
            Assert.IsNull(s.PlanMainCaseConstraints());
        }

        [Test]
        public void PowerTruth_EachDocument_IsPlacedWithHolderDateAndObject()
        {
            var s = SessionAtStage(MainCaseTruthStage.PowerTruth, 5);
            int planned = 0;
            foreach (var name in MainCaseClueNames.PowerTruthClues)
            {
                var constraints = s.PlanMainCaseConstraints();
                Assert.IsNotNull(constraints, name);
                for (int seed = 1; seed <= 3; seed++)
                {
                    var built = Generate(constraints, seed);
                    Assert.IsNotNull(built, $"{constraints.PlannedClueName} seed {seed}");
                    var c = built.Contract;
                    Assert.IsTrue(constraints.IsSatisfiedBy(c, built.Npcs));
                    Assert.GreaterOrEqual(DateTime.Parse(c.IncidentDate), DateTime.Parse(constraints.EarliestIncidentDate), "사건은 문서가 존재할 수 있는 날 이후");
                    Assert.IsTrue(built.Npcs.Exists(c.DocumentHolderNpcId), "기존 의뢰 인물이 문서를 보유한다");
                    Assert.AreEqual(constraints.DocumentHolderTag, c.DocumentHolderTag);
                    Assert.AreEqual(constraints.DocumentObjectId, c.PlannedClueObjectId);
                }
                s.MainCase.AcquireClue(constraints.PlannedClueName);
                planned++;
            }
            Assert.AreEqual(8, planned);
            Assert.IsNull(s.PlanMainCaseConstraints(), "8개를 모두 얻으면 더 배치하지 않는다");
        }

        [Test]
        public void PowerTruth_Session_DocumentFoundOnlyInPlannedObject_AndSaved()
        {
            var s = SessionAtStage(MainCaseTruthStage.PowerTruth, 9);
            var offer = s.OfferContract();
            Assert.IsNotNull(offer);
            var c = offer.Contract;
            CollectionAssert.Contains(MainCaseClueNames.PowerTruthClues, c.PlannedMainCaseClue,
                "권력의 진실 자료는 개별 출현 조건이 확정되지 않았으므로 관리 번호 순서로 강제하지 않는다.");
            Assert.IsTrue(s.AcceptContract().Success);

            string room = CaseCatalog.ObjectRoom(c.PlannedClueObjectId);
            var other = s.GetInvestigationObjects().FirstOrDefault(o => o.ObjectId != c.PlannedClueObjectId && o.RoomId != room);
            if (other != null)
            {
                s.Occupancy.PlaceAt(s.PlayerId, other.RoomId);
                s.InvestigateObject(other.ObjectId);
                Assert.IsFalse(s.MainCase.HasAcquired(c.PlannedMainCaseClue), "다른 오브젝트에서는 나오지 않는다");
            }

            s.Occupancy.PlaceAt(s.PlayerId, room);
            Assert.IsTrue(s.GetInvestigationObjects().Any(o => o.ObjectId == c.PlannedClueObjectId), "배치 오브젝트는 항상 조사 대상");
            var result = s.InvestigateObject(c.PlannedClueObjectId);
            Assert.IsTrue(result.Success, result.Failure.ToString());
            CollectionAssert.Contains(result.NewMainCaseClues, c.PlannedMainCaseClue);
            Assert.IsTrue(s.MainCase.HasAcquired(c.PlannedMainCaseClue));

            var loaded = GameSession.Load(s.CaptureSave(), new GameSessionConfig { Random = new Random(1) });
            Assert.AreEqual(c.PlannedClueObjectId, loaded.CurrentContract.PlannedClueObjectId);
            Assert.AreEqual(c.DocumentHolderNpcId, loaded.CurrentContract.DocumentHolderNpcId);
            Assert.AreEqual(c.DocumentHolderTag, loaded.CurrentContract.DocumentHolderTag);
        }

        [Test]
        public void Systemic_RepeatedNumberList_PlannedAfterConditionalClues_HeldByVictim()
        {
            var s = SessionAtStage(MainCaseTruthStage.SystemicTruth, 4);
            foreach (var name in new[]
                     {
                         MainCaseClueNames.GovernmentDepartmentContactLog, MainCaseClueNames.ResultReportRecording,
                         MainCaseClueNames.ParticipantRosterDeliveryConfirmation, MainCaseClueNames.SeniorOfficialLetter,
                         MainCaseClueNames.NumberNoteLeftForLover
                     })
                s.MainCase.AcquireClue(name);
            var constraints = s.PlanMainCaseConstraints();
            Assert.AreEqual(MainCaseClueNames.RepeatedExperimentNumberList, constraints.PlannedClueName);
            var built = Generate(constraints, 2);
            Assert.IsNotNull(built);
            Assert.AreEqual(built.Contract.Truth.VictimId, built.Contract.DocumentHolderNpcId);
        }

        [Test]
        public void NoPlan_OutsideSystemicStage()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(3) });
            s.StartNewGame();
            Assert.AreEqual(MainCaseTruthStage.PersonalTruth, s.MainCase.CurrentStage);
            Assert.IsNull(s.PlanMainCaseConstraints());
        }
    }
}
