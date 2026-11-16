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
        public void DefaultConfig_DeterminesAllFourteenBlanks_PerNotion20261110()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            CollectionAssert.IsEmpty(report.UndeterminedBlankIndices);
            Assert.AreEqual(MainCaseClueNames.ParticipantOperatingRulesNotice, report.Blanks[0].RequiredClueName);
            Assert.AreEqual(MainCaseClueNames.ParticipantOperatingRulesNotice, report.Blanks[1].RequiredClueName);
            Assert.AreEqual(MainCaseClueNames.ParticipantRosterDeliveryConfirmation, report.Blanks[2].RequiredClueName);
            Assert.AreEqual(MainCaseClueNames.UpperClassHospitalityRecord, report.Blanks[5].RequiredClueName);
            Assert.AreEqual(MainCaseClueNames.ProductSalesSettlement, report.Blanks[6].RequiredClueName);
            // 8번: 참가자 명단 전달 확인서 + 정부 부서 연락 기록 종합(2026-11-10 원문, 2번과 공유).
            Assert.AreEqual(BlankMatchMode.AllOf, report.Blanks[8].MatchMode);
            CollectionAssert.AreEquivalent(new[]
            {
                MainCaseClueNames.ParticipantRosterDeliveryConfirmation, MainCaseClueNames.GovernmentDepartmentContactLog
            }, report.Blanks[8].AcceptedClueNames);
            Assert.AreEqual(MainCaseClueNames.RebelInternalCircularMemo, report.Blanks[11].RequiredClueName);
            Assert.AreEqual(MainCaseClueNames.RebelInternalCircularMemo, report.Blanks[12].RequiredClueName);

            Assert.AreEqual(BlankMatchMode.AllOf, report.Blanks[4].MatchMode);
            Assert.AreEqual(BlankMatchMode.AllOf, report.Blanks[7].MatchMode);
            CollectionAssert.AreEquivalent(new[]
            {
                MainCaseClueNames.UpperClassHospitalityRecord, MainCaseClueNames.ViewingTicketRevenueSettlement,
                MainCaseClueNames.ProductSalesSettlement
            }, report.Blanks[7].AcceptedClueNames);
            Assert.AreEqual(BlankMatchMode.AllOf, report.Blanks[13].MatchMode);
            CollectionAssert.AreEquivalent(new[]
            {
                MainCaseClueNames.RebelInternalCircularMemo, MainCaseClueNames.GovernmentDepartmentContactLog
            }, report.Blanks[13].AcceptedClueNames);
        }

        [Test]
        public void OneInput_FillsBlanks0And1Together()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            Assert.AreEqual(FillBlankOutcome.Filled, report.SubmitInput(MainCaseClueNames.ParticipantOperatingRulesNotice));

            Assert.IsTrue(report.Blanks[0].IsFilled);
            Assert.IsTrue(report.Blanks[1].IsFilled, "같은 단서를 다시 입력하도록 요구하지 않는다.");
            CollectionAssert.AreEqual(new[] { 2 }, report.CurrentGroupBlankIndices);
        }

        [Test]
        public void EarlierInput_CountsTowardLaterMultiClueBlank()
        {
            // 8번에서 입력한 정부 부서 연락 기록은 13번(반란 메모 + 정부 부서 연락 기록) 판정에도 반영된다.
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            report.TryFillBlank(8, MainCaseClueNames.GovernmentDepartmentContactLog);
            Assert.IsFalse(report.Blanks[13].IsFilled);
            CollectionAssert.Contains(report.Blanks[13].CollectedClueNames.ToList(), MainCaseClueNames.GovernmentDepartmentContactLog);

            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(11, MainCaseClueNames.RebelInternalCircularMemo));

            Assert.IsTrue(report.Blanks[11].IsFilled);
            Assert.IsTrue(report.Blanks[12].IsFilled, "11번과 같은 단서로 12번도 완성한다.");
            Assert.IsTrue(report.Blanks[13].IsFilled, "필요한 두 단서가 모두 입력되었으므로 13번도 완성된다.");
        }

        [Test]
        public void MultiClueBlank7_WaitsUntilAllThreeEntered()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());

            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(5, MainCaseClueNames.UpperClassHospitalityRecord));
            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(6, MainCaseClueNames.ProductSalesSettlement));
            Assert.IsFalse(report.Blanks[7].IsFilled);

            Assert.AreEqual(FillBlankOutcome.Filled, report.TryFillBlank(7, MainCaseClueNames.ViewingTicketRevenueSettlement));
            Assert.IsTrue(report.Blanks[7].IsFilled);
            Assert.IsTrue(report.Blanks[9].IsFilled, "5번에서 입력한 상류층 접대·후원 기록이 9번에도 반영된다.");
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
        public void WhitespaceIsIgnored_ButOtherCharactersAreNot()
        {
            // 2026-11-10 확정: 띄어쓰기는 무시하고, 다른 글자·숫자·기호는 무시하지 않는다.
            var a = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.Filled, a.TryFillBlank(6, "상품판매정산표"));

            var b = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, b.TryFillBlank(6, "상품 판매 정산표!"));
            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, b.TryFillBlank(6, "상품 판매 정산서"));
        }

        [Test]
        public void SubmitInput_WhileComposing_IsIgnored()
        {
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.IgnoredWhileComposing, report.SubmitInput("한 달", isComposing: true));
        }

        [Test]
        public void SubmitInput_AnswerForLaterBlankOnly_IsIncorrectAtCurrentStop()
        {
            // 타자기가 0·1번에서 멈춰 있을 때 뒤쪽 칸의 정답만 입력하면 받아들이지 않는다.
            var report = new FinalReportState(FinalReportAnswerConfig.CreateDefault());
            Assert.AreEqual(FillBlankOutcome.IncorrectClueName, report.SubmitInput(MainCaseClueNames.ProductSalesSettlement));
            Assert.IsFalse(report.Blanks[6].IsFilled);
        }

        [Test]
        public void WithoutRebelMemo_ReportCannotComplete()
        {
            var tracker = TrackerWithAll();
            var coordinator = new MainCaseFinalReportCoordinator(tracker, new PostReportBranching(), FinalReportAnswerConfig.CreateDefault());
            foreach (var input in new[]
                     {
                         MainCaseClueNames.ParticipantOperatingRulesNotice, MainCaseClueNames.ParticipantRosterDeliveryConfirmation,
                         MainCaseClueNames.NieceDeathReport, MainCaseClueNames.NieceDiary, MainCaseClueNames.UpperClassHospitalityRecord,
                         MainCaseClueNames.ProductSalesSettlement, MainCaseClueNames.ViewingTicketRevenueSettlement,
                         MainCaseClueNames.GovernmentDepartmentContactLog, MainCaseClueNames.ExperimentContinuationDecisionRecord
                     })
                coordinator.SubmitInput(input);

            Assert.AreEqual(FillBlankOutcome.ClueNotAcquired, coordinator.SubmitInput(MainCaseClueNames.RebelInternalCircularMemo));
            Assert.IsFalse(coordinator.Report.IsComplete);
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
            tracker.AcquireClue(MainCaseClueNames.RebelInternalCircularMemo);
            var branching = new PostReportBranching();
            var coordinator = new MainCaseFinalReportCoordinator(tracker, branching, FinalReportAnswerConfig.CreateDefault());
            Assert.IsTrue(coordinator.CanWriteFinalReport);

            // 실제 확정 정답만으로, 타자기가 멈추는 순서대로 입력해 끝까지 완성할 수 있어야 한다.
            string[] inputs =
            {
                MainCaseClueNames.ParticipantOperatingRulesNotice,        // 0·1
                MainCaseClueNames.ParticipantRosterDeliveryConfirmation,  // 2
                MainCaseClueNames.NieceDeathReport,                       // 3 (4번에도 반영)
                MainCaseClueNames.NieceDiary,                             // 4
                MainCaseClueNames.UpperClassHospitalityRecord,            // 5 (7·9번에도 반영)
                MainCaseClueNames.ProductSalesSettlement,                 // 6 (7번에도 반영)
                MainCaseClueNames.ViewingTicketRevenueSettlement,         // 7
                MainCaseClueNames.GovernmentDepartmentContactLog,         // 8 (13번에도 반영)
                MainCaseClueNames.ExperimentContinuationDecisionRecord,   // 10 (9번은 이미 완성)
                MainCaseClueNames.RebelInternalCircularMemo,              // 11·12·13
            };
            foreach (var input in inputs)
                Assert.AreEqual(FillBlankOutcome.Filled, coordinator.SubmitInput(input), input);

            Assert.IsTrue(coordinator.Report.IsComplete);
            Assert.AreEqual(PhoneCallStage.AwaitingAnswer, branching.PhoneStage);
        }
    }
}
