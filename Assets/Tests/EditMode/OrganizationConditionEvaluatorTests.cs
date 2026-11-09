using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class OrganizationConditionEvaluatorTests
    {
        private static ContractInstance BuildContract()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            return contract;
        }

        [Test]
        public void Evaluate_NoCondition_ReturnsNull()
        {
            var contract = BuildContract();
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.IsNull(result);
        }

        [Test]
        public void Evaluate_RecoverBody_TargetNotFound_ReturnsFalse()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.RecoverBody;
            contract.OrgCondition.TargetId = "clue-body";
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(false, result);
        }

        [Test]
        public void Evaluate_RecoverBody_HeldByPlayer_ReturnsTrue()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.RecoverBody;
            contract.OrgCondition.TargetId = "clue-body";
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-body", "c1"));

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(true, result);
        }

        [Test]
        public void Evaluate_RecoverBody_Dropped_ReturnsFalse()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.RecoverBody;
            contract.OrgCondition.TargetId = "clue-body";
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-body", "c1"));
            repo.Drop("clue-body");

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(false, result);
        }

        [Test]
        public void Evaluate_RecoverItem_RequiresDelivery_FalseUntilDelivered()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.RecoverItem;
            contract.OrgCondition.TargetId = "clue-item";
            contract.OrgCondition.RequiresDeliveryToOrganization = true;
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-item", "c1"));

            var withoutDelivery = OrganizationConditionEvaluator.Evaluate(contract, repo, deliveredToOrganization: false);
            var withDelivery = OrganizationConditionEvaluator.Evaluate(contract, repo, deliveredToOrganization: true);

            Assert.AreEqual(false, withoutDelivery);
            Assert.AreEqual(true, withDelivery);
        }

        [Test]
        public void Evaluate_DesignateOfficialCulprit_NotFinalized_ReturnsNull()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.DesignateOfficialCulprit;
            contract.OrgCondition.TargetId = "npc-perp";
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.IsNull(result);
        }

        [Test]
        public void Evaluate_DesignateOfficialCulprit_MatchesTarget_ReturnsTrue()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.DesignateOfficialCulprit;
            contract.OrgCondition.TargetId = "npc-perp";
            contract.OfficialConclusion.IsFinalized = true;
            contract.OfficialConclusion.OfficiallyAccusedNpcId = "npc-perp";
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(true, result);
        }

        [Test]
        public void Evaluate_DesignateOfficialCulprit_WrongPerson_ReturnsFalse()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.DesignateOfficialCulprit;
            contract.OrgCondition.TargetId = "npc-perp";
            contract.OfficialConclusion.IsFinalized = true;
            contract.OfficialConclusion.OfficiallyAccusedNpcId = "npc-someone-else";
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(false, result);
        }

        [Test]
        public void Evaluate_KeepUnsolved_OutcomeUnsolved_ReturnsTrue()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.KeepUnsolved;
            contract.OfficialConclusion.IsFinalized = true;
            contract.OfficialConclusion.Outcome = ContractOutcome.Unsolved;
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(true, result);
        }

        [Test]
        public void Evaluate_KeepUnsolved_OutcomeResolved_ReturnsFalse()
        {
            var contract = BuildContract();
            contract.OrgCondition.Type = OrganizationConditionType.KeepUnsolved;
            contract.OfficialConclusion.IsFinalized = true;
            contract.OfficialConclusion.Outcome = ContractOutcome.Resolved;
            var repo = new ClueRepository();

            var result = OrganizationConditionEvaluator.Evaluate(contract, repo);

            Assert.AreEqual(false, result);
        }
    }
}
