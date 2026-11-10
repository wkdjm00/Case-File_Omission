using System;
using CaseFileOmission.Evidence;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class EvidenceManipulationOutcomeResolverTests
    {
        private static ClueRepository BuildRepositoryWithClues(params string[] clueIds)
        {
            var repo = new ClueRepository();
            foreach (var id in clueIds)
                repo.Discover(new ClueDefinition(id, "contract-1"));
            return repo;
        }

        [Test]
        public void Attempt_NoModifiers_UsesBaseSeventyPercent()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1));

            var result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player",
                targetIsSuspicious: false, witnessedByOthers: false, targetAlreadyConfirmedClue: false, targetUnawareOfFacts: false);

            Assert.AreEqual(70f, result.ProbabilityUsed);
        }

        [Test]
        public void BaseChance_IsConfirmedSeventyPercent()
        {
            // 사용자 확정(2026-11-10 C3): 물리 증거 조작 성공 확률 70%. 거짓 알리바이 확률(70%)과는 별개 규칙이다.
            Assert.AreEqual(70f, EvidenceManipulationOutcomeResolver.BaseChance);
            Assert.AreEqual(5f, EvidenceManipulationOutcomeResolver.MinChance);
            Assert.AreEqual(95f, EvidenceManipulationOutcomeResolver.MaxChance);
        }

        [Test]
        public void Attempt_BeforeUnlock_IsLocked_WithoutRollOrRecord()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1)) { UnlockCondition = () => false };

            var result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player", false, false, false, false);

            Assert.AreEqual(ManipulationOutcome.Locked, result.Outcome);
            Assert.AreEqual(0, service.History.Count);
            Assert.AreEqual(0, resolver.FailedAttempts.Count);
        }

        [Test]
        public void Attempt_AllNegativeModifiers_LowersChance()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1));

            var result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player",
                targetIsSuspicious: true, witnessedByOthers: true, targetAlreadyConfirmedClue: true, targetUnawareOfFacts: false);

            Assert.AreEqual(25f, result.ProbabilityUsed); // 70 - 15 - 20 - 10
        }

        [Test]
        public void Attempt_UnawareTarget_RaisesChance()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1));

            var result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player",
                targetIsSuspicious: false, witnessedByOthers: false, targetAlreadyConfirmedClue: false, targetUnawareOfFacts: true);

            Assert.AreEqual(80f, result.ProbabilityUsed);
        }

        [Test]
        public void Attempt_Success_AppliesDistortThroughExistingService()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            // 확률 100%에 가깝게(모디파이어 없이 70, random 항상 0 미만 비교가 성립하도록 시드 조정은 불확실하므로
            // 극단적으로 유리한 조건(unaware=true, 나머지 false)으로 70+10=80%를 만든 뒤 반복 시도로 성공 케이스를 찾는다.
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1));

            ManipulationOutcomeResult result = null;
            for (int i = 0; i < 50 && (result == null || result.Outcome != ManipulationOutcome.Succeeded); i++)
            {
                result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player",
                    false, false, false, true);
            }

            Assert.IsNotNull(result);
            if (result.Outcome == ManipulationOutcome.Succeeded)
            {
                Assert.IsNotNull(result.AppliedRecord);
                Assert.AreEqual(1, service.History.Count);
            }
        }

        [Test]
        public void Attempt_Failure_DoesNotTouchManipulationServiceHistory_ButRecordsAttempt()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));
            var resolver = new EvidenceManipulationOutcomeResolver(service, new Random(1));

            int successCount = 0;
            int failureCount = 0;
            const int attempts = 50;
            for (int i = 0; i < attempts; i++)
            {
                var result = resolver.Attempt(ManipulationType.Distort, "clue-1", "player",
                    true, true, true, false); // 25%로 낮춘 조건을 반복해 성공·실패를 모두 관찰한다.

                if (result.Outcome == ManipulationOutcome.Succeeded)
                {
                    successCount++;
                    Assert.IsNotNull(result.AppliedRecord);
                }
                else
                {
                    failureCount++;
                    Assert.IsNull(result.AppliedRecord, "실패하면 원본에 아무 변경도 적용되지 않아야 한다.");
                }
            }

            Assert.Greater(failureCount, 0, "25% 확률로 50회 시도했는데 한 번도 실패하지 않는 것은 비정상적으로 높은 확률이다.");
            Assert.AreEqual(successCount, service.History.Count, "성공한 시도 수만큼만 실제 조작 이력에 남아야 한다.");
            Assert.AreEqual(failureCount, resolver.FailedAttempts.Count, "실패한 시도 수만큼 시도 기록이 남아야 한다.");
        }
    }
}
