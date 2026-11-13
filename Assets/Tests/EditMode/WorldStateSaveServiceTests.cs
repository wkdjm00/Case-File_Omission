using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Evidence;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class WorldStateSaveServiceTests
    {
        private const string ContractId = "c1";

        private sealed class World
        {
            public readonly ClueRepository Clues = new ClueRepository();
            public readonly EvidenceManipulationService Manipulation;
            public readonly EvidenceManipulationOutcomeResolver Outcomes;
            public readonly EvidenceCapacityManager Capacity = new EvidenceCapacityManager();
            public readonly NpcRegistry Npcs = new NpcRegistry();
            public readonly NpcRelationshipStore Relationships = new NpcRelationshipStore();
            public readonly NpcInfoTransferService InfoTransfer;
            public readonly MeetingSession Meeting;
            public readonly List<ContractReportState> Reports = new List<ContractReportState>();
            public readonly List<CaseBoardState> CaseBoards = new List<CaseBoardState>();
            public readonly MainCaseProgressTracker MainCase = new MainCaseProgressTracker();
            public readonly PostReportBranching PostReport = new PostReportBranching();
            public readonly MainCaseFinalReportCoordinator FinalReport;
            public readonly RoomOccupancyTracker Occupancy = new RoomOccupancyTracker(HouseLayoutFactory.CreateStandardHouse());
            public readonly GameEventLog EventLog = new GameEventLog();

            public World(Random random = null, string meetingContractId = ContractId)
            {
                Manipulation = new EvidenceManipulationService(Clues, random ?? new Random(1));
                Outcomes = new EvidenceManipulationOutcomeResolver(Manipulation, random ?? new Random(1));
                InfoTransfer = new NpcInfoTransferService(new Random(1));
                Meeting = new MeetingSession(meetingContractId, Manipulation);
                FinalReport = new MainCaseFinalReportCoordinator(MainCase, PostReport, FinalReportAnswerConfig.CreateDefault());
            }

            public WorldStateTargets Targets => new WorldStateTargets
            {
                Clues = Clues,
                Manipulation = Manipulation,
                ManipulationOutcomes = Outcomes,
                Capacity = Capacity,
                Npcs = Npcs,
                Relationships = Relationships,
                InfoTransfer = InfoTransfer,
                Meeting = Meeting,
                Reports = Reports,
                CaseBoards = CaseBoards,
                MainCase = MainCase,
                FinalReport = FinalReport.Report,
                PostReport = PostReport,
                Occupancy = Occupancy,
                EventLog = EventLog
            };
        }

        private static ClueDefinition Clue(string id, bool identifies = false) => new ClueDefinition(id, ContractId)
        {
            Format = ClueFormat.Document,
            TruthStatus = ClueTruthStatus.PartiallyFactual,
            DisplayName = id + "-name",
            IdentifiesPerpetrator = identifies
        };

        /// <summary>저장 → JSON 문자열 → 불러오기까지 실제 경로를 그대로 거친다.</summary>
        private static World RoundTrip(World source, GameLogger logger = null, World target = null)
        {
            var data = new SaveGameData { World = WorldStateSaveService.Capture(source.Targets) };
            var loaded = SaveGameService.FromJson(SaveGameService.ToJson(data));

            target ??= new World();
            WorldStateSaveService.Restore(loaded.World, target.Targets, logger);
            return target;
        }

        [Test]
        public void Evidence_RoundTrip_PreservesGraphOwnershipAndManipulation()
        {
            var source = new World();
            source.Clues.Discover(Clue("a", identifies: true));
            source.Clues.Discover(Clue("b"));
            source.Clues.Discover(Clue("c"));
            source.Clues.Drop("b");
            source.Clues.GetRecord("a").MarkConfirmedByPolice("police-a");
            source.Manipulation.Distort("a", "player");
            source.Manipulation.Conceal("b", "player", new[] { "b", "c" });
            source.Capacity.TryAcquire("npc-1", HolderType.Npc, "c");
            source.Capacity.TryAcquire("npc-1", HolderType.Npc, "a");

            var target = RoundTrip(source);

            var defA = target.Clues.GetDefinition("a");
            Assert.AreEqual(ClueTruthStatus.PartiallyFactual, defA.TruthStatus);
            Assert.AreEqual("a-name", defA.DisplayName);
            Assert.IsTrue(defA.IdentifiesPerpetrator);
            Assert.AreEqual(source.Clues.GetRecord("a").DiscoveredAtUtc, target.Clues.GetRecord("a").DiscoveredAtUtc);
            Assert.AreEqual(ClueWorldState.DroppedInWorld, target.Clues.GetRecord("b").WorldState);
            CollectionAssert.Contains(target.Clues.GetRecord("a").ConfirmedByPoliceIds, "police-a");

            Assert.AreEqual(2, target.Manipulation.History.Count);
            Assert.AreEqual(ManipulationType.Conceal, target.Manipulation.History[1].Type);
            Assert.AreEqual("c", target.Manipulation.History[1].DisplayedClueId);

            CollectionAssert.AreEqual(new[] { "c", "a" }, target.Capacity.GetHeldClues("npc-1"), "획득 순서(가장 오래된 것 교체 기준)도 유지된다.");
        }

        [Test]
        public void CaseBoard_RoundTrip_RestoresPositionsDiscardsAndLinks_SkippingUnknownClues()
        {
            var source = new World();
            source.Clues.Discover(Clue("a"));
            source.Clues.Discover(Clue("b"));
            source.Clues.Discover(Clue("c"));
            var board = new CaseBoardState(ContractId, 77);
            board.SyncMaterials(new[] { "a", "b", "c" });
            board.Move("a", 0.25f, 0.75f);
            board.Discard("c");
            board.Connect("a", "b");
            board.Items.Add(new CaseBoardItem { ItemId = "ghost", X = 0.5f, Y = 0.1f });
            source.CaseBoards.Add(board);

            var logger = new GameLogger();
            var target = RoundTrip(source, logger);

            var restored = target.CaseBoards.Single();
            Assert.AreEqual(77, restored.Seed);
            Assert.AreEqual(0.25f, restored.Get("a").X, 1e-5);
            Assert.AreEqual(0.75f, restored.Get("a").Y, 1e-5);
            Assert.IsFalse(restored.Get("c").OnBoard, "폐기 상태 유지");
            Assert.IsTrue(restored.Links.Single().Connects("a", "b"));
            Assert.IsTrue(restored.IsMemoVisible);
            Assert.AreEqual(3, restored.Items.Count(i => i.BoardDecorationOnly), "장식 항목도 그대로 복원");
            Assert.IsNull(restored.Get("ghost"), "단서 저장소에 없는 항목은 건너뛴다.");
            Assert.IsTrue(logger.Entries.Any(e => e.ErrorCode == "SAVE_UNKNOWN_CLUE_ID"));
        }

        [Test]
        public void Evidence_FailedManipulationAttempts_RoundTrip()
        {
            var source = new World(new Random(3));
            source.Clues.Discover(Clue("a"));
            int guard = 0;
            while (source.Outcomes.FailedAttempts.Count == 0 && guard++ < 200)
                source.Outcomes.Attempt(ManipulationType.Distort, "a", "player", true, true, true, false);

            Assert.Greater(source.Outcomes.FailedAttempts.Count, 0);

            var target = RoundTrip(source);
            Assert.AreEqual(source.Outcomes.FailedAttempts.Count, target.Outcomes.FailedAttempts.Count);
            Assert.AreEqual(source.Manipulation.History.Count, target.Manipulation.History.Count);
        }

        [Test]
        public void Npc_RoundTrip_PreservesStateKnowledgeRelationsAndTransferLog()
        {
            var source = new World();
            var sender = new NpcState("n1", ContractId, NpcActualRole.Witness, NpcPersonality.BlindlyTrusting) { Occupation = "비서" };
            var receiver = new NpcState("n2", ContractId, NpcActualRole.Perpetrator, NpcPersonality.Jealous);
            source.Npcs.Add(sender);
            source.Npcs.Add(receiver);
            sender.LearnInfo("info-1", NpcJudgement.Suspect);
            receiver.MarkDeceased();
            sender.MarkCooperating();
            source.Relationships.SetSuspicion("n1", "n2", 30f);
            source.Relationships.SetTrust("n2", "n1", 12f);
            source.InfoTransfer.RestoreTransfer("n1", "n2", "info-1");

            var target = RoundTrip(source);

            var n1 = target.Npcs.Get("n1");
            var n2 = target.Npcs.Get("n2");
            Assert.AreEqual("비서", n1.Occupation);
            Assert.AreEqual(NpcPersonality.BlindlyTrusting, n1.Personality);
            Assert.IsTrue(n1.IsCooperatingWithPlayer);
            Assert.IsFalse(n2.IsAlive);
            Assert.AreEqual(NpcActualRole.Perpetrator, n2.ActualRole);
            Assert.IsTrue(n1.Knows("info-1"));
            Assert.AreEqual(NpcJudgement.Suspect, n1.Beliefs["info-1"]);

            Assert.AreEqual(30f, target.Relationships.GetSuspicion("n1", "n2"));
            Assert.AreEqual(0f, target.Relationships.GetSuspicion("n2", "n1"), "A→B와 B→A는 따로 저장된다.");
            Assert.AreEqual(12f, target.Relationships.GetTrust("n2", "n1"));

            var result = target.InfoTransfer.TryTransfer(n1, n2, "info-1", true, SuspicionLevel.None, false, false);
            Assert.AreEqual(InfoTransferOutcome.AlreadyTransferred, result.Outcome, "불러온 뒤에도 같은 정보를 다시 전달하지 않는다.");
        }

        [Test]
        public void Meeting_RoundTrip_ContinuesCountersAndBlocksDuplicateActions()
        {
            var source = new World();
            source.Meeting.RecordTurnSkip("skip-1", "police-a", "player");
            source.Meeting.RecordTurnSkip("skip-2", "police-a", "player");
            source.Meeting.RecordResponseFailed("fail-1", "police-a", "player");

            var target = RoundTrip(source);

            var nextSkip = target.Meeting.RecordTurnSkip("skip-3", "police-a", "player");
            Assert.AreEqual(3, nextSkip.ConsecutiveCountUsed, "연속 차례 넘기기 단계가 이어진다.");

            var nextFail = target.Meeting.RecordResponseFailed("fail-2", "police-a", "player");
            Assert.AreEqual(2, nextFail.ConsecutiveCountUsed);

            var duplicate = target.Meeting.RecordTurnSkip("skip-1", "police-a", "player");
            Assert.IsTrue(duplicate.WasDuplicateAction, "저장 전에 처리한 행동 eventId는 다시 처리되지 않는다.");
        }

        [Test]
        public void Meeting_EndedSession_StaysEnded()
        {
            var source = new World();
            source.Meeting.EndSession();

            var target = RoundTrip(source);
            Assert.IsFalse(target.Meeting.IsActive);
        }

        [Test]
        public void Meeting_ContractMismatch_IsSkippedAndLogged()
        {
            var source = new World();
            source.Meeting.RecordTurnSkip("skip-1", "police-a", "player");
            var logger = new GameLogger();

            var target = RoundTrip(source, logger, new World(meetingContractId: "other"));

            Assert.AreEqual(1, target.Meeting.RecordTurnSkip("skip-x", "police-a", "player").ConsecutiveCountUsed);
            Assert.IsTrue(logger.Entries.Any(e => e.ErrorCode == "SAVE_MEETING_CONTRACT_MISMATCH"));
        }

        [Test]
        public void Report_RoundTrip_PreservesContentLinksClaimAndSubmission()
        {
            var source = new World();
            source.Clues.Discover(Clue("a"));
            source.Npcs.Add(new NpcState("n1", ContractId, NpcActualRole.Perpetrator, NpcPersonality.Greedy));
            var report = new ContractReportState(ContractId);
            report.SetContent(ReportSection.Motive, "동기 서술");
            report.LinkClue(ReportSection.Motive, "a");
            report.SetClaimedNpcId(ReportSection.Perpetrator, "n1");
            report.MarkSubmitted();
            source.Reports.Add(report);

            var target = RoundTrip(source);

            var restored = target.Reports.Single(r => r.ContractId == ContractId);
            Assert.IsTrue(restored.IsSubmitted);
            Assert.AreEqual("동기 서술", restored.GetSection(ReportSection.Motive).Content);
            CollectionAssert.AreEqual(new[] { "a" }, restored.GetSection(ReportSection.Motive).LinkedClueIds);
            Assert.AreEqual("n1", restored.GetSection(ReportSection.Perpetrator).ClaimedNpcId);
        }

        [Test]
        public void MainCase_RoundTrip_PreservesAcquisitionTimingAndFinalReportProgress()
        {
            var source = new World();
            source.MainCase.RecordContractCompleted();
            source.MainCase.AcquireClue(MainCaseClueNames.NumberNoteLeftForLover);
            source.MainCase.RecordContractCompleted();
            source.MainCase.AcquireClue(MainCaseClueNames.NieceDiary);
            source.FinalReport.Report.TryFillBlank(4, MainCaseClueNames.NieceDiary); // AllOf 칸 일부만 수집

            var target = RoundTrip(source);

            Assert.AreEqual(2, target.MainCase.CompletedContractCount);
            Assert.AreEqual(1, target.MainCase.ContractsCompletedSinceAcquired(MainCaseClueNames.NumberNoteLeftForLover));
            Assert.IsFalse(target.FinalReport.Report.Blanks[4].IsFilled);
            CollectionAssert.Contains(target.FinalReport.Report.Blanks[4].CollectedClueNames, MainCaseClueNames.NieceDiary);

            target.MainCase.AcquireClue(MainCaseClueNames.NieceDeathReport);
            Assert.AreEqual(FillBlankOutcome.Filled, target.FinalReport.Report.TryFillBlank(4, MainCaseClueNames.NieceDeathReport),
                "불러온 뒤 나머지 단서로 칸을 이어서 완성할 수 있다.");
        }

        [Test]
        public void MainCase_LoadingCompletedReport_DoesNotRetriggerPhoneCall()
        {
            var source = new World();
            for (int i = 0; i < 14; i++)
                source.FinalReport.Report.RestoreProgress(i, true, new[] { "x" });
            source.PostReport.RestoreStages(PhoneCallStage.InformationRevealed, RebelRecruitmentStage.Offered);

            var target = RoundTrip(source);

            Assert.IsTrue(target.FinalReport.Report.IsComplete);
            Assert.AreEqual(PhoneCallStage.InformationRevealed, target.PostReport.PhoneStage, "수화기 질문이 다시 시작되지 않는다.");
            Assert.AreEqual(RebelRecruitmentStage.Offered, target.PostReport.RebelStage);
            Assert.AreEqual(GameEndingType.RebelRecruitAccepted, target.PostReport.DecideRebelRecruitment(true));
        }

        [Test]
        public void PositionsAndGlobalEvents_RoundTrip()
        {
            var source = new World();
            source.Occupancy.PlaceAt("npc-1", HouseLayoutFactory.Reception);
            source.EventLog.TryRecord(new GameEvent("evt-1", GameEventType.ContractResolved, ContractId, "p"));

            var target = RoundTrip(source);

            Assert.AreEqual(HouseLayoutFactory.Reception, target.Occupancy.GetRoom("npc-1"));
            Assert.IsTrue(target.EventLog.HasProcessed("evt-1"));
            Assert.IsFalse(target.EventLog.TryRecord(new GameEvent("evt-1", GameEventType.ContractResolved, ContractId, "p")));
        }

        [Test]
        public void BadReferences_AreSkippedAndLogged_WithoutCorruptingValidState()
        {
            var data = new WorldSaveData
            {
                Version = WorldSaveData.CurrentVersion,
                Evidence = new EvidenceSaveData
                {
                    Definitions = new[]
                    {
                        new ClueDefinitionDto { ClueId = "a", ContractId = ContractId, Format = ClueFormat.Document },
                        new ClueDefinitionDto { ClueId = "bad-enum", ContractId = ContractId, Format = (ClueFormat)99 }
                    },
                    Records = new[] { new ClueRecordDto { ClueId = "orphan" } },
                    Holdings = new[] { new HolderHoldingDto { HolderId = "npc-1", HolderType = HolderType.Npc, ClueIds = new[] { "a", "ghost" } } },
                    ManipulationHistory = new[] { new ManipulationRecordDto { OriginalClueId = "ghost", DisplayedClueId = "ghost" } }
                },
                Reports = new[]
                {
                    new ContractReportSaveData
                    {
                        ContractId = ContractId,
                        Sections = new[] { new ReportSectionDto { Section = ReportSection.Motive, LinkedClueIds = new[] { "a", "ghost" } } }
                    }
                },
                MainCase = new MainCaseExtendedSaveData
                {
                    HasFinalReport = true,
                    FinalReportBlanks = new[] { new FinalReportBlankDto { Index = 99, IsFilled = true } }
                },
                Positions = new[] { new OccupantPositionDto { OccupantId = "npc-1", RoomId = "garden" } }
            };

            var target = new World();
            var logger = new GameLogger();

            Assert.DoesNotThrow(() => WorldStateSaveService.Restore(data, target.Targets, logger));

            Assert.IsTrue(target.Clues.Exists("a"));
            Assert.IsFalse(target.Clues.Exists("bad-enum"));
            Assert.IsFalse(target.Clues.Exists("orphan"));
            CollectionAssert.AreEqual(new[] { "a" }, target.Capacity.GetHeldClues("npc-1"));
            Assert.AreEqual(0, target.Manipulation.History.Count);
            CollectionAssert.AreEqual(new[] { "a" }, target.Reports.Single().GetSection(ReportSection.Motive).LinkedClueIds);
            Assert.IsFalse(target.FinalReport.Report.IsComplete);
            Assert.IsFalse(target.Occupancy.HasPosition("npc-1"), "실외 등 존재하지 않는 방 위치는 복원하지 않는다.");

            var codes = logger.Entries.Select(e => e.ErrorCode).ToList();
            CollectionAssert.Contains(codes, "SAVE_INVALID_ENUM");
            CollectionAssert.Contains(codes, "SAVE_UNKNOWN_CLUE_ID");
            CollectionAssert.Contains(codes, "SAVE_INVALID_MANIPULATION");
            CollectionAssert.Contains(codes, "SAVE_INVALID_REPORT_BLANK");
            CollectionAssert.Contains(codes, "SAVE_UNKNOWN_ROOM_ID");
        }

        [Test]
        public void HoldingsOverLimit_AreTruncatedToLimit()
        {
            var source = new World();
            foreach (var id in new[] { "a", "b", "c", "d" })
                source.Clues.Discover(Clue(id));

            var data = WorldStateSaveService.Capture(source.Targets);
            data.Evidence.Holdings = new[] { new HolderHoldingDto { HolderId = "npc-1", HolderType = HolderType.Npc, ClueIds = new[] { "a", "b", "c", "d" } } };

            var target = new World();
            var logger = new GameLogger();
            WorldStateSaveService.Restore(data, target.Targets, logger);

            Assert.AreEqual(ClueRepository.NpcDirectHoldLimit, target.Capacity.GetHeldClues("npc-1").Count);
            Assert.IsTrue(logger.Entries.Any(e => e.ErrorCode == "SAVE_HOLDING_OVER_LIMIT"));
        }

        [Test]
        public void OldSaveWithoutWorld_LeavesFreshState_AndLogs()
        {
            var oldJson = "{\"ContractId\":\"c1\",\"Phase\":1,\"CurrentDay\":1}";
            var loaded = SaveGameService.FromJson(oldJson);
            var target = new World();
            var logger = new GameLogger();

            Assert.DoesNotThrow(() => WorldStateSaveService.Restore(loaded.World, target.Targets, logger));
            Assert.AreEqual(0, target.Clues.AllDefinitions.Count());
            Assert.IsTrue(logger.Entries.Any(e => e.ErrorCode == "SAVE_WORLD_MISSING"));
        }

        [Test]
        public void FileStore_WriteThenRead_RoundTrips_AndCorruptFileIsRejected()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cfo-save-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "slot1.json");
            try
            {
                var source = new World();
                source.Clues.Discover(Clue("a"));
                var data = new SaveGameData { ContractId = ContractId, World = WorldStateSaveService.Capture(source.Targets) };

                SaveGameFileStore.Write(path, data);
                SaveGameFileStore.Write(path, data); // 덮어쓰기 경로

                Assert.IsTrue(SaveGameFileStore.TryRead(path, out var loaded));
                var target = new World();
                WorldStateSaveService.Restore(loaded.World, target.Targets);
                Assert.IsTrue(target.Clues.Exists("a"));

                File.WriteAllText(path, "{ not json");
                var logger = new GameLogger();
                Assert.IsFalse(SaveGameFileStore.TryRead(path, out var corrupt, logger));
                Assert.IsNull(corrupt);
                Assert.AreEqual("SAVE_FILE_CORRUPT", logger.Entries.Single().ErrorCode);

                Assert.IsFalse(SaveGameFileStore.TryRead(Path.Combine(dir, "missing.json"), out _));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
