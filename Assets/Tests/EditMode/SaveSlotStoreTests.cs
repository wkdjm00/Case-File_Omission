using System;
using System.IO;
using CaseFileOmission.Game;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>06 「2026-11-13 저장/불러오기·버전·복구 구현 계약」: 수동 3 + 자동 1, 서로 덮어쓰지 않음, 백업 복구, 순번.</summary>
    public class SaveSlotStoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "cfo_slots_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private static GameSession NewGame()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(7) });
            s.StartNewGame();
            s.CompletePrologue();
            return s;
        }

        [Test]
        public void ManualAndAutoSlots_AreIndependent_AndSequenceIncreases()
        {
            var store = new SaveSlotStore(_dir);
            var s = NewGame();
            Assert.IsTrue(s.SaveToSlot(store, 1));
            s.OfferContract();
            s.AcceptContract();
            Assert.IsTrue(s.SaveToSlot(store, SaveSlotStore.AutoSlot));

            Assert.IsTrue(store.TryRead(1, out var manual));
            Assert.IsTrue(store.TryRead(SaveSlotStore.AutoSlot, out var auto));
            Assert.AreEqual(1, manual.Slot);
            Assert.AreEqual(SaveSlotStore.AutoSlot, auto.Slot);
            Assert.AreEqual(Core.GamePhase.Idle, manual.Phase, "자동 저장이 수동 슬롯을 덮어쓰지 않는다.");
            Assert.AreEqual(Core.GamePhase.Investigation, auto.Phase);
            Assert.Greater(auto.SaveSequence, manual.SaveSequence);
            Assert.AreEqual(manual.RunId, auto.RunId);
            Assert.IsFalse(store.Exists(2));
        }

        [Test]
        public void CorruptMainFile_FallsBackToBackup()
        {
            var store = new SaveSlotStore(_dir);
            var s = NewGame();
            s.SaveToSlot(store, 2);
            s.SaveToSlot(store, 2); // 두 번째 저장 때 첫 저장이 백업이 된다
            File.WriteAllText(store.PathOf(2), "{ 깨진 파일");

            var logger = new Core.GameLogger();
            Assert.IsTrue(store.TryRead(2, out var data, logger));
            Assert.AreEqual(2, data.Slot);
            Assert.That(logger.Entries.Count, Is.GreaterThan(0));
        }

        [Test]
        public void LoadedGame_ContinuesSaveSequence()
        {
            var store = new SaveSlotStore(_dir);
            var s = NewGame();
            s.SaveToSlot(store, 3);
            s.SaveToSlot(store, 3);
            store.TryRead(3, out var data);
            var loaded = GameSession.Load(data, new GameSessionConfig { Random = new Random(1) });
            loaded.SaveToSlot(store, 1);
            store.TryRead(1, out var next);
            Assert.AreEqual(data.SaveSequence + 1, next.SaveSequence);
            Assert.AreEqual(data.RunId, next.RunId);
        }
    }
}
