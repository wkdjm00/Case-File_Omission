using System;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MeetingSessionTests
    {
        private static ClueRepository BuildRepositoryWithClues(params string[] clueIds)
        {
            var repo = new ClueRepository();
            foreach (var id in clueIds)
                repo.Discover(new ClueDefinition(id, "c1"));
            return repo;
        }

        [Test]
        public void PresentCard_Distort_AppliesThroughEvidenceService()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var evidenceService = new EvidenceManipulationService(repo, new Random(1));
            var session = new MeetingSession("c1", evidenceService);

            var card = session.PresentCard("evt-1", "player", "clue-1", CardManipulation.Distort);

            Assert.IsNotNull(card.ManipulationRecordApplied);
            Assert.AreEqual(ManipulationType.Distort, card.ManipulationRecordApplied.Type);
            Assert.AreEqual(1, evidenceService.History.Count);
        }

        [Test]
        public void PresentCard_None_DoesNotTouchEvidenceService()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var evidenceService = new EvidenceManipulationService(repo, new Random(1));
            var session = new MeetingSession("c1", evidenceService);

            var card = session.PresentCard("evt-1", "player", "clue-1", CardManipulation.None);

            Assert.IsNull(card.ManipulationRecordApplied);
            Assert.AreEqual(0, evidenceService.History.Count);
        }

        [Test]
        public void RecordTurnSkip_ConsecutiveCounts_MatchConfirmedTable()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            var first = session.RecordTurnSkip("evt-1", "police-A", "player");
            var second = session.RecordTurnSkip("evt-2", "police-A", "player");
            var third = session.RecordTurnSkip("evt-3", "police-A", "player");

            Assert.AreEqual(5f, first.Delta);
            Assert.AreEqual(8f, second.Delta);
            Assert.AreEqual(12f, third.Delta);
        }

        [Test]
        public void PresentCard_ResetsConsecutiveSkipForPresenter()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var evidenceService = new EvidenceManipulationService(repo, new Random(1));
            var session = new MeetingSession("c1", evidenceService);

            session.RecordTurnSkip("evt-1", "police-A", "player");
            session.RecordTurnSkip("evt-2", "police-A", "player"); // 연속 2회 -> 다음은 12여야 정상이나 카드 제시로 초기화

            session.PresentCard("evt-present", "player", "clue-1", CardManipulation.None);

            var afterReset = session.RecordTurnSkip("evt-3", "police-A", "player");
            Assert.AreEqual(5f, afterReset.Delta, "카드를 제시하면 연속 차례 넘기기 단계가 초기화되어야 한다.");
        }

        [Test]
        public void RecordAccepted_VerifiedBonus_AddsExtraDecrease()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            var result = session.RecordAccepted("evt-1", "police-A", "player", verified: true);

            Assert.AreEqual(-12f, result.Delta); // -4(1회) + -8(검증보너스)
        }

        [Test]
        public void RecordAccepted_ThenResponseFailed_ResetsAcceptStreak()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            session.RecordAccepted("evt-1", "police-A", "player", verified: false);
            session.RecordAccepted("evt-2", "police-A", "player", verified: false); // 연속 2회 -4,-7

            session.RecordResponseFailed("evt-3", "police-A", "player");

            var afterReset = session.RecordAccepted("evt-4", "police-A", "player", verified: false);
            Assert.AreEqual(-4f, afterReset.Delta, "대응 실패 후에는 수용 연속 단계가 초기화되어야 한다.");
        }

        [Test]
        public void RecordGiveUp_ConsecutiveCounts_MatchConfirmedTable()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            var first = session.RecordGiveUp("evt-1", "police-A", "player");
            var second = session.RecordGiveUp("evt-2", "police-A", "player");

            Assert.AreEqual(12f, first.Delta);
            Assert.AreEqual(15f, second.Delta);
        }

        [Test]
        public void DuplicateActionEventId_DoesNotReprocess()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            var first = session.RecordTurnSkip("evt-1", "police-A", "player");
            var duplicate = session.RecordTurnSkip("evt-1", "police-A", "player");

            Assert.IsFalse(first.WasDuplicateAction);
            Assert.IsTrue(duplicate.WasDuplicateAction);
            Assert.AreEqual(0f, duplicate.Delta);
        }

        [Test]
        public void EndSession_ThenAnyAction_Throws()
        {
            var repo = BuildRepositoryWithClues("clue-1");
            var session = new MeetingSession("c1", new EvidenceManipulationService(repo));

            session.EndSession();

            Assert.Throws<InvalidOperationException>(() => session.RecordTurnSkip("evt-1", "police-A", "player"));
        }
    }
}
