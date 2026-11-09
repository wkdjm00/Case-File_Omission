using System;
using CaseFileOmission.Contracts;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractContentGeneratorTests
    {
        private static ContractInstance BuildBareContract(string id = "c1")
        {
            var contract = new ContractInstance(id)
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            return contract;
        }

        [Test]
        public void Populate_AssignsPerpetratorAndVictim()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();
            var clueRepository = new ClueRepository();
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            new ContractContentGenerator(new Random(1)).Populate(contract, npcRegistry, clueRepository, occupancy);

            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.PerpetratorId));
            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.VictimId));
            Assert.IsTrue(npcRegistry.Exists(contract.Truth.PerpetratorId));
            Assert.IsTrue(npcRegistry.Exists(contract.Truth.VictimId));
        }

        [Test]
        public void Populate_PlacesPerpetratorAndVictimInSameRoom()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();
            var clueRepository = new ClueRepository();
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            new ContractContentGenerator(new Random(1)).Populate(contract, npcRegistry, clueRepository, occupancy);

            string perpRoom = occupancy.GetRoom(contract.Truth.PerpetratorId);
            string victimRoom = occupancy.GetRoom(contract.Truth.VictimId);
            Assert.AreEqual(perpRoom, victimRoom);
        }

        [Test]
        public void Populate_GeneratesCluesLinkedToContract()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();
            var clueRepository = new ClueRepository();
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            new ContractContentGenerator(new Random(1)).Populate(contract, npcRegistry, clueRepository, occupancy);

            var clueIds = clueRepository.GetAllClueIds("c1");
            Assert.AreEqual(2, clueIds.Count);
        }

        [Test]
        public void Populate_FillsMotiveMethodTool()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();
            var clueRepository = new ClueRepository();
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            new ContractContentGenerator(new Random(1)).Populate(contract, npcRegistry, clueRepository, occupancy);

            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.PrimaryMotive));
            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.Method));
            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.Tool));
        }

        [Test]
        public void Populate_PerpetratorKnowsOwnGuilt()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();
            var clueRepository = new ClueRepository();
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            new ContractContentGenerator(new Random(1)).Populate(contract, npcRegistry, clueRepository, occupancy);

            var perpetrator = npcRegistry.Get(contract.Truth.PerpetratorId);
            Assert.AreEqual(1, perpetrator.KnownInfoIds.Count);
        }
    }
}
