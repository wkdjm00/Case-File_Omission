using System.Linq;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class TempLayoutV1Tests
    {
        [Test]
        public void StandardHouse_HasExactlyEightRooms_AndSevenFixedConnections()
        {
            var graph = HouseLayoutFactory.CreateStandardHouse();

            Assert.AreEqual(8, graph.AllRoomIds.Count);
            CollectionAssert.AreEquivalent(HouseLayoutFactory.AllRoomIds, graph.AllRoomIds);
            Assert.AreEqual(7, TempLayoutV1.Doors.Count);
            Assert.IsTrue(graph.AreConnected(HouseLayoutFactory.EntranceHall, HouseLayoutFactory.Reception));
            Assert.IsTrue(graph.AreConnected(HouseLayoutFactory.Kitchen, HouseLayoutFactory.PantryUtility));
            Assert.IsFalse(graph.AreConnected(HouseLayoutFactory.EntranceHall, HouseLayoutFactory.Kitchen),
                "주방은 홀과 벽만 공유하고 문은 식당·저장실 쪽에만 있다.");
        }

        [Test]
        public void StandardHouse_HasNoForbiddenRooms()
        {
            foreach (var id in HouseLayoutFactory.AllRoomIds)
            {
                StringAssert.DoesNotContain("BATH", id);
                StringAssert.DoesNotContain("GUEST", id);
                StringAssert.DoesNotContain("BASEMENT", id);
                StringAssert.DoesNotContain("OUTDOOR", id);
            }
        }

        [Test]
        public void Layout_PassesAllCoordinateChecks()
        {
            var issues = TempLayoutV1.Validate();
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test]
        public void EveryRoom_IsReachableFromEveryOtherRoom()
        {
            var graph = HouseLayoutFactory.CreateStandardHouse();
            foreach (var a in HouseLayoutFactory.AllRoomIds)
            foreach (var b in HouseLayoutFactory.AllRoomIds)
                Assert.IsNotNull(graph.FindPath(a, b), $"{a} → {b}");
        }

        [Test]
        public void DoorFitsWall_RejectsDoorOutsideSharedWall()
        {
            var hall = TempLayoutV1.GetRoom(HouseLayoutFactory.EntranceHall);
            var reception = TempLayoutV1.GetRoom(HouseLayoutFactory.Reception);
            var off = new DoorLayout { DoorId = "X", RoomA = hall.RoomId, RoomB = reception.RoomId, CenterX = -5.1f, CenterZ = 4.8f };

            Assert.IsFalse(TempLayoutV1.DoorFitsWall(off, hall, reception), "응접실 세로 범위(±3) 밖의 문은 맞닿지 않는다.");
        }

        [Test]
        public void FindDoor_IsSymmetric()
        {
            Assert.AreSame(TempLayoutV1.FindDoor(HouseLayoutFactory.Study, HouseLayoutFactory.CorridorStairs),
                TempLayoutV1.FindDoor(HouseLayoutFactory.CorridorStairs, HouseLayoutFactory.Study));
            Assert.IsNull(TempLayoutV1.FindDoor(HouseLayoutFactory.Study, HouseLayoutFactory.Bedroom));
            Assert.IsTrue(TempLayoutV1.Doors.Select(d => d.DoorId).Distinct().Count() == TempLayoutV1.Doors.Count);
        }
    }
}
