using System;
using CaseFileOmission.Contracts;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractRetryPolicyTests
    {
        private sealed class RejectEverythingValidator : IContractValidator
        {
            public int CallCount { get; private set; }

            public ContractValidationResult Validate(ContractInstance contract)
            {
                CallCount++;
                var result = new ContractValidationResult();
                result.Errors.Add(ContractValidationErrorCode.CONTRADICTION);
                return result;
            }
        }

        [Test]
        public void GenerateValidCandidate_ReturnsValidContractWithinAttempts()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(42));
            var validator = new ContractValidator();
            var policy = new ContractRetryPolicy(generator, validator, maxAttemptsPerCandidate: 3);

            int counter = 0;
            var candidate = policy.GenerateValidCandidate(() => $"contract-{counter++}");

            Assert.IsNotNull(candidate);
            Assert.IsTrue(new ContractValidator().Validate(candidate).IsValid);
        }

        [Test]
        public void GenerateValidCandidate_WhenAlwaysInvalid_DiscardsEachCandidateAfterThreeAttempts_ThenFails()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(1));
            var validator = new RejectEverythingValidator();
            var logger = new CaseFileOmission.Core.GameLogger();
            var policy = new ContractRetryPolicy(generator, validator, maxAttemptsPerCandidate: 3, maxCandidates: 4, logger: logger);

            int counter = 0;
            Assert.Throws<ContractGenerationFailedException>(() =>
                policy.GenerateValidCandidate(() => $"contract-{counter++}"));

            Assert.AreEqual(12, validator.CallCount, "후보당 3회 시도 후 폐기하고 새 후보를 만들어야 한다.");
            Assert.AreEqual(4, counter, "폐기할 때마다 새 후보(새 ID)를 만들어야 한다.");
            Assert.AreEqual(4, policy.DiscardedCandidates);
            Assert.IsTrue(System.Linq.Enumerable.Any(logger.Entries, e => e.ErrorCode == "CONTRACT_GENERATION_FAILED"));
        }

        [Test]
        public void GenerateValidCandidate_RetryWithinCandidate_KeepsTruth()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(3));
            var policy = new ContractRetryPolicy(generator, new ContractValidator(), maxAttemptsPerCandidate: 3, maxCandidates: 1);

            ContractTruth firstTruth = null;
            int attempts = 0;
            var result = policy.GenerateValidCandidate(() => "c1", c =>
            {
                attempts++;
                firstTruth ??= c.Truth;
                var r = new ContractValidationResult();
                if (attempts < 3) r.Errors.Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                return r;
            });

            Assert.AreEqual(3, attempts);
            Assert.AreSame(firstTruth, result.Truth, "같은 후보 재시도에서는 실제 진실을 바꾸지 않는다.");
        }

        [Test]
        public void RetryWithinCandidate_KeepsCrimeTypeCoverUpAndCondition()
        {
            // 사용자 확정(2026-11-10 C2): 검증을 재시도해도 사건의 핵심 설정을 임의로 바꾸지 않는다.
            var generator = new ContractGenerator(ContractGenerationConfig.CreateDefault(), new Random(17));
            var policy = new ContractRetryPolicy(generator, new ContractValidator(), maxAttemptsPerCandidate: 3, maxCandidates: 1);

            var seen = new System.Collections.Generic.List<(CrimeType, CoverUpMethod, OrganizationConditionType)>();
            policy.TryGenerate(() => "c1", c =>
            {
                seen.Add((c.PrimaryCrimeType, c.Truth.CoverUp, c.OrgCondition.Type));
                var r = new ContractValidationResult();
                r.Errors.Add(ContractValidationErrorCode.TIMELINE_CONFLICT);
                return r;
            });

            Assert.AreEqual(3, seen.Count);
            Assert.AreEqual(seen[0], seen[1]);
            Assert.AreEqual(seen[0], seen[2]);
        }

        [Test]
        public void TryGenerate_AllTenCandidatesFail_ReturnsFailureAfterThirtyValidations()
        {
            var generator = new ContractGenerator(ContractGenerationConfig.CreateDefault(), new Random(2));
            var validator = new RejectEverythingValidator();
            var policy = new ContractRetryPolicy(generator, validator, maxAttemptsPerCandidate: 3, maxCandidates: 10);

            int ids = 0;
            var result = policy.TryGenerate(() => $"c{ids++}", validator.Validate);

            Assert.IsFalse(result.Success, "모두 실패하면 예외 없이 실패를 반환한다.");
            Assert.AreEqual(10, result.CandidatesUsed);
            Assert.AreEqual(30, result.ValidationAttempts);
            Assert.AreEqual(30, validator.CallCount);
            Assert.AreEqual(10, ids);
        }

        [Test]
        public void DefaultConfig_UsesTenCandidatesAndThreeAttempts()
        {
            var config = ContractGenerationConfig.CreateDefault();
            Assert.AreEqual(10, config.MaxCandidates);
            Assert.AreEqual(3, config.MaxAttemptsPerCandidate);
            Assert.AreEqual(5, config.MaxRetriesPerElement);
        }
    }
}
