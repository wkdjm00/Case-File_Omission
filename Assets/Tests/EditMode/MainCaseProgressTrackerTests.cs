using CaseFileOmission.MainCase;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MainCaseProgressTrackerTests
    {
        [Test]
        public void StartsAtPersonalTruth()
        {
            var tracker = new MainCaseProgressTracker();
            Assert.AreEqual(MainCaseTruthStage.PersonalTruth, tracker.CurrentStage);
        }

        [Test]
        public void TryAdvanceStage_MissingClues_Fails()
        {
            var tracker = new MainCaseProgressTracker();

            bool advanced = tracker.TryAdvanceStage();

            Assert.IsFalse(advanced);
            Assert.AreEqual(MainCaseTruthStage.PersonalTruth, tracker.CurrentStage);
        }

        [Test]
        public void TryAdvanceStage_AllPersonalTruthCluesAcquired_AdvancesToSystemicTruth()
        {
            var tracker = new MainCaseProgressTracker();
            tracker.AcquireClue(MainCaseClueNames.NieceDiary);
            tracker.AcquireClue(MainCaseClueNames.NieceDeathReport);
            tracker.AcquireClue(MainCaseClueNames.FamilyNotificationRecord);

            bool advanced = tracker.TryAdvanceStage();

            Assert.IsTrue(advanced);
            Assert.AreEqual(MainCaseTruthStage.SystemicTruth, tracker.CurrentStage);
        }

        [Test]
        public void TryAdvanceStage_PartialClues_DoesNotAdvance()
        {
            var tracker = new MainCaseProgressTracker();
            tracker.AcquireClue(MainCaseClueNames.NieceDiary);
            // 사망 관련 보고서, 가족 통지 기록은 아직 미확보

            Assert.IsFalse(tracker.TryAdvanceStage());
        }

        [Test]
        public void TryAdvanceStage_AtDesignerTruth_NeverAdvancesFurther()
        {
            var tracker = new MainCaseProgressTracker();
            tracker.AcquireClue(MainCaseClueNames.NieceDiary);
            tracker.AcquireClue(MainCaseClueNames.NieceDeathReport);
            tracker.AcquireClue(MainCaseClueNames.FamilyNotificationRecord);
            tracker.TryAdvanceStage();

            foreach (var clue in MainCaseClueNames.SystemicTruthClues) tracker.AcquireClue(clue);
            tracker.TryAdvanceStage();

            foreach (var clue in MainCaseClueNames.PowerTruthClues) tracker.AcquireClue(clue);
            tracker.TryAdvanceStage();

            foreach (var clue in MainCaseClueNames.ExperimentTruthClues) tracker.AcquireClue(clue);
            tracker.TryAdvanceStage();

            Assert.AreEqual(MainCaseTruthStage.DesignerTruth, tracker.CurrentStage);
            Assert.IsFalse(tracker.TryAdvanceStage(), "설계자의 진실 단계는 확정상 전용 단서가 없어 더 진행되지 않아야 한다.");
        }
    }
}
