using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Reports;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ReportTruthAndSubmissionTests
    {
        private static ContractInstance BuildContract()
        {
            var contract = new ContractInstance("c1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            contract.Truth.PerpetratorId = "npc-real-perp";
            return contract;
        }

        [Test]
        public void CheckForUnrebuttedFalseEvidence_FlagsFalseClue()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-false", "c1") { TruthStatus = ClueTruthStatus.Fabricated });

            var report = new ContractReportState("c1");
            report.LinkClue(ReportSection.CaseSummary, "clue-false");

            var issues = new ContractReportValidator().CheckForUnrebuttedFalseEvidence(report, repo);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ReportSection.CaseSummary, issues[0].Section);
        }

        [Test]
        public void CheckForUnrebuttedFalseEvidence_FactualClue_NoIssue()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-real", "c1") { TruthStatus = ClueTruthStatus.Factual });

            var report = new ContractReportState("c1");
            report.LinkClue(ReportSection.CaseSummary, "clue-real");

            var issues = new ContractReportValidator().CheckForUnrebuttedFalseEvidence(report, repo);

            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void ReportTruthComparator_MatchingClaim_ReturnsTrue()
        {
            var contract = BuildContract();
            var report = new ContractReportState("c1");
            report.SetClaimedNpcId(ReportSection.Perpetrator, "npc-real-perp");

            var comparison = ReportTruthComparator.Compare(contract, report);

            Assert.IsTrue(comparison.PerpetratorClaimed);
            Assert.IsTrue(comparison.MatchesActualTruth);
        }

        [Test]
        public void ReportTruthComparator_FalseAccusation_ReturnsFalseButDoesNotThrow()
        {
            var contract = BuildContract();
            var report = new ContractReportState("c1");
            report.SetClaimedNpcId(ReportSection.Perpetrator, "npc-innocent-bystander");

            var comparison = ReportTruthComparator.Compare(contract, report);

            Assert.IsTrue(comparison.PerpetratorClaimed);
            Assert.IsFalse(comparison.MatchesActualTruth, "실제 진실과 다른 지목도 비교 결과로만 남지, 예외를 던지지 않는다.");
        }

        [Test]
        public void Submit_ValidReport_UpdatesOfficialConclusion()
        {
            var contract = BuildContract();
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "c1"));

            var report = new ContractReportState("c1");
            foreach (var section in ReportRequirementResolver.GetRequiredSections(contract))
            {
                report.SetContent(section, "내용");
                report.LinkClue(section, "clue-1");
            }
            report.SetClaimedNpcId(ReportSection.Perpetrator, "npc-framed-innocent");

            var result = ReportSubmissionCoordinator.Submit(contract, report, repo, new ContractReportValidator());

            Assert.AreEqual(ReportSubmissionOutcome.Submitted, result.Outcome);
            Assert.IsTrue(report.IsSubmitted);
            Assert.AreEqual("npc-framed-innocent", contract.OfficialConclusion.OfficiallyAccusedNpcId,
                "제출이 통과하면 플레이어가 조작한 지목 결과가 그대로 공식 결론에 반영되어야 한다(진실과 달라도 됨).");
        }

        [Test]
        public void Submit_InvalidReport_DoesNotMarkSubmitted_OrTouchOfficialConclusion()
        {
            var contract = BuildContract();
            var repo = new ClueRepository();
            var report = new ContractReportState("c1"); // 아무것도 채우지 않음

            var result = ReportSubmissionCoordinator.Submit(contract, report, repo, new ContractReportValidator());

            Assert.AreEqual(ReportSubmissionOutcome.Rejected, result.Outcome);
            Assert.IsFalse(report.IsSubmitted);
            Assert.IsNull(contract.OfficialConclusion.OfficiallyAccusedNpcId);
        }
    }
}
