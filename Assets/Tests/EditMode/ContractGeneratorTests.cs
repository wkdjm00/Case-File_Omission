using System;
using System.Collections.Generic;
using CaseFileOmission.Contracts;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractGeneratorTests
    {
        [Test]
        public void PickPrimaryCrimeType_LargeSample_MatchesConfirmedWeights()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(12345));

            var counts = new Dictionary<CrimeType, int>();
            const int sampleSize = 20000;
            for (int i = 0; i < sampleSize; i++)
            {
                var type = generator.PickPrimaryCrimeType();
                counts.TryGetValue(type, out int current);
                counts[type] = current + 1;
            }

            AssertRatioNear(counts, CrimeType.Murder, 0.30f, sampleSize);
            AssertRatioNear(counts, CrimeType.Missing, 0.30f, sampleSize);
            AssertRatioNear(counts, CrimeType.AssaultInjury, 0.20f, sampleSize);
            AssertRatioNear(counts, CrimeType.TheftRobbery, 0.12f, sampleSize);
            AssertRatioNear(counts, CrimeType.Blackmail, 0.08f, sampleSize);
        }

        [Test]
        public void PickSubCrimeType_LargeSample_MatchesConfirmedWeights()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(54321));

            var counts = new Dictionary<CrimeType, int>();
            const int sampleSize = 20000;
            for (int i = 0; i < sampleSize; i++)
            {
                var type = generator.PickSubCrimeType();
                counts.TryGetValue(type, out int current);
                counts[type] = current + 1;
            }

            AssertRatioNear(counts, CrimeType.TheftRobbery, 0.30f, sampleSize);
            AssertRatioNear(counts, CrimeType.Blackmail, 0.30f, sampleSize);
            AssertRatioNear(counts, CrimeType.AssaultInjury, 0.20f, sampleSize);
            AssertRatioNear(counts, CrimeType.Murder, 0.10f, sampleSize);
            AssertRatioNear(counts, CrimeType.Missing, 0.10f, sampleSize);
        }

        private static void AssertRatioNear(Dictionary<CrimeType, int> counts, CrimeType type, float expectedRatio, int sampleSize)
        {
            counts.TryGetValue(type, out int count);
            float ratio = (float)count / sampleSize;
            Assert.AreEqual(expectedRatio, ratio, 0.02f, $"{type} 비율이 허용 오차를 벗어났습니다. 실제: {ratio}");
        }

        [Test]
        public void RollOrganizationCondition_ApproximatesConfiguredChance()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(777));

            int assignedCount = 0;
            const int sampleSize = 20000;
            for (int i = 0; i < sampleSize; i++)
            {
                if (generator.RollOrganizationCondition())
                    assignedCount++;
            }

            float ratio = (float)assignedCount / sampleSize;
            Assert.AreEqual(0.4f, ratio, 0.02f);
        }

        [Test]
        public void CreateCandidate_AlwaysIndoorHouse_OutdoorExcludedByUserInstruction()
        {
            // 사용자 지시(2026-11-09): 의뢰 사건 공간에서 실외를 제외한다.
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(1));

            for (int i = 0; i < 200; i++)
            {
                var candidate = generator.CreateCandidate($"contract-{i}");
                Assert.AreEqual(LocationCategory.Indoor, candidate.LocationCategory);
                Assert.AreEqual(StubLocation.House, candidate.Location);
            }
        }

        [Test]
        public void CreateCandidate_OrganizationConditionRate_StaysAtFortyPercent_AndNeverInfeasible()
        {
            // 05 확정: 부여가 정해지면 실행 가능한 조건 중에서 고른다 — 불가능 조합 때문에 조건 없음으로 떨어지지 않는다.
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(2024));

            int withCondition = 0;
            const int sampleSize = 20000;
            for (int i = 0; i < sampleSize; i++)
            {
                var candidate = generator.CreateCandidate($"c{i}");
                if (!candidate.OrgCondition.HasCondition) continue;
                withCondition++;
                Assert.IsTrue(ContractGenerator.IsConditionFeasible(candidate.OrgCondition.Type, candidate));
            }

            Assert.AreEqual(0.4f, (float)withCondition / sampleSize, 0.02f);
        }

        [Test]
        public void PickFeasibleCondition_AvoidsImmediateRepeat_WhenAlternativeExists()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(5));
            var contract = generator.CreateCandidate("c1");

            for (int i = 0; i < 200; i++)
            {
                var picked = generator.PickFeasibleCondition(contract, OrganizationConditionType.KeepUnsolved);
                Assert.AreNotEqual(OrganizationConditionType.KeepUnsolved, picked);
            }
        }

        [Test]
        public void CreateCandidate_TruthIsFixedBeforeElements()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generator = new ContractGenerator(config, new Random(8));
            var candidate = generator.CreateCandidate("c9");

            Assert.AreEqual(ContractGenerator.PerpetratorIdFor("c9"), candidate.Truth.PerpetratorId);
            Assert.AreEqual(ContractGenerator.VictimIdFor("c9"), candidate.Truth.VictimId);
            Assert.IsFalse(string.IsNullOrEmpty(candidate.Truth.RelationshipType));
        }

        [Test]
        public void CreateCandidate_WithSameSeed_IsDeterministic()
        {
            var config = ContractGenerationConfig.CreateDefault();
            var generatorA = new ContractGenerator(config, new Random(999));
            var generatorB = new ContractGenerator(config, new Random(999));

            var candidateA = generatorA.CreateCandidate("c1");
            var candidateB = generatorB.CreateCandidate("c1");

            Assert.AreEqual(candidateA.PrimaryCrimeType, candidateB.PrimaryCrimeType);
            Assert.AreEqual(candidateA.LocationCategory, candidateB.LocationCategory);
            Assert.AreEqual(candidateA.OrgCondition.Type, candidateB.OrgCondition.Type);
        }
    }
}
