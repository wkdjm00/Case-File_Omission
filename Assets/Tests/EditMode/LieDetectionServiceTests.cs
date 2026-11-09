using System;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class LieDetectionServiceTests
    {
        [Test]
        public void AttemptLie_PoliceAlreadyConfirmedClue_ForcesDetection()
        {
            var repo = new ClueRepository();
            var record = repo.Discover(new ClueDefinition("clue-1", "contract-1"));
            record.MarkConfirmedByPolice("police-A");

            var service = new LieDetectionService(repo, new Random(1));
            var result = service.AttemptLie("clue-1", "police-A", ContradictionLevel.None, hasSupportingEvidence: true);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ForcedDetectionByConfirmedClue);
        }

        [Test]
        public void AttemptLie_NotConfirmedByThatPolice_UsesProbability()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));

            var service = new LieDetectionService(repo, new Random(1));
            var result = service.AttemptLie("clue-1", "police-B", ContradictionLevel.None, hasSupportingEvidence: false);

            Assert.IsFalse(result.ForcedDetectionByConfirmedClue);
            Assert.AreEqual(50f, result.ProbabilityUsed);
        }

        [Test]
        public void AttemptLie_ClearContradiction_LowersProbability()
        {
            var repo = new ClueRepository();
            var service = new LieDetectionService(repo, new Random(1));

            var result = service.AttemptLie("non-existent-clue", null, ContradictionLevel.Clear, hasSupportingEvidence: false);

            Assert.AreEqual(20f, result.ProbabilityUsed); // 50 - 30
        }

        [Test]
        public void AttemptLie_ProbabilityNeverBelowFloor()
        {
            var repo = new ClueRepository();
            var service = new LieDetectionService(repo, new Random(1));

            var result = service.AttemptLie("x", null, ContradictionLevel.Clear, hasSupportingEvidence: false);

            Assert.GreaterOrEqual(result.ProbabilityUsed, 5f);
        }
    }
}
