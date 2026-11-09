using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PolicePositionTrackerTests
    {
        [Test]
        public void TryGetLastKnownRoom_NoSightingYet_ReturnsFalse()
        {
            var tracker = new PolicePositionTracker();

            bool found = tracker.TryGetLastKnownRoom(PoliceRoster.PoliceA, out var room);

            Assert.IsFalse(found, "경찰은 보고된 적 없는 위치를 알고 있으면 안 된다.");
            Assert.IsNull(room);
        }

        [Test]
        public void ReportSighting_ThenTryGetLastKnownRoom_ReturnsReportedRoom()
        {
            var tracker = new PolicePositionTracker();

            tracker.ReportSighting(PoliceRoster.PoliceA, "kitchen");

            bool found = tracker.TryGetLastKnownRoom(PoliceRoster.PoliceA, out var room);
            Assert.IsTrue(found);
            Assert.AreEqual("kitchen", room);
        }

        [Test]
        public void ReportSighting_UpdatesToLatestRoom()
        {
            var tracker = new PolicePositionTracker();

            tracker.ReportSighting(PoliceRoster.PoliceA, "kitchen");
            tracker.ReportSighting(PoliceRoster.PoliceA, "bedroom");

            tracker.TryGetLastKnownRoom(PoliceRoster.PoliceA, out var room);
            Assert.AreEqual("bedroom", room);
        }

        [Test]
        public void ClearSighting_RemovesKnownPosition()
        {
            var tracker = new PolicePositionTracker();
            tracker.ReportSighting(PoliceRoster.PoliceA, "kitchen");

            tracker.ClearSighting(PoliceRoster.PoliceA);

            Assert.IsFalse(tracker.TryGetLastKnownRoom(PoliceRoster.PoliceA, out _));
        }
    }
}
