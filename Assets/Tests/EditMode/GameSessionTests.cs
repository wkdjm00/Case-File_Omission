using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class GameSessionTests
    {
        private static GameSession NewSession(int seed = 7) =>
            new GameSession(new GameSessionConfig { Random = new Random(seed) });

        private static GameSession StartContract(int seed = 7)
        {
            var session = NewSession(seed);
            session.StartNewGame();
            session.CompletePrologue();
            session.OfferContract();
            Assert.IsTrue(session.AcceptContract().Success);
            return session;
        }

        private static Dictionary<string, bool> AllVotes(GameSession s, bool accept) =>
            s.MeetingFlow.ParticipantIds.Where(id => id != s.PlayerId).ToDictionary(id => id, _ => accept);

        [Test]
        public void NewGame_GivesDiaryAtStart_AndDeathReportAfterPrologue()
        {
            var s = NewSession();
            s.StartNewGame();
            Assert.IsTrue(s.MainCase.HasAcquired(MainCaseClueNames.NieceDiary));
            Assert.IsFalse(s.MainCase.HasAcquired(MainCaseClueNames.NieceDeathReport));

            s.CompletePrologue();
            Assert.IsTrue(s.MainCase.HasAcquired(MainCaseClueNames.NieceDeathReport));
        }

        [Test]
        public void Offer_RevealsCondition_Accept_LocksConditionAndAssignsOneHandler()
        {
            var s = NewSession();
            var offer = s.OfferContract();
            Assert.AreEqual(GamePhase.Idle, s.State.CurrentPhase);
            Assert.AreSame(offer, s.OfferContract(), "수락 전에는 같은 제안을 유지한다.");

            Assert.IsTrue(s.AcceptContract().Success);

            Assert.AreEqual(GamePhase.Investigation, s.State.CurrentPhase);
            Assert.IsTrue(s.CurrentContract.OrgCondition.IsLocked);
            CollectionAssert.Contains(PoliceRoster.AllPoliceIds.ToList(), s.HandlerPoliceId);
            Assert.AreEqual(GameStateController.TimeSlotsPerBlock, s.State.RemainingTimeSlots);
            Assert.Throws<InvalidOperationException>(() => s.CurrentContract.OrgCondition.Type = OrganizationConditionType.KeepUnsolved);
        }

        [Test]
        public void Investigation_SpendsTimeSlots_AndBlockEndsWhenBudgetRunsOut()
        {
            var s = StartContract();
            var first = s.InvestigateRoom();
            Assert.IsTrue(first.Success);
            Assert.AreEqual(11, s.State.RemainingTimeSlots);
            Assert.IsTrue(first.NewClueIds.Count > 0, "현장 조사로 현장 자료를 얻는다.");

            var detailed = s.InvestigateRoom(detailedSearch: true);
            Assert.AreEqual(9, s.State.RemainingTimeSlots, "상세 수색은 2칸.");
            Assert.AreEqual(0, detailed.NewClueIds.Count, "이미 얻은 자료는 다시 얻지 않는다.");

            while (s.State.RemainingTimeSlots > 0)
                Assert.IsTrue(s.InvestigateRoom().Success);

            Assert.IsTrue(s.State.IsTimeBlockEnded);
            Assert.AreEqual(GameActionFailure.TimeBlockEnded, s.InvestigateRoom().Failure);

            Assert.IsTrue(s.AdvanceToNextTimeBlock().Success);
            Assert.AreEqual(TimeBlock.Evening, s.State.CurrentTimeBlock);
            Assert.AreEqual(12, s.State.RemainingTimeSlots);
        }

        [Test]
        public void MoveToUnconnectedRoom_Fails_WithoutSpendingTime()
        {
            var s = StartContract();
            string far = s.Rooms.AllRoomIds.First(r => r != s.PlayerRoom && !s.Rooms.AreConnected(s.PlayerRoom, r));

            Assert.AreEqual(GameActionFailure.RoomNotConnected, s.MovePlayer(far).Failure);
            Assert.AreEqual(12, s.State.RemainingTimeSlots);
        }

        [Test]
        public void FullContract_MeetingConclusionAdopted_SettlesAndReturnsToIdle()
        {
            var s = StartContract();
            s.InvestigateRoom();
            Assert.IsTrue(s.EndInvestigationEarly().Success);
            Assert.IsTrue(s.StartMeeting().Success);
            Assert.AreEqual(GamePhase.Meeting, s.State.CurrentPhase);
            Assert.LessOrEqual(s.MeetingFlow.PlayerHand.Count, 5);

            var contract = s.CurrentContract;
            float trustBefore = s.Organization.Trust;
            var result = s.ProposeConclusion(contract.Truth.PerpetratorId, AllVotes(s, true));

            Assert.AreEqual(ConclusionOutcome.Adopted, result.Outcome);
            Assert.AreEqual(GamePhase.Idle, s.State.CurrentPhase);
            Assert.IsNull(s.CurrentContract);
            Assert.AreSame(contract, s.LastContract);
            Assert.IsTrue(contract.OfficialConclusion.IsFinalized);
            Assert.AreEqual(ContractOutcome.Resolved, contract.OfficialConclusion.Outcome);
            Assert.AreEqual(1, s.MainCase.CompletedContractCount);
            if (!contract.OrgCondition.HasCondition)
                Assert.AreEqual(trustBefore, s.Organization.Trust, "조건 없는 의뢰는 신뢰도 변화가 없다.");
            else
                Assert.IsTrue(contract.OrgCondition.Achieved.HasValue, "조건이 있으면 종료 시 자동 판정한다.");
        }

        [Test]
        public void MeetingWithoutMajority_AfterTwoVerifications_EndsUnsolved()
        {
            var s = StartContract();
            s.EndInvestigationEarly();
            s.StartMeeting();
            var contract = s.CurrentContract;

            Assert.AreEqual(ConclusionOutcome.NeedsVerification, s.ProposeConclusion("x", AllVotes(s, false)).Outcome);
            Assert.AreEqual(ConclusionOutcome.NeedsVerification, s.ProposeConclusion("x", AllVotes(s, false)).Outcome);
            Assert.AreEqual(ConclusionOutcome.Unsolved, s.ProposeConclusion("x", AllVotes(s, false)).Outcome);

            Assert.AreEqual(ContractOutcome.Unsolved, contract.OfficialConclusion.Outcome);
            Assert.AreEqual(GamePhase.Idle, s.State.CurrentPhase);
        }

        [Test]
        public void LeaveMeeting_ReturnsToInvestigation_InNextTimeBlock()
        {
            var s = StartContract();
            s.EndInvestigationEarly();
            s.StartMeeting();

            Assert.IsTrue(s.LeaveMeeting().Success);

            Assert.AreEqual(GamePhase.Investigation, s.State.CurrentPhase);
            Assert.AreEqual(TimeBlock.Evening, s.State.CurrentTimeBlock);
            Assert.AreEqual(12, s.State.RemainingTimeSlots);
        }

        [Test]
        public void SkippingTurnWithoutCards_RaisesHandlerSuspicion()
        {
            var s = StartContract();
            s.EndInvestigationEarly();
            s.StartMeeting();
            string handler = s.HandlerPoliceId;
            float before = s.PoliceSuspicion.GetSuspicion(handler, s.PlayerId);

            Assert.IsTrue(s.EndPlayerTurn().Success);

            Assert.AreEqual(before + 5f, s.PoliceSuspicion.GetSuspicion(handler, s.PlayerId), 0.001f, "첫 차례 넘기기 +5.");
            Assert.AreEqual(s.PlayerId, s.MeetingFlow.CurrentTurnId);
        }

        [Test]
        public void AbandonContract_CostsTwoTrust()
        {
            var s = StartContract();
            Assert.IsTrue(s.AbandonContract().Success);
            Assert.AreEqual(38f, s.Organization.Trust);
            Assert.AreEqual(GamePhase.Idle, s.State.CurrentPhase);
        }

        [Test]
        public void ShootingHandlerPolice_EndsContract_AsHandlerDeceased()
        {
            var s = StartContract(seed: 3);
            string handler = s.HandlerPoliceId;
            var contract = s.CurrentContract;

            for (int i = 0; i < 3 && s.CurrentContract != null; i++)
                s.FireAt(handler, isCloseRange: true, hasUnstableAim: false, targetMovingFast: false, pathPartiallyBlocked: false);

            Assume.That(s.PoliceLife.IsAlive(handler), Is.False, "이 시드에서는 3발 안에 명중해야 한다.");
            Assert.AreEqual(ContractOutcome.HandlerPoliceDeceased, contract.OfficialConclusion.Outcome);
            Assert.AreEqual(GamePhase.Idle, s.State.CurrentPhase);
            Assert.IsNotNull(s.Campaign.NextHandlerPoliceId);
            Assert.AreEqual(15f, s.PoliceSuspicion.GetSuspicion(s.Campaign.NextHandlerPoliceId, s.PlayerId), 0.001f,
                "다음 담당 경찰은 +5 +10 = +15.");
        }

        [Test]
        public void HandlerSuspicionAtMax_TriggersArrest_AndBlocksActions()
        {
            var s = StartContract();
            s.PoliceSuspicion.ApplyBatch(s.HandlerPoliceId, s.PlayerId, new[] { 100f });

            s.CheckEndings();

            Assert.IsTrue(s.IsGameOver);
            Assert.AreEqual(GameEndingType.Arrest, s.Endings.GameOverEnding);
            Assert.AreEqual(GameActionFailure.GameOver, s.InvestigateRoom().Failure);
        }

        [Test]
        public void SaveAndLoad_MidInvestigation_RestoresContractCluesAndTime()
        {
            var s = StartContract();
            var found = s.InvestigateRoom().NewClueIds;
            var contract = s.CurrentContract;

            var json = SaveLoad.SaveGameService.ToJson(s.CaptureSave());
            var loaded = GameSession.Load(SaveLoad.SaveGameService.FromJson(json), new GameSessionConfig { Random = new Random(1) });

            Assert.AreEqual(GamePhase.Investigation, loaded.State.CurrentPhase);
            Assert.AreEqual(11, loaded.State.RemainingTimeSlots);
            Assert.IsNotNull(loaded.CurrentContract);
            Assert.AreEqual(contract.Truth.PerpetratorId, loaded.CurrentContract.Truth.PerpetratorId);
            Assert.AreEqual(contract.HandlerPoliceId, loaded.CurrentContract.HandlerPoliceId);
            Assert.AreEqual(contract.OrgCondition.Type, loaded.CurrentContract.OrgCondition.Type);
            Assert.IsTrue(loaded.CurrentContract.OrgCondition.IsLocked);
            foreach (var clueId in found)
                Assert.IsTrue(loaded.Clues.Exists(clueId));
            Assert.AreEqual(s.Materials.Count, loaded.Materials.Count, "아직 얻지 않은 자료도 되살아나야 한다.");
            Assert.AreEqual(s.PlayerRoom, loaded.PlayerRoom);

            Assert.IsTrue(loaded.InvestigateRoom().Success, "불러온 뒤에도 조사를 이어 갈 수 있다.");
        }

        [Test]
        public void SecondContract_AvoidsImmediateConditionRepeat_WhenAlternativeExists()
        {
            var s = StartContract(seed: 11);
            s.AbandonContract();
            var previous = s.Campaign.PreviousConditionType;

            var next = s.OfferContract().Contract;
            if (previous != OrganizationConditionType.None && next.OrgCondition.HasCondition)
                Assert.AreNotEqual(previous, next.OrgCondition.Type);
        }

        private static void CompleteFinalReportForTest(GameSession s)
        {
            var report = s.FinalReport.Report;
            for (int i = 0; i < report.Blanks.Count; i++)
                report.RestoreProgress(i, true, new string[0]);
        }

        [Test]
        public void PhysicalManipulation_LockedUntilMainFinalReportAndFirstMeeting()
        {
            // 사용자 확정(2026-11-10 C1): 메인 사건 최종 보고서(타자기) 완성 + 첫 회의 종료 이후부터.
            var s = StartContract();
            string clue = s.InvestigateRoom().NewClueIds.First();

            Assert.IsFalse(s.IsPhysicalManipulationUnlocked);
            Assert.AreEqual(Evidence.ManipulationOutcome.Locked, s.AttemptPhysicalManipulation(clue, Evidence.ManipulationType.Distort).Outcome);

            // 의뢰 보고서를 제출해도 풀리지 않는다.
            foreach (var section in Reports.ReportRequirementResolver.GetRequiredSections(s.CurrentContract))
                s.Report.LinkClue(section, clue);
            s.Report.SetClaimedNpcId(Reports.ReportSection.Perpetrator, s.CurrentContract.Truth.PerpetratorId);
            Assert.AreEqual(Reports.ReportSubmissionOutcome.Submitted, s.SubmitReport().Outcome);
            Assert.IsFalse(s.IsPhysicalManipulationUnlocked, "의뢰 보고서 제출은 조건이 아니다.");

            CompleteFinalReportForTest(s);
            Assert.IsFalse(s.IsPhysicalManipulationUnlocked, "첫 회의가 끝나기 전에는 잠겨 있다.");

            s.EndInvestigationEarly();
            s.StartMeeting();
            s.LeaveMeeting();
            Assert.IsTrue(s.IsPhysicalManipulationUnlocked);

            var result = s.AttemptPhysicalManipulation(clue, Evidence.ManipulationType.Distort, targetUnawareOfFacts: false);
            Assert.AreNotEqual(Evidence.ManipulationOutcome.Locked, result.Outcome);
            Assert.GreaterOrEqual(result.ProbabilityUsed, 5f);
            Assert.LessOrEqual(result.ProbabilityUsed, 70f, "기본 70%에서 감소 보정만 붙을 수 있는 조건이다.");
        }

        [Test]
        public void WrongConclusion_IsNotBlocked_AndBecomesOfficialConclusion()
        {
            // 사용자 확정(2026-11-10 C4): 진실과 다른 결론도 제출·채택될 수 있다.
            var s = StartContract();
            var contract = s.CurrentContract;
            string clue = s.InvestigateRoom().NewClueIds.First();
            string innocent = s.Npcs.GetByContract(contract.ContractId).First(n => n.NpcId != contract.Truth.PerpetratorId).NpcId;

            foreach (var section in Reports.ReportRequirementResolver.GetRequiredSections(contract))
                s.Report.LinkClue(section, clue);
            s.Report.SetClaimedNpcId(Reports.ReportSection.Perpetrator, innocent);
            Assert.AreEqual(Reports.ReportSubmissionOutcome.Submitted, s.SubmitReport().Outcome);

            s.EndInvestigationEarly();
            s.StartMeeting();
            var conclusion = s.ProposeConclusion(null, AllVotes(s, true));

            Assert.AreEqual(ConclusionOutcome.Adopted, conclusion.Outcome);
            Assert.AreEqual(innocent, contract.OfficialConclusion.OfficiallyAccusedNpcId);
            Assert.AreNotEqual(contract.Truth.PerpetratorId, contract.OfficialConclusion.OfficiallyAccusedNpcId, "실제 진실은 바뀌지 않는다.");
        }

        [TestCase(TimeBlock.Morning)]
        [TestCase(TimeBlock.Evening)]
        public void StartTimeBlock_FollowsContractData_NotIncidentTimeline(TimeBlock configured)
        {
            // 사용자 확정(2026-11-10 C5): 발견 시각으로 조사 시작 단계를 정하지 않는다.
            for (int seed = 0; seed < 10; seed++)
            {
                var config = ContractGenerationConfig.CreateDefault();
                config.DefaultStartTimeBlock = configured;
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed), ContractConfig = config });
                var offer = s.OfferContract();
                s.AcceptContract();

                Assert.AreEqual(configured, s.State.CurrentTimeBlock);
                Assert.AreEqual(10, offer.Timeline.SlotMinutes, "사건 시간 정밀도는 10분 단위.");
            }
        }

        private static void ReachDesignerStage(GameSession s)
        {
            foreach (var clue in MainCaseClueNames.PersonalTruthClues
                         .Concat(MainCaseClueNames.SystemicTruthClues)
                         .Concat(MainCaseClueNames.PowerTruthClues)
                         .Concat(MainCaseClueNames.ExperimentTruthClues))
                s.MainCase.AcquireClue(clue);
            while (s.MainCase.TryAdvanceStage()) { }
            Assert.AreEqual(MainCaseTruthStage.DesignerTruth, s.MainCase.CurrentStage);
        }

        private static void MovePlayerTo(GameSession s, string room)
        {
            if (s.PlayerRoom == room)
                return;
            foreach (var step in s.Rooms.FindPath(s.PlayerRoom, room))
            {
                if (step != s.PlayerRoom)
                    Assert.IsTrue(s.MovePlayer(step).Success);
            }
            Assert.AreEqual(room, s.PlayerRoom);
        }

        [Test]
        public void RebelMemo_NotPlacedBeforeDesignerStage()
        {
            var s = StartContract();
            Assert.IsNull(s.Campaign.RebelMemoRoomId);
            Assert.IsNull(s.Campaign.RebelContactNpcId);
        }

        [Test]
        public void RebelMemo_PlacedInContactRoom_AcquiredByInvestigatingThatRoom()
        {
            var s = NewSession();
            ReachDesignerStage(s);
            s.OfferContract();
            s.AcceptContract();

            string contact = s.Campaign.RebelContactNpcId;
            string room = s.Campaign.RebelMemoRoomId;
            Assert.IsNotNull(contact);
            Assert.IsNotNull(room);
            var contactNpc = s.Npcs.Get(contact);
            Assert.AreNotEqual(NpcActualRole.Perpetrator, contactNpc.ActualRole, "가해자는 연락책으로 정하지 않는다.");
            Assert.IsFalse(s.Materials.Any(m => m.MaterialId == GameSession.RebelMemoClueId), "의뢰 보고서 근거 자료 목록에 넣지 않는다.");

            MovePlayerTo(s, room);
            var result = s.InvestigateRoom();

            CollectionAssert.Contains(result.NewMainCaseClues, MainCaseClueNames.RebelInternalCircularMemo);
            Assert.IsTrue(s.MainCase.HasAcquired(MainCaseClueNames.RebelInternalCircularMemo));
            Assert.AreEqual(MainCaseTruthStage.DesignerTruth, s.MainCase.CurrentStage, "필수 단서가 아니므로 단계는 그대로다.");
        }

        [Test]
        public void RebelMemo_NotObtainedByTalkingToContact()
        {
            // 연락책이 살아 있는(대화 가능한) 의뢰를 찾아, 그 방으로 이동해 대화만 한다.
            for (int seed = 0; seed < 50; seed++)
            {
                var s = NewSession(seed);
                ReachDesignerStage(s);
                s.OfferContract();
                s.AcceptContract();
                string contact = s.Campaign.RebelContactNpcId;
                if (contact == null || !s.Npcs.Get(contact).IsAlive || !s.Occupancy.HasPosition(contact))
                    continue;

                MovePlayerTo(s, s.Occupancy.GetRoom(contact));
                Assert.IsTrue(s.TalkTo(contact).Success);
                Assert.IsFalse(s.MainCase.HasAcquired(MainCaseClueNames.RebelInternalCircularMemo), "대화로는 얻지 않는다.");
                return;
            }

            Assert.Fail("50개 시드 안에 살아 있는 연락책이 있는 의뢰가 없다.");
        }

        [Test]
        public void RebelMemo_MissedThenReplacedNextContract_AndNotAfterAcquired()
        {
            var s = NewSession();
            ReachDesignerStage(s);
            s.OfferContract();
            s.AcceptContract();
            Assert.IsNotNull(s.Campaign.RebelMemoRoomId);

            s.AbandonContract();
            Assert.IsNull(s.Campaign.RebelMemoRoomId);

            s.OfferContract();
            s.AcceptContract();
            Assert.IsNotNull(s.Campaign.RebelMemoRoomId, "확보할 때까지 의뢰마다 다시 놓인다.");

            MovePlayerTo(s, s.Campaign.RebelMemoRoomId);
            s.InvestigateRoom();
            s.AbandonContract();

            s.OfferContract();
            s.AcceptContract();
            Assert.IsNull(s.Campaign.RebelMemoRoomId, "한 번 확보하면 다시 등장하지 않는다.");
        }

        [Test]
        public void RebelMemo_Placement_SurvivesSaveLoad()
        {
            var s = NewSession();
            ReachDesignerStage(s);
            s.OfferContract();
            s.AcceptContract();

            var json = SaveLoad.SaveGameService.ToJson(s.CaptureSave());
            var loaded = GameSession.Load(SaveLoad.SaveGameService.FromJson(json), new GameSessionConfig { Random = new Random(1) });

            Assert.AreEqual(s.Campaign.RebelContactNpcId, loaded.Campaign.RebelContactNpcId);
            Assert.AreEqual(s.Campaign.RebelMemoRoomId, loaded.Campaign.RebelMemoRoomId);
        }

        [Test]
        public void MainStoryEnding_ThenContractsContinue()
        {
            var s = NewSession();
            s.Endings.TryExecute(GameEndingType.BossHiddenEnding);

            s.OfferContract();
            Assert.IsTrue(s.AcceptContract().Success, "10 확정: 메인 서사 엔딩 뒤에도 의뢰가 계속된다.");
        }
    }
}
