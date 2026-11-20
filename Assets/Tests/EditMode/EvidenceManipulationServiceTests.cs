using System;
using System.Collections.Generic;
using CaseFileOmission.Evidence;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class EvidenceManipulationServiceTests
    {
        private static ClueRepository BuildRepositoryWithClues(params string[] clueIds)
        {
            var repo = new ClueRepository();
            foreach (var id in clueIds)
                repo.Discover(new ClueDefinition(id, "contract-1"));
            return repo;
        }

        [Test]
        public void Distort_KeepsSameClueIdAsDisplayed()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo, new Random(1));

            var result = service.Distort("clue-1", "player");

            Assert.AreEqual(ManipulationType.Distort, result.Type);
            Assert.AreEqual("clue-1", result.DisplayedClueId);
            Assert.AreEqual("clue-1", result.OriginalClueId);
        }

        [Test]
        public void Distort_UnknownClue_Throws()
        {
            var repo = new ClueRepository();
            var service = new EvidenceManipulationService(repo);

            Assert.Throws<KeyNotFoundException>(() => service.Distort("missing", "player"));
        }

        [Test]
        public void Conceal_PicksSubstituteExcludingConcealedClue()
        {
            var repo = BuildRepositoryWithClues("clue-1", "clue-2", "clue-3");
            var service = new EvidenceManipulationService(repo, new Random(42));
            var held = new List<string> { "clue-1", "clue-2", "clue-3" };

            for (int i = 0; i < 50; i++)
            {
                var result = service.Conceal("clue-1", "player", held);
                Assert.AreNotEqual("clue-1", result.DisplayedClueId, "대체 카드는 은폐 대상과 달라야 한다.");
                Assert.Contains(result.DisplayedClueId, held);
            }
        }

        [Test]
        public void Conceal_NoOtherHeldClue_ConcealsWithoutSubstitute()
        {
            // 06 확정: 대체 카드 묶음이 비면 보충하지 않는다 — 대체 없이 은폐만 기록한다.
            var repo = BuildRepositoryWithClues("clue-1");
            var service = new EvidenceManipulationService(repo);
            var held = new List<string> { "clue-1" };

            var record = service.Conceal("clue-1", "player", held);
            Assert.IsNull(record.DisplayedClueId);
            Assert.AreEqual(ManipulationType.Conceal, record.Type);
        }

        [Test]
        public void History_RecordsEachManipulationInOrder()
        {
            var repo = BuildRepositoryWithClues("clue-1", "clue-2");
            var service = new EvidenceManipulationService(repo, new Random(7));

            service.Distort("clue-1", "player");
            service.Conceal("clue-2", "player", new List<string> { "clue-1", "clue-2" });

            Assert.AreEqual(2, service.History.Count);
            Assert.AreEqual(ManipulationType.Distort, service.History[0].Type);
            Assert.AreEqual(ManipulationType.Conceal, service.History[1].Type);
        }
    }
}
