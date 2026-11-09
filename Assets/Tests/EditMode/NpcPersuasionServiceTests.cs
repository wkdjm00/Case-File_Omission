using System;
using System.Collections.Generic;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class NpcPersuasionServiceTests
    {
        [Test]
        public void TryPersuade_WithoutKnowingCulprit_Throws()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            var service = new NpcPersuasionService(new Random(1));

            Assert.Throws<InvalidOperationException>(() =>
                service.TryPersuade(target, false, false, false, false, new HashSet<string>()));
        }

        [Test]
        public void TryPersuade_Success_MarksCooperatingAndRevealsUnsharedInfo()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            target.LearnInfo("info-a", NpcJudgement.Believe);
            target.LearnInfo("info-b", NpcJudgement.Believe);

            var alreadyShared = new HashSet<string> { "info-a" };
            var service = new NpcPersuasionService(new Random(1));

            var result = service.TryPersuade(target, true, hasStrongSupportingEvidence: true,
                hasUnfavorableDirectEvidence: false, targetAlreadyTrustsPlayer: true, alreadyShared);

            if (result.Success)
            {
                Assert.IsTrue(target.IsCooperatingWithPlayer);
                Assert.Contains("info-b", result.RevealedInfoIds);
                Assert.IsFalse(result.RevealedInfoIds.Contains("info-a"), "이미 공유된 정보는 다시 노출하지 않는다.");
            }
        }

        [Test]
        public void TryPersuade_ProbabilityStaysWithinBounds()
        {
            var target = new NpcState("npc-1", "contract-1", NpcActualRole.Perpetrator, NpcPersonality.Guilty);
            var service = new NpcPersuasionService(new Random(1));

            var result = service.TryPersuade(target, true, false, true, false, new HashSet<string>());

            Assert.GreaterOrEqual(result.ProbabilityUsed, 5f);
            Assert.LessOrEqual(result.ProbabilityUsed, 95f);
        }
    }
}
