using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class OrganizationStateTests
    {
        [Test]
        public void StartsAtForty()
        {
            var state = new OrganizationState();
            Assert.AreEqual(40f, state.Trust);
        }

        [Test]
        public void ApplyConditionAchieved_AddsFive()
        {
            var state = new OrganizationState();
            state.ApplyConditionAchieved();
            Assert.AreEqual(45f, state.Trust);
        }

        [Test]
        public void ApplyConditionFailed_SubtractsFive()
        {
            var state = new OrganizationState();
            state.ApplyConditionFailed();
            Assert.AreEqual(35f, state.Trust);
        }

        [Test]
        public void ApplyContractAbandoned_SubtractsTwo()
        {
            var state = new OrganizationState();
            state.ApplyContractAbandoned();
            Assert.AreEqual(38f, state.Trust);
        }

        [Test]
        public void Trust_ClampedWithinZeroToHundred()
        {
            var state = new OrganizationState();
            for (int i = 0; i < 30; i++) state.ApplyConditionFailed();
            Assert.AreEqual(0f, state.Trust);
            Assert.IsTrue(state.HasReachedZero);

            var highState = new OrganizationState();
            for (int i = 0; i < 30; i++) highState.ApplyConditionAchieved();
            Assert.AreEqual(100f, highState.Trust);
        }

        [TestCase(10f, SupportTier.NoSupport)]
        [TestCase(30f, SupportTier.ExtraConditionAdded)]
        [TestCase(50f, SupportTier.Base)]
        [TestCase(80f, SupportTier.OneConditionEased)]
        [TestCase(95f, SupportTier.TwoConditionsEased)]
        public void SupportTierResolver_MatchesConfirmedRanges(float trust, SupportTier expected)
        {
            Assert.AreEqual(expected, OrganizationSupportTierResolver.Resolve(trust));
        }

        [Test]
        public void SupportTierResolver_Zero_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() => OrganizationSupportTierResolver.Resolve(0f));
        }
    }
}
