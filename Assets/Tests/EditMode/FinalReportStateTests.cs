using CaseFileOmission.MainCase;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class FinalReportStateTests
    {
        [Test]
        public void HasExactlyFourteenBlanks()
        {
            var report = new FinalReportState();
            Assert.AreEqual(14, report.Blanks.Count);
        }

        [Test]
        public void FirstBlank_MatchesOriginalText()
        {
            var report = new FinalReportState();
            Assert.AreEqual("한 달", report.Blanks[0].RevealedText);
        }

        [Test]
        public void LastBlank_MatchesOriginalText()
        {
            var report = new FinalReportState();
            Assert.AreEqual("최종 설계자의 구체적인 신원과 개인적인 동기", report.Blanks[13].RevealedText);
        }

        [Test]
        public void TryFillBlank_NotConfigured_ReturnsBlankNotConfigured()
        {
            var report = new FinalReportState();

            var outcome = report.TryFillBlank(0, "아무 단서");

            Assert.AreEqual(FillBlankOutcome.BlankNotConfigured, outcome);
        }

        [Test]
        public void ConfigureBlankAnswer_ThenCorrectInput_Fills()
        {
            var report = new FinalReportState();
            report.ConfigureBlankAnswer(0, "참가자 운영 규칙 안내문");

            var outcome = report.TryFillBlank(0, "참가자 운영 규칙 안내문");

            Assert.AreEqual(FillBlankOutcome.Filled, outcome);
            Assert.IsTrue(report.Blanks[0].IsFilled);
        }

        [Test]
        public void ConfigureBlankAnswer_ThenWrongInput_ReturnsIncorrect()
        {
            var report = new FinalReportState();
            report.ConfigureBlankAnswer(0, "참가자 운영 규칙 안내문");

            var outcome = report.TryFillBlank(0, "다른 단서");

            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, outcome);
            Assert.IsFalse(report.Blanks[0].IsFilled);
        }

        [Test]
        public void TryFillBlank_AlreadyFilled_ReturnsAlreadyFilled()
        {
            var report = new FinalReportState();
            report.ConfigureBlankAnswer(0, "정답");
            report.TryFillBlank(0, "정답");

            var outcome = report.TryFillBlank(0, "정답");

            Assert.AreEqual(FillBlankOutcome.AlreadyFilled, outcome);
        }

        [Test]
        public void IsComplete_FalseUntilAllBlanksFilled()
        {
            var report = new FinalReportState();
            for (int i = 0; i < 14; i++)
                report.ConfigureBlankAnswer(i, $"clue-{i}");

            Assert.IsFalse(report.IsComplete);

            for (int i = 0; i < 13; i++)
                report.TryFillBlank(i, $"clue-{i}");
            Assert.IsFalse(report.IsComplete, "13개만 채웠을 때는 아직 완료가 아니어야 한다.");

            report.TryFillBlank(13, "clue-13");
            Assert.IsTrue(report.IsComplete);
        }
    }
}
