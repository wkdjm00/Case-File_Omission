using CaseFileOmission.Core;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class GameLoggerTests
    {
        [Test]
        public void LogError_RecordsAllFields()
        {
            var logger = new GameLogger();

            logger.LogError(
                errorCode: "CLUE_DUPLICATE",
                system: "Evidence",
                phase: GamePhase.Investigation,
                cause: "동일 clueId 재등록 시도",
                recoverable: true,
                contractId: "c1",
                npcId: "npc-1",
                clueId: "clue-1");

            Assert.AreEqual(1, logger.Entries.Count);
            var entry = logger.Entries[0];
            Assert.AreEqual("CLUE_DUPLICATE", entry.ErrorCode);
            Assert.AreEqual("Evidence", entry.System);
            Assert.AreEqual(GamePhase.Investigation, entry.Phase);
            Assert.AreEqual("c1", entry.ContractId);
            Assert.AreEqual("npc-1", entry.NpcId);
            Assert.AreEqual("clue-1", entry.ClueId);
            Assert.IsTrue(entry.Recoverable);
        }

        [Test]
        public void LogError_MultipleEntries_AreKeptInOrder()
        {
            var logger = new GameLogger();

            logger.LogError("E1", "Core", GamePhase.Idle, "first", true);
            logger.LogError("E2", "Core", GamePhase.Idle, "second", false);

            Assert.AreEqual(2, logger.Entries.Count);
            Assert.AreEqual("E1", logger.Entries[0].ErrorCode);
            Assert.AreEqual("E2", logger.Entries[1].ErrorCode);
            Assert.IsFalse(logger.Entries[1].Recoverable);
        }
    }
}
