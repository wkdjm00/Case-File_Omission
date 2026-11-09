using System;
using CaseFileOmission.Combat;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class FirearmResolverTests
    {
        [Test]
        public void CalculateHitChance_NoModifiers_ReturnsBase()
        {
            var resolver = new FirearmResolver(new Random(1));
            float chance = resolver.CalculateHitChance(false, false, false, false);
            Assert.AreEqual(70f, chance);
        }

        [Test]
        public void CalculateHitChance_AllModifiers_ClampedToFloor()
        {
            var resolver = new FirearmResolver(new Random(1));
            // 70 + 15 - 20 - 15 - 30 = 20, 범위 내이므로 그대로
            float chance = resolver.CalculateHitChance(true, true, true, true);
            Assert.AreEqual(20f, chance);
        }

        [Test]
        public void Fire_ConsumesAmmo()
        {
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));

            resolver.Fire(firearm, "player", "target-1", false, false, false, false);

            Assert.AreEqual(FirearmState.StartingAmmo - 1, firearm.CurrentAmmo);
        }

        [Test]
        public void Fire_WithoutAmmo_Throws()
        {
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));
            for (int i = 0; i < FirearmState.StartingAmmo; i++)
                resolver.Fire(firearm, "player", "target-1", false, false, false, false);

            Assert.Throws<InvalidOperationException>(() =>
                resolver.Fire(firearm, "player", "target-1", false, false, false, false));
        }

        [Test]
        public void Reload_RestoresFullAmmo()
        {
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));
            resolver.Fire(firearm, "player", "target-1", false, false, false, false);

            firearm.Reload();

            Assert.AreEqual(FirearmState.StartingAmmo, firearm.CurrentAmmo);
        }
    }
}
