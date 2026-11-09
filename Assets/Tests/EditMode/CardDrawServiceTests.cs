using System;
using System.Collections.Generic;
using CaseFileOmission.Meeting;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class CardDrawServiceTests
    {
        [Test]
        public void DrawCards_MoreHeldThanLimit_DrawsExactlyLimit()
        {
            var held = new List<string> { "c1", "c2", "c3", "c4", "c5", "c6", "c7" };

            var drawn = CardDrawService.DrawCards(held, CardPresentationLimits.PlayerNormalTurnLimit, new Random(1));

            Assert.AreEqual(CardPresentationLimits.PlayerNormalTurnLimit, drawn.Count);
        }

        [Test]
        public void DrawCards_FewerHeldThanLimit_DrawsAllHeld()
        {
            var held = new List<string> { "c1", "c2" };

            var drawn = CardDrawService.DrawCards(held, CardPresentationLimits.PlayerNormalTurnLimit, new Random(1));

            Assert.AreEqual(2, drawn.Count);
        }

        [Test]
        public void DrawCards_NoDuplicatesInDrawnSet()
        {
            var held = new List<string> { "c1", "c2", "c3", "c4", "c5" };

            var drawn = CardDrawService.DrawCards(held, 5, new Random(1));

            Assert.AreEqual(5, new HashSet<string>(drawn).Count);
        }

        [Test]
        public void DrawCards_AllDrawnComeFromHeldSet()
        {
            var held = new List<string> { "c1", "c2", "c3" };

            var drawn = CardDrawService.DrawCards(held, 5, new Random(1));

            foreach (var id in drawn)
                CollectionAssert.Contains(held, id);
        }
    }
}
