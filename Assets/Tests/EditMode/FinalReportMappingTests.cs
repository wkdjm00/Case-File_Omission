using System.Linq;
using CaseFileOmission.MainCase;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class FinalReportMappingTests
    {
        private static MainCaseProgressTracker TrackerWithAll()
        {
            var tracker = new MainCaseProgressTracker();
            foreach (var clue in MainCaseClueNames.PersonalTruthClues
                         .Concat(MainCaseClueNames.SystemicTruthClues)
                         .Concat(MainCaseClueNames.PowerTruthClues)
                         .Concat(MainCaseClueNames.ExperimentTruthClues))
                tracker.AcquireClue(clue);

            while (tracker.TryAdvanceStage()) { }
            return tracker;
        }

        [Test]
        public void Template_HasFourteenBlanks_InOrder_MatchingOriginalTexts()
        {
            var blanks = FinalReportTemplate.Blanks;
            Assert.AreEqual(14, blanks.Count);
            for (int i = 0; i < 14; i++)
                Assert.AreEqual(i, blanks[i].BlankIndex);

            Assert.AreEqual("한 달", blanks[0].Text);
            Assert.AreEqual("경제적 수익", blanks[6].Text);
            Assert.AreEqual("최종 설계자의 구체적인 신원과 개인적인 동기", blanks[13].Text);
        }

        [Test]
        public void DefaultConfig_DeterminesOnlyBlanksWithDirectEvidence()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 5, 7, 8, 11, 12, 13 }, report.UndeterminedBlankIndices);
            Assert.AreEqual(MainCaseClueNames.ProductSalesSettlement, report.Blanks[6].RequiredClueName);
            Assert.AreEqual(BlankMatchMode.AllOf, report.Blanks[4].MatchMode);
        }

        [Test]
        public void Blanks0And1_ShareOneSentenceGroup()
        {
            var report = new FinalReportState();
            Assert.AreEqual(report.Blanks[0].GroupId, report.Blanks[1].GroupId);
            CollectionAssert.AreEqual(new[] { 0, 1 }, report.CurrentGroupBlankIndices);
        }

        [Test]
        public void WrongInput_DoesNotChangeState()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            var outcome = report.TryFillBlank(6, "관람권 수입 정산표");

            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, outcome);
            Assert.IsFalse(report.Blanks[6].IsFilled);
            Assert.AreEqual(0, report.Blanks[6].CollectedClueNames.Count);
        }

        [Test]
        public void AnyOfBlank_EitherAcceptedClueFills()
        {
            var a = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            var b = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            Assert.AreEqual(FillBlankOutcome.Filled, a.TryFillBlank(3, MainCaseClueNames.FamilyNotificationRecord));
            Assert.AreEqual(FillBlankOutcome.Filled, b.TryFillBlank(3, MainCaseClueNames.NieceDeathReport));
        }

        [Test]
        public void AllOfBlank_RequiresBothClues()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            Assert.AreEqual(FillBlankOutcome.PartiallyFilled, report.TryFillBlank(4, MainCaseClueNames.NieceDiary));
            Assert.IsFalse(report.Blanks[4].IsFilled);
            Assert.AreEqual(FillBlankOutcome.AlreadyCollected, report.TryFillBlank(4, MainCaseClueNames.NieceDiary));
            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(4, MainCaseClueNames.NieceDeathReport));
            Assert.IsTrue(report.Blanks[4].IsFilled);
        }

        [Test]
        public void AliasAndNormalization_AreAccepted()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            // 문서 제목 표기(별칭) + 앞뒤 공백
            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(3, "  가족 통지 및 사고 처리 기록 "));

            // 'ㆍ'(한글 아래아)로 입력한 가운뎃점
            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(9, "상류층 접대ㆍ후원 기록"));
        }

        [Test]
        public void DecomposedHangulInput_IsNormalized()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            string decomposed = MainCaseClueNames.ProductSalesSettlement.Normalize(System.Text.NormalizationForm.FormD);

            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(6, decomposed));
        }

        [Test]
        public void WhitespaceIsNotIgnoredByDefault()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, report.TryFillBlank(6, "상품판매정산표"));
        }

        [Test]
        public void SubmitInput_WhileComposing_IsIgnored()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.IgnoredWhileComposing, report.SubmitInput("한 달", isComposing: true));
        }

        [Test]
        public void SubmitInput_CurrentGroupUndetermined_ReturnsBlankNotConfigured()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.BlankNotConfigured, report.SubmitInput(MainCaseClueNames.ParticipantOperatingRulesNotice));
        }

        [Test]
        public void ClueNotAcquired_IsRejected()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault(), clue => false);
            Assert.AreEqual(FillBlankOutcome.ClueNotAcquired, report.TryFillBlank(6, MainCaseClueNames.ProductSalesSettlement));
            Assert.IsFalse(report.Blanks[6].IsFilled);
        }

        [Test]
        public void Typewriter_StopsAtFirstOpenBlank_AndRevealsFilledText()
        {
            var report = new FinalReportState();
            string before = report.GetTypedTextUntilCurrentStop();
            StringAssert.EndsWith("약 ", before);

            report.ConfigureBlankAnswer(0, "x");
            report.TryFillBlank(0, "x");
            string after = report.GetTypedTextUntilCurrentStop();
            StringAssert.EndsWith("약 한 달 동안 진행되어 최종적으로 ", after);
        }

        [Test]
        public void FullCompletion_ProducesFullText_AndRaisesCompletedOnce()
        {
            var report = new FinalReportState();
            for (int i = 0; i < 14; i++)
                report.ConfigureBlankAnswer(i, $"clue-{i}");

            int raised = 0;
            report.Completed += () => raised++;

            for (int i = 0; i < 14; i++)
                Assert.AreEqual(FillBlankOutcome.Filled, report.SubmitInput($"clue-{i}"));

            Assert.AreEqual(1, raised);
            string text = report.GetCompletedText();
            StringAssert.Contains("약 한 달 동안 진행되어 최종적으로 1명만 생존하는", text);
            StringAssert.EndsWith("보고서 작성 종료.", text);
        }

        [Test]
        public void Coordinator_LockedBeforeDesignerStage()
        {
            var coordinator = new MainCaseFinalReportCoordinator(
                new MainCaseProgressTracker(), new PostReportBranching(), FinalReportAnswerConfig.CreateDefault());

            Assert.AreEqual(FillBlankOutcome.ReportLocked, coordinator.SubmitInput("아무거나"));
        }

        [Test]
        public void Coordinator_CompletionTriggersPhoneCall()
        {
            var tracker = TrackerWithAll();
            var branching = new PostReportBranching();
            var config = FinalReportAnswerConfig.CreateDefault();
            foreach (var entry in config.Entries.Where(e => !e.IsDetermined))
            {
                entry.IsDetermined = true;
                entry.AcceptedClueNames.Clear();
                entry.AcceptedClueNames.Add(MainCaseClueNames.ProductPurchaseWishList); // 테스트용 임시 정답
            }

            var coordinator = new MainCaseFinalReportCoordinator(tracker, branching, config);
            Assert.IsTrue(coordinator.CanWriteFinalReport);

            foreach (var blank in coordinator.Report.Blanks)
            {
                foreach (var name in blank.AcceptedClueNames)
                    coordinator.Report.TryFillBlank(blank.Index, name);
            }

            Assert.IsTrue(coordinator.Report.IsComplete);
            Assert.AreEqual(PhoneCallStage.AwaitingAnswer, branching.PhoneStage);
        }
    }
}
