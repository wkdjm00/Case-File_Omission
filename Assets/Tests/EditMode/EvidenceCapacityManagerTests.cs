using CaseFileOmission.Evidence;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class EvidenceCapacityManagerTests
    {
        [Test]
        public void TryAcquire_BelowLimit_Acquires()
        {
            var manager = new EvidenceCapacityManager();

            var result = manager.TryAcquire("npc-1", HolderType.Npc, "clue-1");

            Assert.AreEqual(EvidenceAcquisitionOutcome.Acquired, result.Outcome);
            Assert.AreEqual(1, manager.GetHeldClues("npc-1").Count);
        }

        [Test]
        public void TryAcquire_SameClueTwice_ReturnsAlreadyHeld()
        {
            var manager = new EvidenceCapacityManager();
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-1");

            var result = manager.TryAcquire("npc-1", HolderType.Npc, "clue-1");

            Assert.AreEqual(EvidenceAcquisitionOutcome.AlreadyHeld, result.Outcome);
            Assert.AreEqual(1, manager.GetHeldClues("npc-1").Count);
        }

        [Test]
        public void TryAcquire_NpcAtLimit_WithoutReplaceCallback_Rejects()
        {
            var manager = new EvidenceCapacityManager();
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-1");
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-2");
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-3"); // NPC 한도 3

            var result = manager.TryAcquire("npc-1", HolderType.Npc, "clue-4");

            Assert.AreEqual(EvidenceAcquisitionOutcome.RejectedAtCapacity, result.Outcome);
            Assert.AreEqual(3, manager.GetHeldClues("npc-1").Count, "한도 초과 시 자동으로 삭제·교체하지 않는다.");
        }

        [Test]
        public void TryAcquire_NpcAtLimit_ReplaceApproved_SwapsOldest()
        {
            var manager = new EvidenceCapacityManager();
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-1");
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-2");
            manager.TryAcquire("npc-1", HolderType.Npc, "clue-3");

            var result = manager.TryAcquire("npc-1", HolderType.Npc, "clue-4", (oldId, newId) => true);

            Assert.AreEqual(EvidenceAcquisitionOutcome.Replaced, result.Outcome);
            Assert.AreEqual("clue-1", result.ReplacedClueId);
            CollectionAssert.DoesNotContain(manager.GetHeldClues("npc-1"), "clue-1");
            CollectionAssert.Contains(manager.GetHeldClues("npc-1"), "clue-4");
        }

        [Test]
        public void TryAcquire_PoliceLimitIsFive()
        {
            var manager = new EvidenceCapacityManager();
            for (int i = 0; i < 5; i++)
                manager.TryAcquire("police-A", HolderType.Police, $"clue-{i}");

            var result = manager.TryAcquire("police-A", HolderType.Police, "clue-overflow");

            Assert.AreEqual(EvidenceAcquisitionOutcome.RejectedAtCapacity, result.Outcome);
            Assert.AreEqual(5, EvidenceCapacityManager.GetLimit(HolderType.Police));
            Assert.AreEqual(3, EvidenceCapacityManager.GetLimit(HolderType.Npc));
        }
    }
}
