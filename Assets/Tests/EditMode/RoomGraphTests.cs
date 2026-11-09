using System;
using System.Collections.Generic;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class RoomGraphTests
    {
        private static RoomGraph BuildHouse()
        {
            var graph = new RoomGraph();
            graph.AddRoom(new RoomDefinition("living-room"));
            graph.AddRoom(new RoomDefinition("kitchen"));
            graph.AddRoom(new RoomDefinition("hallway"));
            graph.AddRoom(new RoomDefinition("bedroom"));
            graph.AddRoom(new RoomDefinition("attic")); // 연결 안 함(고립된 방)

            graph.Connect("living-room", "kitchen");
            graph.Connect("living-room", "hallway");
            graph.Connect("hallway", "bedroom");
            return graph;
        }

        [Test]
        public void Connect_UnknownRoom_Throws()
        {
            var graph = new RoomGraph();
            graph.AddRoom(new RoomDefinition("living-room"));

            Assert.Throws<KeyNotFoundException>(() => graph.Connect("living-room", "does-not-exist"));
        }

        [Test]
        public void AddRoom_DuplicateId_Throws()
        {
            var graph = new RoomGraph();
            graph.AddRoom(new RoomDefinition("living-room"));

            Assert.Throws<InvalidOperationException>(() => graph.AddRoom(new RoomDefinition("living-room")));
        }

        [Test]
        public void AreConnected_IsBidirectional()
        {
            var graph = BuildHouse();

            Assert.IsTrue(graph.AreConnected("living-room", "kitchen"));
            Assert.IsTrue(graph.AreConnected("kitchen", "living-room"));
        }

        [Test]
        public void AreConnected_NonAdjacentRooms_False()
        {
            var graph = BuildHouse();

            Assert.IsFalse(graph.AreConnected("kitchen", "bedroom"), "주방과 침실은 직접 연결되어 있지 않다(거실을 거쳐야 함).");
        }

        [Test]
        public void CanReach_MultiHopPath_True()
        {
            var graph = BuildHouse();

            Assert.IsTrue(graph.CanReach("kitchen", "bedroom"), "주방->거실->복도->침실 경로로 도달 가능해야 한다.");
        }

        [Test]
        public void CanReach_IsolatedRoom_False()
        {
            var graph = BuildHouse();

            Assert.IsFalse(graph.CanReach("living-room", "attic"), "다락방은 어디와도 연결되지 않았다.");
        }

        [Test]
        public void CanReach_SameRoom_True()
        {
            var graph = BuildHouse();

            Assert.IsTrue(graph.CanReach("kitchen", "kitchen"));
        }
    }
}
