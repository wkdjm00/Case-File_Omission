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
        public void DecideAndAct_NoOneToTalkTo_StaysInPlace()
        {
            var (registry, rooms, occupancy) = BuildScene();
            var npc = new NpcState("npc-1", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            registry.Add(npc);
            occupancy.PlaceAt("npc-1", HouseLayoutFactory.LivingRoom);

            var loop = new NpcActionLoop(registry, occupancy, rooms, new NpcInfoTransferService(new Random(1)));
            var result = loop.DecideAndAct("npc-1", "c1");

            // 07 확정: 할 일이 없으면 대기한다(무작위 이동하지 않는다).
            Assert.AreEqual(NpcActionType.Stay, result.ActionType);
            Assert.AreEqual(HouseLayoutFactory.LivingRoom, occupancy.GetRoom("npc-1"));
        }

        [Test]
        public void DecideAndAct_AlreadyTransferredInfo_IsNotRetried()
        {
            var (registry, rooms, occupancy) = BuildScene();

            var informed = new NpcState("npc-informed", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            informed.LearnInfo("secret-1", NpcJudgement.Believe);
            var uninformed = new NpcState("npc-uninformed", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            registry.Add(informed);
            registry.Add(uninformed);
            occupancy.PlaceAt("npc-informed", HouseLayoutFactory.LivingRoom);
            occupancy.PlaceAt("npc-uninformed", HouseLayoutFactory.LivingRoom);

            // roll 0.0 < 최소 5%이므로 전달은 반드시 성공한다.
            var transfer = new NpcInfoTransferService(new FixedRandom(0.0));
            var loop = new NpcActionLoop(registry, occupancy, rooms, transfer);
            var first = loop.DecideAndAct("npc-uninformed", "c1");
            Assert.AreEqual(InfoTransferOutcome.Succeeded, first.TransferResult.Outcome);

            var second = loop.DecideAndAct("npc-uninformed", "c1");
            Assert.AreEqual(NpcActionType.Stay, second.ActionType, "같은 쌍에 이미 전달한 정보는 반복 전달하지 않고, 할 일이 없으면 대기한다.");
        }

        private sealed class FixedRandom : Random
        {
            private readonly double _value;
            public FixedRandom(double value) { _value = value; }
            public override double NextDouble() => _value;
            protected override double Sample() => _value;
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

            Assert.AreEqual(NpcActionType.Stay, result.ActionType, "다른 방에 있는 NPC와는 정보 전달을 시도하지 않는다.");
        }
    }
}
