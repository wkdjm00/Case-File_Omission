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
        public void GenerateValidCandidate_WhenAlwaysInvalid_ThrowsAfterMaxAttempts()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(1));
            var validator = new RejectEverythingValidator();
            var policy = new ContractRetryPolicy(generator, validator, maxAttemptsPerCandidate: 3);

            Assert.Throws<ContractGenerationFailedException>(() =>
                policy.GenerateValidCandidate(() => "contract-x"));

            Assert.AreEqual(3, validator.CallCount, "후보당 최대 3회 시도 규칙을 따라야 한다.");
        }
    }
}
