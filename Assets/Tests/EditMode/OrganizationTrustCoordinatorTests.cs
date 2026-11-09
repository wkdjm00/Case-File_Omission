using CaseFileOmission.Contracts;
using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class OrganizationTrustCoordinatorTests
    {
        private static ContractInstance BuildContract(OrganizationConditionType conditionType, bool? achieved)
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            contract.OrgCondition.Type = conditionType;
            contract.OrgCondition.Achieved = achieved;
            return contract;
        }

        [Test]
        public void ApplyContractOutcome_ConditionAchieved_IncreasesTrustByFive()
        {
            var state = new OrganizationState();
            var contract = BuildContract(OrganizationConditionType.RecoverBody, true);

            OrganizationTrustCoordinator.ApplyContractOutcome(state, contract, contractAbandoned: false);

            Assert.AreEqual(45f, state.Trust);
        }

        [Test]
        public void ApplyContractOutcome_ConditionFailed_DecreasesTrustByFive()
        {
            var state = new OrganizationState();
            var contract = BuildContract(OrganizationConditionType.RecoverBody, false);

            OrganizationTrustCoordinator.ApplyContractOutcome(state, contract, contractAbandoned: false);

            Assert.AreEqual(35f, state.Trust);
        }

        [Test]
        public void ApplyContractOutcome_NoCondition_NoChange()
        {
            var state = new OrganizationState();
            var contract = BuildContract(OrganizationConditionType.None, null);

            OrganizationTrustCoordinator.ApplyContractOutcome(state, contract, contractAbandoned: false);

            Assert.AreEqual(40f, state.Trust);
        }

        [Test]
        public void ApplyContractOutcome_Abandoned_DecreasesTrustByTwo_RegardlessOfCondition()
        {
            var state = new OrganizationState();
            var contract = BuildContract(OrganizationConditionType.RecoverBody, true);

            OrganizationTrustCoordinator.ApplyContractOutcome(state, contract, contractAbandoned: true);

            Assert.AreEqual(38f, state.Trust);
        }

        [Test]
        public void ApplyContractOutcome_ConditionNotYetJudged_NoChange()
        {
            var state = new OrganizationState();
            var contract = BuildContract(OrganizationConditionType.RecoverBody, null);

            OrganizationTrustCoordinator.ApplyContractOutcome(state, contract, contractAbandoned: false);

            Assert.AreEqual(40f, state.Trust);
        }
    }
}
