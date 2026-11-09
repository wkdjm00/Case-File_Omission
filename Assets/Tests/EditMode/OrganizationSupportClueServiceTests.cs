using System.Collections.Generic;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class OrganizationSupportClueServiceTests
    {
        private static ContractInstance BuildContract(CoverUpMethod coverUp)
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            contract.Truth.CoverUp = coverUp;
            return contract;
        }

        [Test]
        public void TryGrantOrg01_SuicideDisguise_Grants()
        {
            var service = new OrganizationSupportClueService();
            var contract = BuildContract(CoverUpMethod.SuicideDisguise);

            bool granted = service.TryGrantOrg01(contract);

            Assert.IsTrue(granted);
            Assert.IsTrue(service.HasGranted(OrganizationSupportClueService.Org01ClueId));
        }

        [Test]
        public void TryGrantOrg01_NotSuicideDisguise_DoesNotGrant()
        {
            var service = new OrganizationSupportClueService();
            var contract = BuildContract(CoverUpMethod.AccidentalDeathDisguise);

            Assert.IsFalse(service.TryGrantOrg01(contract));
        }

        [Test]
        public void TryGrantOrg01_Twice_OnlyGrantsOnce()
        {
            var service = new OrganizationSupportClueService();
            var contract = BuildContract(CoverUpMethod.SuicideDisguise);

            Assert.IsTrue(service.TryGrantOrg01(contract));
            Assert.IsFalse(service.TryGrantOrg01(contract));
        }

        [Test]
        public void TryGrantOrg02_TwoConcealedPerpetratorClues_Grants()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "c1") { IdentifiesPerpetrator = true });
            repo.Discover(new ClueDefinition("clue-2", "c1") { IdentifiesPerpetrator = true });
            repo.Discover(new ClueDefinition("clue-3", "c1") { IdentifiesPerpetrator = false });

            var history = new List<ManipulationRecord>
            {
                new ManipulationRecord("clue-1", ManipulationType.Conceal, "player", "clue-3"),
                new ManipulationRecord("clue-2", ManipulationType.Conceal, "player", "clue-3"),
                new ManipulationRecord("clue-3", ManipulationType.Conceal, "player", "clue-1"),
            };

            var service = new OrganizationSupportClueService();
            bool granted = service.TryGrantOrg02(repo, history);

            Assert.IsTrue(granted);
        }

        [Test]
        public void TryGrantOrg02_OnlyOneConcealedPerpetratorClue_DoesNotGrant()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "c1") { IdentifiesPerpetrator = true });

            var history = new List<ManipulationRecord>
            {
                new ManipulationRecord("clue-1", ManipulationType.Conceal, "player", "clue-2"),
            };

            var service = new OrganizationSupportClueService();
            Assert.IsFalse(service.TryGrantOrg02(repo, history));
        }
    }
}
