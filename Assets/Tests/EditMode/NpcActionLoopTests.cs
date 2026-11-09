using System;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class NpcActionLoopTests
    {
        private static (NpcRegistry registry, RoomGraph rooms, RoomOccupancyTracker occupancy) BuildScene()
        {
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);
            var registry = new NpcRegistry();
            return (registry, rooms, occupancy);
        }

        [Test]
        public void DecideAndAct_DeceasedNpc_Stays()
        {
            var (registry, rooms, occupancy) = BuildScene();
            var npc = new NpcState("npc-1", "c1", NpcActualRole.Victim, NpcPersonality.Indifferent);
            npc.MarkDeceased();
            registry.Add(npc);
            occupancy.PlaceAt("npc-1", HouseLayoutFactory.LivingRoom);

            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(1)));
            var result = loop.DecideAndAct("npc-1", "c1");

            Assert.AreEqual(NpcActionType.Stay, result.ActionType);
        }

        [Test]
        public void DecideAndAct_OtherNpcInSameRoomWithUnknownInfo_AttemptsTransfer()
        {
            var (registry, rooms, occupancy) = BuildScene();

            var informed = new NpcState("npc-informed", "c1", NpcActualRole.Witness, NpcPersonality.BlindlyTrusting);
            informed.LearnInfo("secret-1", NpcJudgement.Believe);
            var uninformed = new NpcState("npc-uninformed", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);

            registry.Add(informed);
            registry.Add(uninformed);
            occupancy.PlaceAt("npc-informed", HouseLayoutFactory.LivingRoom);
            occupancy.PlaceAt("npc-uninformed", HouseLayoutFactory.LivingRoom);

            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(1)));
            var result = loop.DecideAndAct("npc-uninformed", "c1");

            Assert.AreEqual(NpcActionType.AttemptInfoTransfer, result.ActionType);
            Assert.AreEqual("npc-informed", result.ActorId);
            Assert.AreEqual("npc-uninformed", result.TargetId);
            Assert.AreEqual("secret-1", result.InfoId);
            Assert.IsNotNull(result.TransferResult);
        }

        [Test]
        public void DecideAndAct_TransferSuccess_ReceiverActuallyLearnsInfo()
        {
            var (registry, rooms, occupancy) = BuildScene();

            var informed = new NpcState("npc-informed", "c1", NpcActualRole.Witness, NpcPersonality.BlindlyTrusting);
            informed.LearnInfo("secret-1", NpcJudgement.Believe);
            var uninformed = new NpcState("npc-uninformed", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);

            registry.Add(informed);
            registry.Add(uninformed);
            occupancy.PlaceAt("npc-informed", HouseLayoutFactory.LivingRoom);
            occupancy.PlaceAt("npc-uninformed", HouseLayoutFactory.LivingRoom);

            // BlindlyTrusting(+15) 발신자와 낮은 시드 조합으로 성공을 유도한다. 성공하지 못해도
            // TryTransfer 결과와 NpcState 반영이 항상 일치하는지만 검증한다.
            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(2)));
            var result = loop.DecideAndAct("npc-uninformed", "c1");

            bool receiverKnows = uninformed.Knows("secret-1");
            bool reportedSuccess = result.TransferResult.Outcome == InfoTransferOutcome.Succeeded;
            Assert.AreEqual(reportedSuccess, receiverKnows, "전달 결과와 NPC의 실제 지식 상태가 항상 일치해야 한다.");
        }

        [Test]
        public void DecideAndAct_NoOneToTalkTo_MovesToAdjacentRoom()
        {
            var (registry, rooms, occupancy) = BuildScene();
            var npc = new NpcState("npc-1", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            registry.Add(npc);
            occupancy.PlaceAt("npc-1", HouseLayoutFactory.LivingRoom);

            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(1)));
            var result = loop.DecideAndAct("npc-1", "c1");

            Assert.AreEqual(NpcActionType.Move, result.ActionType);
            Assert.IsTrue(rooms.AreConnected(HouseLayoutFactory.LivingRoom, result.TargetId));
            Assert.AreEqual(result.TargetId, occupancy.GetRoom("npc-1"), "실제로 이동이 적용되어 있어야 한다.");
        }

        [Test]
        public void DecideAndAct_IgnoresNpcsInDifferentRooms()
        {
            var (registry, rooms, occupancy) = BuildScene();

            var informedElsewhere = new NpcState("npc-informed", "c1", NpcActualRole.Witness, NpcPersonality.BlindlyTrusting);
            informedElsewhere.LearnInfo("secret-1", NpcJudgement.Believe);
            var npc = new NpcState("npc-1", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);

            registry.Add(informedElsewhere);
            registry.Add(npc);
            occupancy.PlaceAt("npc-informed", HouseLayoutFactory.Bedroom);
            occupancy.PlaceAt("npc-1", HouseLayoutFactory.LivingRoom);

            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(1)));
            var result = loop.DecideAndAct("npc-1", "c1");

            Assert.AreEqual(NpcActionType.Move, result.ActionType, "다른 방에 있는 NPC와는 정보 전달을 시도하지 않는다.");
        }
    }
}
