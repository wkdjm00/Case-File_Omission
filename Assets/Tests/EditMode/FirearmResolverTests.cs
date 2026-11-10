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
        public void Reload_DoesNotCreateAmmoBeyondThreePerContract()
        {
            // 06 확정: 계약당 탄약 3발 — 재장전으로 총량이 늘어나지 않는다.
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));
            resolver.Fire(firearm, "player", "target-1", false, false, false, false);

            Assert.IsFalse(firearm.Reload(), "장탄수 3이면 장전할 예비 탄이 없다.");
            Assert.AreEqual(2, firearm.CurrentAmmo);
        }

        [Test]
        public void Reload_WithSmallerMagazine_LoadsFromSameThreeRounds()
        {
            var firearm = new FirearmState(magazineCapacity: 2);
            var resolver = new FirearmResolver(new Random(1));
            resolver.Fire(firearm, "player", "t", false, false, false, false);
            resolver.Fire(firearm, "player", "t", false, false, false, false);

            Assert.IsTrue(firearm.Reload());
            Assert.AreEqual(1, firearm.CurrentAmmo);
            Assert.AreEqual(0, firearm.ReserveAmmo);
            resolver.Fire(firearm, "player", "t", false, false, false, false);
            Assert.IsFalse(firearm.CanFire);
            Assert.IsFalse(firearm.Reload());
        }

        [Test]
        public void ResetForContract_RestoresThreeRounds()
        {
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));
            for (int i = 0; i < 3; i++)
                resolver.Fire(firearm, "player", "t", false, false, false, false);

            firearm.ResetForContract();

            Assert.AreEqual(3, firearm.CurrentAmmo + firearm.ReserveAmmo);
        }

        [Test]
        public void Fire_RecordsEventLocationAndLogsOnce()
        {
            var firearm = new FirearmState();
            var resolver = new FirearmResolver(new Random(1));
            var log = new CaseFileOmission.Core.GameEventLog();

            var shot = resolver.Fire(firearm, "player", "t", true, false, false, false, roomId: "kitchen", eventLog: log, contractId: "c1");

            Assert.AreEqual("kitchen", shot.RoomId);
            Assert.IsTrue(log.HasProcessed(shot.EventId));
        }

        [TestCase(false, true, true, true, 10f)]
        [TestCase(true, false, false, false, 85f)]
        public void CalculateHitChance_ClampsToConfirmedRange(bool close, bool unstable, bool fast, bool blocked, float expected)
        {
            var resolver = new FirearmResolver(new Random(1));
            Assert.AreEqual(expected, resolver.CalculateHitChance(close, unstable, fast, blocked));
        }
    }
}
