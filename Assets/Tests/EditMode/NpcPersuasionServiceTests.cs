using System;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class NpcPersuasionServiceTests
    {
        private sealed class FixedRandom : Random
        {
            private readonly double _value;
            public FixedRandom(double value) { _value = value; }
            public override double NextDouble() => _value;
        }

        [Test]
        public void TryPersuade_WithoutKnowingCulprit_Throws()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            var service = new NpcPersuasionService(new Random(1));

            Assert.Throws<InvalidOperationException>(() => service.TryPersuade(target, false, false, false, false));
        }

        [Test]
        public void TryPersuade_NonPerpetrator_Throws()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Witness, NpcPersonality.Guilty);
            var service = new NpcPersuasionService(new Random(1));

            Assert.Throws<InvalidOperationException>(() => service.TryPersuade(target, true, false, false, false),
                "07 확정: 회유 대상은 실제 가해자뿐이다.");
        }

        [Test]
        public void TryPersuade_Success_MarksCooperatingAndRevealsAllKnownInfo()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            target.LearnInfo("info-a", NpcJudgement.Believe);
            target.LearnInfo("info-b", NpcJudgement.Believe);
            var service = new NpcPersuasionService(new FixedRandom(0.0));

            var result = service.TryPersuade(target, true, hasStrongSupportingEvidence: true,
                hasUnfavorableDirectEvidence: false, targetAlreadyTrustsPlayer: true);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(target.IsCooperatingWithPlayer);
            CollectionAssert.AreEquivalent(new[] { "info-a", "info-b" }, result.RevealedInfoIds,
                "이전에 공유한 정보까지 포함해 보유 정보 전부를 공개한다.");
        }

        [Test]
        public void TryPersuade_Failure_RevealsNothing()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            target.LearnInfo("info-a", NpcJudgement.Believe);
            var service = new NpcPersuasionService(new FixedRandom(0.999));

            var result = service.TryPersuade(target, true, false, false, false);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(target.IsCooperatingWithPlayer);
            CollectionAssert.IsEmpty(result.RevealedInfoIds);
        }

        [TestCase(true, false, false, 70f)]
        [TestCase(false, true, false, 35f)]
        [TestCase(false, false, true, 60f)]
        [TestCase(true, false, true, 80f)]
        public void TryPersuade_UsesConfirmedModifiers(bool strong, bool unfavorable, bool trusts, float expected)
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            var service = new NpcPersuasionService(new FixedRandom(0.999));

            var result = service.TryPersuade(target, true, strong, unfavorable, trusts);

            Assert.AreEqual(expected, result.ProbabilityUsed, 0.001f);
        }
    }
}
