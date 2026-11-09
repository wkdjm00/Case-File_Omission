using CaseFileOmission.Core;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PolicePursuitControllerTests
    {
        private static PoliceSuspicionService BuildServiceWithHighSuspicion(int countAtOrAbove70)
        {
            var service = new PoliceSuspicionService();
            var ids = PoliceRoster.AllPoliceIds;
            for (int i = 0; i < countAtOrAbove70; i++)
                service.ApplyBatch(ids[i], "player", new[] { 70f });
            return service;
        }

        [Test]
        public void TryTrigger_FewerThanThreeHighSuspicionPolice_DoesNotTrigger()
        {
            var suspicion = BuildServiceWithHighSuspicion(2);
            var controller = new PolicePursuitController(suspicion, new GameEventLog());

            bool triggered = controller.TryTrigger("player", "death-1");

            Assert.IsFalse(triggered);
            Assert.AreEqual(PursuitStage.NotActive, controller.Stage);
        }

        [Test]
        public void TryTrigger_ThreeOrMoreHighSuspicionPolice_Triggers()
        {
            var suspicion = BuildServiceWithHighSuspicion(3);
            var controller = new PolicePursuitController(suspicion, new GameEventLog());

            bool triggered = controller.TryTrigger("player", "death-1");

            Assert.IsTrue(triggered);
            Assert.AreEqual(PursuitStage.Triggered, controller.Stage);
        }

        [Test]
        public void TryTrigger_SameDeathEventId_DoesNotRetrigger()
        {
            var suspicion = BuildServiceWithHighSuspicion(3);
            var log = new GameEventLog();
            var controller = new PolicePursuitController(suspicion, log);
            controller.TryTrigger("player", "death-1");

            var secondController = new PolicePursuitController(suspicion, log);
            bool secondTriggered = secondController.TryTrigger("player", "death-1");

            Assert.IsFalse(secondTriggered, "같은 사망 이벤트로는 중복 발동되지 않아야 한다.");
        }

        [Test]
        public void FullLifecycle_TransitionsInOrder()
        {
            var suspicion = BuildServiceWithHighSuspicion(3);
            var controller = new PolicePursuitController(suspicion, new GameEventLog());
            controller.TryTrigger("player", "death-1");

            controller.AdvanceToGatheringInformation();
            controller.AdvanceToPlayerResponse();
            controller.ResolveResponse(true);

            Assert.AreEqual(PursuitStage.ResolvedSuccess, controller.Stage);
        }

        [Test]
        public void ResolveResponse_Failure_CanResume()
        {
            var suspicion = BuildServiceWithHighSuspicion(3);
            var controller = new PolicePursuitController(suspicion, new GameEventLog());
            controller.TryTrigger("player", "death-1");
            controller.AdvanceToGatheringInformation();
            controller.AdvanceToPlayerResponse();
            controller.ResolveResponse(false);

            Assert.AreEqual(PursuitStage.ResolvedFailure, controller.Stage);

            controller.ResumeAfterFailedResponse();
            Assert.AreEqual(PursuitStage.PlayerResponse, controller.Stage);
        }
    }
}
