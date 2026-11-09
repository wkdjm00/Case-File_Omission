using System.Linq;
using CaseFileOmission.Core;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class GameEventLogTests
    {
        [Test]
        public void TryRecord_DuplicateEventId_IsRejected()
        {
            var log = new GameEventLog();
            var evt1 = new GameEvent("evt-1", GameEventType.ClueDiscovered, "contract-1", "payload-a");
            var evt2 = new GameEvent("evt-1", GameEventType.ClueDiscovered, "contract-1", "payload-b");

            bool firstAccepted = log.TryRecord(evt1);
            bool secondAccepted = log.TryRecord(evt2);

            Assert.IsTrue(firstAccepted);
            Assert.IsFalse(secondAccepted, "동일 eventId는 재처리되지 않아야 한다.");
            Assert.AreEqual(1, log.GetOrdered().Count);
        }

        [Test]
        public void TryRecord_DistinctEventIds_AreAllStored()
        {
            var log = new GameEventLog();
            log.TryRecord(new GameEvent("evt-1", GameEventType.SuspicionChanged, "c1", "a"));
            log.TryRecord(new GameEvent("evt-2", GameEventType.SuspicionChanged, "c1", "b"));

            Assert.AreEqual(2, log.GetOrdered().Count);
        }

        [Test]
        public void GetByContract_FiltersCorrectly()
        {
            var log = new GameEventLog();
            log.TryRecord(new GameEvent("evt-1", GameEventType.ClueDiscovered, "c1", "a"));
            log.TryRecord(new GameEvent("evt-2", GameEventType.ClueDiscovered, "c2", "b"));

            var forC1 = log.GetByContract("c1").ToList();

            Assert.AreEqual(1, forC1.Count);
            Assert.AreEqual("evt-1", forC1[0].EventId);
        }

        [Test]
        public void HasProcessed_ReflectsRecordedState()
        {
            var log = new GameEventLog();
            Assert.IsFalse(log.HasProcessed("evt-1"));

            log.TryRecord(new GameEvent("evt-1", GameEventType.Custom, "c1", null));

            Assert.IsTrue(log.HasProcessed("evt-1"));
        }
    }
}
