using System.Collections.Generic;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class RoomOccupancyTrackerTests
    {
        private static RoomGraph BuildHouse()
        {
            var graph = new RoomGraph();
            graph.AddRoom(new RoomDefinition("living-room"));
            graph.AddRoom(new RoomDefinition("kitchen"));
            graph.AddRoom(new RoomDefinition("attic"));
            graph.Connect("living-room", "kitchen");
            return graph;
        }

        [Test]
        public void PlaceAt_UnknownRoom_Throws()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);

            Assert.Throws<KeyNotFoundException>(() => tracker.PlaceAt("npc-1", "does-not-exist"));
        }

        [Test]
        public void TryMove_ToConnectedRoom_Succeeds()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);
            tracker.PlaceAt("npc-1", "living-room");

            bool moved = tracker.TryMove("npc-1", "kitchen");

            Assert.IsTrue(moved);
            Assert.AreEqual("kitchen", tracker.GetRoom("npc-1"));
        }

        [Test]
        public void TryMove_ToUnconnectedRoom_Fails_PositionUnchanged()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);
            tracker.PlaceAt("npc-1", "living-room");

            bool moved = tracker.TryMove("npc-1", "attic");

            Assert.IsFalse(moved);
            Assert.AreEqual("living-room", tracker.GetRoom("npc-1"));
        }

        [Test]
        public void TryMove_ToSameRoom_SucceedsAsNoOp()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);
            tracker.PlaceAt("npc-1", "living-room");

            Assert.IsTrue(tracker.TryMove("npc-1", "living-room"));
        }

        [Test]
        public void GetRoom_BeforePlacement_Throws()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);

            Assert.Throws<KeyNotFoundException>(() => tracker.GetRoom("npc-1"));
        }

        [Test]
        public void HasPosition_ReflectsPlacementState()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);

            Assert.IsFalse(tracker.HasPosition("npc-1"));
            tracker.PlaceAt("npc-1", "living-room");
            Assert.IsTrue(tracker.HasPosition("npc-1"));
        }
    }
}
