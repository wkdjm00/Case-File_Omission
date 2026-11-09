using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class NpcBehaviorPlannerTests
    {
        private const string Contract = "c1";

        private sealed class FixedRandom : Random
        {
            private readonly double _value;
            public FixedRandom(double value) { _value = value; }
            public override double NextDouble() => _value;
        }

        private sealed class ListSource : INpcBehaviorCandidateSource
        {
            public readonly List<NpcBehaviorCandidate> Candidates = new List<NpcBehaviorCandidate>();
            public IEnumerable<NpcBehaviorCandidate> GetCandidates(NpcState actor, string currentRoomId, int currentMinute) => Candidates;
        }

        private sealed class Scene
        {
            public RoomGraph Rooms = HouseLayoutFactory.CreateStandardHouse();
            public NpcRegistry Registry = new NpcRegistry();
            public RoomOccupancyTracker Occupancy;
            public NpcInfoTransferService Transfer = new NpcInfoTransferService(new FixedRandom(0.0));
            public NpcRelationshipStore Relationships = new NpcRelationshipStore();
            public ClueRepository Clues = new ClueRepository();
            public EvidenceCapacityManager Capacity = new EvidenceCapacityManager();
            public NpcBehaviorPlanner Planner;

            public Scene(RoomGraph rooms = null, INpcBehaviorPolicy policy = null)
            {
                if (rooms != null) Rooms = rooms;
                Occupancy = new RoomOccupancyTracker(Rooms);
                Planner = new NpcBehaviorPlanner(Registry, Occupancy, Rooms, Transfer, Relationships, Clues, Capacity, policy);
            }

            public NpcState AddNpc(string id, string room, NpcPersonality personality = NpcPersonality.Greedy,
                NpcActualRole role = NpcActualRole.CaseRelatedA)
            {
                var npc = new NpcState(id, Contract, role, personality);
                Registry.Add(npc);
                if (room != null) Occupancy.PlaceAt(id, room);
                return npc;
            }

            public void DropClue(string clueId, string room)
            {
                Clues.Discover(new ClueDefinition(clueId, Contract));
                Clues.Drop(clueId);
                Occupancy.PlaceAt(clueId, room);
            }
        }

        [Test]
        public void NothingToDo_WaitsInPlace()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);

            var r = s.Planner.Act("t1", "npc-1", 0);

            Assert.AreEqual(NpcBehaviorStatus.Executed, r.Status);
            Assert.AreEqual(NpcBehaviorKind.Wait, r.Kind, "07 확정: 할 일이 없으면 대기한다.");
            Assert.AreEqual(HouseLayoutFactory.LivingRoom, s.Occupancy.GetRoom("npc-1"));
        }

        [Test]
        public void DeceasedNpc_DoesNotAct()
        {
            var s = new Scene();
            var npc = s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            npc.LearnInfo("info-1", NpcJudgement.Believe);
            s.AddNpc("npc-2", HouseLayoutFactory.LivingRoom);
            npc.MarkDeceased();

            var r = s.Planner.Act("t1", "npc-1", 0);

            Assert.AreEqual(NpcBehaviorStatus.NotAlive, r.Status);
            Assert.IsFalse(s.Registry.Get("npc-2").Knows("info-1"));
        }

        [Test]
        public void SameRoomPartner_SharesKnownInfo_AndPartnerTurnIsConsumed()
        {
            var s = new Scene();
            var sender = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom);
            sender.LearnInfo("info-1", NpcJudgement.Believe);
            var receiver = s.AddNpc("npc-b", HouseLayoutFactory.LivingRoom);

            var r = s.Planner.Act("t1", "npc-a", 0);

            Assert.AreEqual(NpcBehaviorKind.ShareInfo, r.Kind);
            Assert.AreEqual("npc-b", r.TargetId);
            Assert.AreEqual(InfoTransferOutcome.Succeeded, r.TransferResult.Outcome);
            Assert.IsTrue(receiver.Knows("info-1"));
            Assert.AreEqual(NpcJudgement.Withhold, receiver.Beliefs["info-1"], "전달받았다고 사실로 믿지 않는다.");

            Assert.AreEqual(NpcBehaviorStatus.DuplicateTurn, s.Planner.Act("t1", "npc-b", 0).Status,
                "대화에 참여한 상대는 같은 턴에 다시 행동하지 않는다.");
        }

        [Test]
        public void SameTurnTwice_SecondIsDuplicate()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);

            s.Planner.Act("t1", "npc-1", 0);
            Assert.AreEqual(NpcBehaviorStatus.DuplicateTurn, s.Planner.Act("t1", "npc-1", 0).Status);
            Assert.AreEqual(NpcBehaviorStatus.Executed, s.Planner.Act("t2", "npc-1", 0).Status);
        }

        [Test]
        public void AlreadyTransferredInfo_IsNotOfferedAgain()
        {
            var s = new Scene();
            var sender = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom);
            sender.LearnInfo("info-1", NpcJudgement.Believe);
            s.AddNpc("npc-b", HouseLayoutFactory.LivingRoom);

            s.Planner.Act("t1", "npc-a", 0);
            var second = s.Planner.Act("t2", "npc-a", 0);

            Assert.AreEqual(NpcBehaviorKind.Wait, second.Kind);
        }

        [Test]
        public void PartnerInAnotherRoom_IsNotContacted()
        {
            var s = new Scene();
            var sender = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom);
            sender.LearnInfo("info-1", NpcJudgement.Believe);
            var other = s.AddNpc("npc-b", HouseLayoutFactory.Bedroom);

            var r = s.Planner.Act("t1", "npc-a", 0);

            Assert.AreEqual(NpcBehaviorKind.Wait, r.Kind);
            Assert.IsFalse(other.Knows("info-1"));
        }

        [Test]
        public void DeadPartner_IsNotContacted()
        {
            var s = new Scene();
            var sender = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom);
            sender.LearnInfo("info-1", NpcJudgement.Believe);
            var victim = s.AddNpc("npc-b", HouseLayoutFactory.LivingRoom, role: NpcActualRole.Victim);
            victim.MarkDeceased();

            Assert.AreEqual(NpcBehaviorKind.Wait, s.Planner.Act("t1", "npc-a", 0).Kind);
            Assert.IsFalse(victim.Knows("info-1"));
        }

        [Test]
        public void Schedule_MovesOneAdjacentRoomPerTurn_ThenWaits()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.Kitchen);
            var schedule = new NpcSchedule("npc-1");
            Assert.AreEqual(NpcScheduleAddResult.Added, schedule.TryAdd(0, 60, HouseLayoutFactory.Bedroom));
            s.Planner.SetSchedule(schedule);

            var visited = new List<string>();
            for (int turn = 0; turn < 3; turn++)
            {
                var r = s.Planner.Act("t" + turn, "npc-1", turn * 10);
                Assert.AreEqual(NpcBehaviorKind.MoveBySchedule, r.Kind);
                visited.Add(s.Occupancy.GetRoom("npc-1"));
            }

            CollectionAssert.AreEqual(
                new[] { HouseLayoutFactory.LivingRoom, HouseLayoutFactory.Hallway, HouseLayoutFactory.Bedroom }, visited);
            Assert.AreEqual(NpcBehaviorKind.Wait, s.Planner.Act("t3", "npc-1", 30).Kind);
        }

        [Test]
        public void Schedule_OutsideAnyEntry_Waits()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.Kitchen);
            var schedule = new NpcSchedule("npc-1");
            schedule.TryAdd(60, 120, HouseLayoutFactory.Bedroom);
            s.Planner.SetSchedule(schedule);

            Assert.AreEqual(NpcBehaviorKind.Wait, s.Planner.Act("t1", "npc-1", 0).Kind);
        }

        [Test]
        public void Schedule_ConflictingOrInvalidEntries_AreRejected()
        {
            var schedule = new NpcSchedule("npc-1");
            Assert.AreEqual(NpcScheduleAddResult.Added, schedule.TryAdd(0, 30, HouseLayoutFactory.Kitchen));
            Assert.AreEqual(NpcScheduleAddResult.ConflictsWithExisting, schedule.TryAdd(20, 40, HouseLayoutFactory.Bedroom));
            Assert.AreEqual(NpcScheduleAddResult.Added, schedule.TryAdd(30, 40, HouseLayoutFactory.Bedroom), "끝 시각은 포함하지 않는다.");
            Assert.AreEqual(NpcScheduleAddResult.InvalidRange, schedule.TryAdd(50, 50, HouseLayoutFactory.Bedroom));
            Assert.AreEqual(NpcScheduleAddResult.MissingRoom, schedule.TryAdd(60, 70, null));
            Assert.AreEqual(HouseLayoutFactory.Bedroom, schedule.GetScheduledRoom(30));
        }

        [Test]
        public void BlockedRoute_UsesAlternatePath()
        {
            var graph = new RoomGraph();
            foreach (var id in new[] { "a", "b", "c", "d" })
                graph.AddRoom(new RoomDefinition(id, id));
            graph.Connect("a", "b");
            graph.Connect("b", "d");
            graph.Connect("a", "c");
            graph.Connect("c", "d");

            var s = new Scene(graph);
            s.AddNpc("npc-1", "a");
            var schedule = new NpcSchedule("npc-1");
            schedule.TryAdd(0, 100, "d");
            s.Planner.SetSchedule(schedule);
            s.Planner.IsRoomBlocked = room => room == "b";

            var r = s.Planner.Act("t1", "npc-1", 0);

            Assert.AreEqual(NpcBehaviorStatus.Executed, r.Status);
            Assert.AreEqual("c", s.Occupancy.GetRoom("npc-1"));
        }

        [Test]
        public void BlockedRoute_WithoutAlternative_WaitsThenAbandonsGoal_NoInfiniteRetry()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            var schedule = new NpcSchedule("npc-1");
            schedule.TryAdd(0, 100, HouseLayoutFactory.Bedroom);
            s.Planner.SetSchedule(schedule);
            s.Planner.IsRoomBlocked = room => room == HouseLayoutFactory.Hallway;

            Assert.AreEqual(NpcBehaviorStatus.BlockedWaiting, s.Planner.Act("t1", "npc-1", 0).Status);
            Assert.AreEqual(NpcBehaviorStatus.ScheduleGoalAbandoned, s.Planner.Act("t2", "npc-1", 10).Status);

            var third = s.Planner.Act("t3", "npc-1", 20);
            Assert.AreEqual(NpcBehaviorKind.Wait, third.Kind, "포기한 일정 목표는 같은 일정 칸 동안 다시 시도하지 않는다.");
            Assert.AreEqual(HouseLayoutFactory.LivingRoom, s.Occupancy.GetRoom("npc-1"));
        }

        [Test]
        public void DroppedClueInRoom_IsAutoAcquired()
        {
            var s = new Scene();
            var npc = s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            s.DropClue("clue-1", HouseLayoutFactory.LivingRoom);

            var r = s.Planner.Act("t1", "npc-1", 0);

            CollectionAssert.AreEqual(new[] { "clue-1" }, r.PickedUpClueIds);
            Assert.AreEqual(ClueWorldState.HeldByNpc, s.Clues.GetRecord("clue-1").WorldState);
            CollectionAssert.Contains(s.Capacity.GetHeldClues("npc-1").ToList(), "clue-1");
            Assert.IsTrue(npc.Knows("clue-1"));
            Assert.IsFalse(s.Occupancy.HasPosition("clue-1"));
        }

        [Test]
        public void DroppedClue_AtCapacity_IsPassedWithoutReplacement()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            s.Capacity.RestoreHoldings("npc-1", HolderType.Npc, new[] { "x1", "x2", "x3" });
            s.DropClue("clue-1", HouseLayoutFactory.LivingRoom);

            var r = s.Planner.Act("t1", "npc-1", 0);

            CollectionAssert.AreEqual(new[] { "clue-1" }, r.PassedClueIds);
            CollectionAssert.AreEqual(new[] { "x1", "x2", "x3" }, s.Capacity.GetHeldClues("npc-1").ToList());
            Assert.AreEqual(ClueWorldState.DroppedInWorld, s.Clues.GetRecord("clue-1").WorldState);
            Assert.IsTrue(s.Occupancy.HasPosition("clue-1"));
        }

        [Test]
        public void DroppedClue_InRoomReachedByScheduledMove_IsAcquired()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.Kitchen);
            s.DropClue("clue-1", HouseLayoutFactory.LivingRoom);
            var schedule = new NpcSchedule("npc-1");
            schedule.TryAdd(0, 100, HouseLayoutFactory.LivingRoom);
            s.Planner.SetSchedule(schedule);

            var r = s.Planner.Act("t1", "npc-1", 0);

            CollectionAssert.AreEqual(new[] { "clue-1" }, r.PickedUpClueIds);
        }

        [Test]
        public void DeceasedNpc_DoesNotPickUpClue()
        {
            var s = new Scene();
            var npc = s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom, role: NpcActualRole.Victim);
            npc.MarkDeceased();
            s.DropClue("clue-1", HouseLayoutFactory.LivingRoom);

            s.Planner.Act("t1", "npc-1", 0);

            Assert.AreEqual(ClueWorldState.DroppedInWorld, s.Clues.GetRecord("clue-1").WorldState);
        }

        [Test]
        public void ExternalCandidate_RequiringUnknownInfoOrOtherRoom_IsNotChosen()
        {
            var s = new Scene();
            var npc = s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            int executed = 0;
            var source = new ListSource();
            source.Candidates.Add(new NpcBehaviorCandidate
                { Kind = NpcBehaviorKind.Report, RequiredInfoId = "info-x", Execute = () => executed++ });
            source.Candidates.Add(new NpcBehaviorCandidate
                { Kind = NpcBehaviorKind.Investigate, RequiredRoomId = HouseLayoutFactory.Bedroom, Execute = () => executed++ });
            s.Planner.AddCandidateSource(source);

            Assert.AreEqual(NpcBehaviorKind.Wait, s.Planner.Act("t1", "npc-1", 0).Kind);
            Assert.AreEqual(0, executed);

            npc.LearnInfo("info-x", NpcJudgement.Believe);
            var r = s.Planner.Act("t2", "npc-1", 0);
            Assert.AreEqual(NpcBehaviorKind.Report, r.Kind);
            Assert.AreEqual(1, executed);
        }

        [Test]
        public void ExternalCandidate_WithoutExecuteCallback_IsNotFeasible()
        {
            var s = new Scene();
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom);
            var source = new ListSource();
            source.Candidates.Add(new NpcBehaviorCandidate { Kind = NpcBehaviorKind.Question });
            s.Planner.AddCandidateSource(source);

            Assert.AreEqual(NpcBehaviorKind.Wait, s.Planner.Act("t1", "npc-1", 0).Kind,
                "효과가 정의되지 않은 행동을 플래너가 임의로 실행하지 않는다.");
        }

        [TestCase(NpcPersonality.Suspicious, NpcBehaviorKind.Investigate)]
        [TestCase(NpcPersonality.Timid, NpcBehaviorKind.Avoid)]
        public void TendencyPolicy_IsSwappable_AndUsesPersonalityPreference(NpcPersonality personality, NpcBehaviorKind expected)
        {
            var policy = new TendencyWeightedNpcBehaviorPolicy(baseWeight: 1f, preferredWeight: 9f, random: new FixedRandom(0.5));
            var s = new Scene(policy: policy);
            s.AddNpc("npc-1", HouseLayoutFactory.LivingRoom, personality);
            var source = new ListSource();
            source.Candidates.Add(new NpcBehaviorCandidate { Kind = NpcBehaviorKind.Investigate, Execute = () => { } });
            source.Candidates.Add(new NpcBehaviorCandidate { Kind = NpcBehaviorKind.Avoid, Execute = () => { } });
            s.Planner.AddCandidateSource(source);

            var r = s.Planner.Act("t1", "npc-1", 0);

            Assert.AreEqual(expected, r.Kind);
            Assert.IsTrue(r.DecidedByProvisionalPolicy, "가중치는 원본 기획 값이 아니므로 임시 판단으로 표시한다.");
        }

        [Test]
        public void PersonalitiesMissingFromBehaviorTable_AreFlagged()
        {
            Assert.IsFalse(NpcPersonalityTendencies.IsDescribedInBehaviorTable(NpcPersonality.Jealous));
            Assert.IsFalse(NpcPersonalityTendencies.IsDescribedInBehaviorTable(NpcPersonality.Dependent));
            Assert.IsTrue(NpcPersonalityTendencies.IsDescribedInBehaviorTable(NpcPersonality.Secretive));
            CollectionAssert.IsEmpty(NpcPersonalityTendencies.GetPreferredKinds(NpcPersonality.Jealous));
        }

        [TestCase(0f, 50f)]
        [TestCase(15f, 45f)]
        [TestCase(50f, 35f)]
        public void SenderSuspicionTowardReceiver_UsesConfirmedTransferModifiers(float suspicion, float expectedChance)
        {
            var s = new Scene();
            var sender = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom, NpcPersonality.Greedy);
            sender.LearnInfo("info-1", NpcJudgement.Believe);
            s.AddNpc("npc-b", HouseLayoutFactory.LivingRoom);
            s.Relationships.SetSuspicion("npc-a", "npc-b", suspicion);

            var r = s.Planner.Act("t1", "npc-a", 0);

            Assert.AreEqual(expectedChance, r.TransferResult.ProbabilityUsed, 0.001f);
        }

        [Test]
        public void ExecuteTurn_EachNpcActsAtMostOnce()
        {
            var s = new Scene();
            var a = s.AddNpc("npc-a", HouseLayoutFactory.LivingRoom);
            a.LearnInfo("info-1", NpcJudgement.Believe);
            s.AddNpc("npc-b", HouseLayoutFactory.LivingRoom);
            s.AddNpc("npc-c", HouseLayoutFactory.Bedroom);

            var results = s.Planner.ExecuteTurn("t1", Contract, 0);

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(NpcBehaviorKind.ShareInfo, results[0].Kind);
            Assert.AreEqual(NpcBehaviorStatus.DuplicateTurn, results[1].Status);
            Assert.AreEqual(NpcBehaviorKind.Wait, results[2].Kind);

            var again = s.Planner.ExecuteTurn("t1", Contract, 0);
            Assert.IsTrue(again.All(r => r.Status == NpcBehaviorStatus.DuplicateTurn));
        }
    }
}
