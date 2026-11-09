using System.Collections.Generic;
using CaseFileOmission.Endings;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class GameEndEvaluatorTests
    {
        [Test]
        public void Evaluate_NoCondition_ReturnsNone()
        {
            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState());
            var result = evaluator.EvaluateAutomaticEndings("player", new List<string>());

            Assert.AreEqual(GameEndingType.None, result);
        }

        [Test]
        public void Evaluate_PoliceSuspicionMax_ReturnsArrest()
        {
            var suspicion = new PoliceSuspicionService();
            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f });

            var evaluator = new GameEndEvaluator(suspicion, new OrganizationState());
            var result = evaluator.EvaluateAutomaticEndings("player", new List<string>());

            Assert.AreEqual(GameEndingType.Arrest, result);
        }

        [Test]
        public void Evaluate_AllPoliceDeceased_ReturnsPoliceAllEliminated()
        {
            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState());
            var deceased = new List<string>(PoliceRoster.AllPoliceIds);

            var result = evaluator.EvaluateAutomaticEndings("player", deceased);

            Assert.AreEqual(GameEndingType.PoliceAllEliminated, result);
        }

        [Test]
        public void Evaluate_OrganizationTrustZero_ReturnsOrganizationTrustZero()
        {
            var organization = new OrganizationState();
            for (int i = 0; i < 10; i++) organization.ApplyConditionFailed();

            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), organization);
            var result = evaluator.EvaluateAutomaticEndings("player", new List<string>());

            Assert.AreEqual(GameEndingType.OrganizationTrustZero, result);
        }

        [Test]
        public void Evaluate_ArrestTakesPriorityOverOrganizationTrustZero()
        {
            var suspicion = new PoliceSuspicionService();
            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f });

            var organization = new OrganizationState();
            for (int i = 0; i < 10; i++) organization.ApplyConditionFailed();

            var evaluator = new GameEndEvaluator(suspicion, organization);
            var result = evaluator.EvaluateAutomaticEndings("player", new List<string>());

            Assert.AreEqual(GameEndingType.Arrest, result);
        }

        [Test]
        public void Evaluate_WithPoliceLifeStateService_AllDeceased_ReturnsPoliceAllEliminated()
        {
            var lifeState = new PoliceLifeStateService();
            foreach (var id in PoliceRoster.AllPoliceIds)
                lifeState.MarkDeceased(id);

            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState());
            var result = evaluator.EvaluateAutomaticEndings("player", lifeState);

            Assert.AreEqual(GameEndingType.PoliceAllEliminated, result);
        }

        [Test]
        public void Evaluate_WithPoliceLifeStateService_NotAllDeceased_ReturnsNone()
        {
            var lifeState = new PoliceLifeStateService();
            lifeState.MarkDeceased(PoliceRoster.PoliceA);

            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState());
            var result = evaluator.EvaluateAutomaticEndings("player", lifeState);

            Assert.AreEqual(GameEndingType.None, result);
        }

        [Test]
        public void Classifier_MainStoryEndings_ContinueAfterward()
        {
            Assert.IsTrue(GameEndingClassifier.ContinuesAfterEnding(GameEndingType.RebelRecruitAccepted));
            Assert.IsTrue(GameEndingClassifier.ContinuesAfterEnding(GameEndingType.BossHiddenEnding));
            Assert.IsFalse(GameEndingClassifier.ContinuesAfterEnding(GameEndingType.Arrest));
        }
    }
}
