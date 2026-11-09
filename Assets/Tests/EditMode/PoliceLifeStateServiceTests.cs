using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PoliceLifeStateServiceTests
    {
        [Test]
        public void AllPoliceStartAlive()
        {
            var service = new PoliceLifeStateService();

            foreach (var id in PoliceRoster.AllPoliceIds)
                Assert.IsTrue(service.IsAlive(id));

            Assert.IsFalse(service.AllPoliceDeceased());
        }

        [Test]
        public void MarkDeceased_UpdatesIsAliveAndDeceasedList()
        {
            var service = new PoliceLifeStateService();

            service.MarkDeceased(PoliceRoster.PoliceA);

            Assert.IsFalse(service.IsAlive(PoliceRoster.PoliceA));
            Assert.IsTrue(service.IsAlive(PoliceRoster.PoliceB));
            CollectionAssert.Contains(service.GetDeceasedIds(), PoliceRoster.PoliceA);
        }

        [Test]
        public void AllPoliceDeceased_TrueOnlyWhenAllFiveMarked()
        {
            var service = new PoliceLifeStateService();

            foreach (var id in PoliceRoster.AllPoliceIds)
            {
                Assert.IsFalse(service.AllPoliceDeceased());
                service.MarkDeceased(id);
            }

            Assert.IsTrue(service.AllPoliceDeceased());
        }
    }
}
