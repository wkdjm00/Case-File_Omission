using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PoliceSuspicionServiceTests
    {
        [Test]
        public void ApplyBatch_DecreasesBeforeIncreases_ThenClampsOnce()
        {
            var service = new PoliceSuspicionService();
            // 시작 0에서 -50과 +30을 같이 적용하면: 0 -50 +30 = -20 -> clamp 0
            float result = service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { -50f, 30f });

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void ApplyBatch_WithinBounds_SumsCorrectly()
        {
            var service = new PoliceSuspicionService();
            service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 20f });
            float result = service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { -5f, 8f });

            Assert.AreEqual(23f, result);
        }

        [Test]
        public void AnyPoliceReachedArrestThreshold_DetectsMaxSuspicion()
        {
            var service = new PoliceSuspicionService();
            Assert.IsFalse(service.AnyPoliceReachedArrestThreshold("player"));

            service.ApplyBatch(PoliceRoster.PoliceC, "player", new[] { 100f });

            Assert.IsTrue(service.AnyPoliceReachedArrestThreshold("player"));
        }

        [Test]
        public void ApplyContractEndEffects_Resolved_HandlerBelow55_DecreasesTen()
        {
            var service = new PoliceSuspicionService();
            service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 30f }); // handler starts at 30

            service.ApplyContractEndEffects("player", PoliceRoster.PoliceA, startingHandlerSuspicion: 30f, kind: ContractEndKind.Resolved);

            Assert.AreEqual(20f, service.GetSuspicion(PoliceRoster.PoliceA, "player"));
        }

        [Test]
        public void ApplyContractEndEffects_Resolved_HandlerImproved_LowersOtherPoliceToo()
        {
            var service = new PoliceSuspicionService();
            service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 30f });
            service.ApplyBatch(PoliceRoster.PoliceB, "player", new[] { 40f });

            service.ApplyContractEndEffects("player", PoliceRoster.PoliceA, startingHandlerSuspicion: 30f, kind: ContractEndKind.Resolved);

            // 핸들러가 30 -> 20으로 낮아졌으므로 다른 경찰도 -5
            Assert.AreEqual(35f, service.GetSuspicion(PoliceRoster.PoliceB, "player"));
        }

        [Test]
        public void ApplyContractEndEffects_Unsolved_NoChange()
        {
            var service = new PoliceSuspicionService();
            service.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 30f });

            service.ApplyContractEndEffects("player", PoliceRoster.PoliceA, startingHandlerSuspicion: 30f, kind: ContractEndKind.Unsolved);

            Assert.AreEqual(30f, service.GetSuspicion(PoliceRoster.PoliceA, "player"));
        }

        [Test]
        public void ApplyContractEndEffects_HandlerDeceased_AllPoliceUpFive_NextHandlerUpFifteen()
        {
            var service = new PoliceSuspicionService();

            service.ApplyContractEndEffects("player", PoliceRoster.PoliceA, 0f, ContractEndKind.HandlerPoliceDeceased, PoliceRoster.PoliceB);

            Assert.AreEqual(5f, service.GetSuspicion(PoliceRoster.PoliceC, "player"));
            Assert.AreEqual(15f, service.GetSuspicion(PoliceRoster.PoliceB, "player"));
        }
    }
}
