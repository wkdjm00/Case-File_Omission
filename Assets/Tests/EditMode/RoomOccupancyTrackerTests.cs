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

        [Test]
        public void GetOccupants_ReturnsOnlyThatRoom_AndRemoveClearsPosition()
        {
            var graph = BuildHouse();
            var tracker = new RoomOccupancyTracker(graph);
            tracker.PlaceAt("npc-1", "living-room");
            tracker.PlaceAt("clue-1", "living-room");
            tracker.PlaceAt("npc-2", "kitchen");

            CollectionAssert.AreEquivalent(new[] { "npc-1", "clue-1" }, tracker.GetOccupants("living-room"));

            Assert.IsTrue(tracker.Remove("clue-1"));
            Assert.IsFalse(tracker.HasPosition("clue-1"));
            CollectionAssert.AreEquivalent(new[] { "npc-1" }, tracker.GetOccupants("living-room"));
            Assert.IsFalse(tracker.Remove("clue-1"), "이미 지운 대상은 false.");
        }

        [Test]
        public void FindPath_AvoidsBlockedRoom_OrReturnsNull()
        {
            var graph = new RoomGraph();
            foreach (var id in new[] { "a", "b", "c", "d" })
                graph.AddRoom(new RoomDefinition(id));
            graph.Connect("a", "b");
            graph.Connect("b", "d");
            graph.Connect("a", "c");
            graph.Connect("c", "d");

            CollectionAssert.AreEqual(new[] { "b", "d" }, graph.FindPath("a", "d"));
            CollectionAssert.AreEqual(new[] { "c", "d" }, graph.FindPath("a", "d", id => id == "b"), "막힌 방을 피해 다른 경로를 찾는다.");
            Assert.IsNull(graph.FindPath("a", "d", id => id == "b" || id == "c"), "다른 경로가 없으면 null.");
            Assert.IsNull(graph.FindPath("a", "d", id => id == "d"), "목적지가 막혀 있으면 null.");
            Assert.AreEqual(0, graph.FindPath("a", "a").Count, "출발지와 목적지가 같으면 빈 목록.");
            Assert.IsNull(graph.FindPath("a", "nowhere"));
        }
    }
}
