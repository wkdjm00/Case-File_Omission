using System;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ClueConfirmationRequestServiceTests
    {
        [Test]
        public void Roll_NotSameRoom_NeverRequested()
        {
            var service = new ClueConfirmationRequestService(new Random(1));
            var result = service.Roll(PoliceRoster.PoliceA, sameRoomAsPlayer: false, currentSuspicion: 90f, cluesFoundByPoliceInThisRoom: 5, acceptedLiesCount: 10);

            Assert.IsFalse(result.Requested);
            Assert.AreEqual(0f, result.ProbabilityUsed);
        }

        [Test]
        public void Roll_BaseCase_UsesTenPercent()
        {
            var service = new ClueConfirmationRequestService(new Random(1));
            var result = service.Roll(PoliceRoster.PoliceC, sameRoomAsPlayer: true, currentSuspicion: 0f, cluesFoundByPoliceInThisRoom: 0, acceptedLiesCount: 0);

            Assert.AreEqual(10f, result.ProbabilityUsed);
        }

        [Test]
        public void Roll_InvestigativeType_AddsPerClueFound()
        {
            var service = new ClueConfirmationRequestService(new Random(1));
            var result = service.Roll(PoliceRoster.PoliceA, sameRoomAsPlayer: true, currentSuspicion: 0f, cluesFoundByPoliceInThisRoom: 2, acceptedLiesCount: 0);

            Assert.AreEqual(20f, result.ProbabilityUsed); // 10 + 5*2
        }

        [Test]
        public void Roll_IntuitiveType_AddsPerTwoAcceptedLies()
        {
            var service = new ClueConfirmationRequestService(new Random(1));
            var result = service.Roll(PoliceRoster.PoliceE, sameRoomAsPlayer: true, currentSuspicion: 0f, cluesFoundByPoliceInThisRoom: 0, acceptedLiesCount: 4);

            Assert.AreEqual(20f, result.ProbabilityUsed); // 10 + 5*(4/2)
        }

        [Test]
        public void Roll_ProbabilityNeverExceedsFiftyPercent()
        {
            var service = new ClueConfirmationRequestService(new Random(1));
            var result = service.Roll(PoliceRoster.PoliceA, sameRoomAsPlayer: true, currentSuspicion: 100f, cluesFoundByPoliceInThisRoom: 10, acceptedLiesCount: 20);

            Assert.AreEqual(50f, result.ProbabilityUsed);
        }
    }
}
