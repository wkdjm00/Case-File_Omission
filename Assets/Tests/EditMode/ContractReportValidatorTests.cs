using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Reports;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractReportValidatorTests
    {
        private static ContractInstance BuildContract(bool withPerpetrator, bool withMotive, bool withTool, bool withSubCrime)
        {
            var contract = new ContractInstance("contract-1")
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");

            if (withPerpetrator) contract.Truth.PerpetratorId = "npc-1";
            if (withMotive) contract.Truth.PrimaryMotive = "원한";
            if (withTool) contract.Truth.Tool = "둔기";
            if (withSubCrime) contract.SubCrimes.Add(CrimeType.TheftRobbery);

            return contract;
        }

        private static ClueRepository BuildRepositoryWithClues(params string[] clueIds)
        {
            var repo = new ClueRepository();
            foreach (var id in clueIds)
                repo.Discover(new ClueDefinition(id, "contract-1"));
            return repo;
        }

        [Test]
        public void Validate_AllRequiredSectionsFilled_CanSubmit()
        {
            var contract = BuildContract(withPerpetrator: true, withMotive: true, withTool: true, withSubCrime: true);
            var repo = BuildRepositoryWithClues("clue-1");

            var report = new ContractReportState("contract-1");
            foreach (var section in ReportRequirementResolver.GetRequiredSections(contract))
            {
                report.SetContent(section, "내용");
                report.LinkClue(section, "clue-1");
            }
            report.SetClaimedNpcId(ReportSection.Perpetrator, "npc-1");

            var result = new ContractReportValidator().Validate(contract, report, repo);

            Assert.IsTrue(result.CanSubmit);
        }

        [Test]
        public void Validate_LinkedCluesWithoutFreeText_CanSubmit()
        {
            // 04 2026-11-09 확정: 자유 서술 입력은 기본 방식이 아니다 — 단서 연결만으로 제출할 수 있어야 한다.
            var contract = BuildContract(withPerpetrator: true, withMotive: false, withTool: false, withSubCrime: false);
            var repo = BuildRepositoryWithClues("clue-1");
            var report = new ContractReportState("contract-1");
            foreach (var section in ReportRequirementResolver.GetRequiredSections(contract))
                report.LinkClue(section, "clue-1");
            report.SetClaimedNpcId(ReportSection.Perpetrator, "npc-1");

            Assert.IsTrue(new ContractReportValidator().Validate(contract, report, repo).CanSubmit);
        }

        [Test]
        public void Validate_ClueFromAnotherContract_FailsSubmission()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: false);
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("other-clue", "contract-2"));
            var report = new ContractReportState("contract-1");
            foreach (var section in ReportRequirementResolver.GetRequiredSections(contract))
                report.LinkClue(section, "other-clue");

            var result = new ContractReportValidator().Validate(contract, report, repo);

            Assert.IsFalse(result.CanSubmit);
            Assert.IsTrue(result.Issues.Exists(i => i.Detail.Contains("이 의뢰의 단서가 아닙니다")));
        }

        [Test]
        public void Validate_RequiredSectionWithoutContent_FailsSubmission()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: false);
            var repo = BuildRepositoryWithClues("clue-1");
            var report = new ContractReportState("contract-1");
            // CaseSummary를 포함해 필수 항목을 아예 채우지 않는다.

            var result = new ContractReportValidator().Validate(contract, report, repo);

            Assert.IsFalse(result.CanSubmit);
            Assert.IsTrue(result.Issues.Exists(i => i.Section == ReportSection.CaseSummary));
        }

        [Test]
        public void Validate_RequiredSectionWithContentButNoClue_FailsSubmission()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: false);
            var repo = BuildRepositoryWithClues("clue-1");
            var report = new ContractReportState("contract-1");
            report.SetContent(ReportSection.CaseSummary, "내용만 채움");
            report.SetContent(ReportSection.KeyPersons, "내용");
            report.LinkClue(ReportSection.KeyPersons, "clue-1");
            report.SetContent(ReportSection.CaseProgress, "내용");
            report.LinkClue(ReportSection.CaseProgress, "clue-1");
            report.SetContent(ReportSection.DamageAndResult, "내용");
            report.LinkClue(ReportSection.DamageAndResult, "clue-1");

            var result = new ContractReportValidator().Validate(contract, report, repo);

            Assert.IsFalse(result.CanSubmit);
            Assert.IsTrue(result.Issues.Exists(i => i.Section == ReportSection.CaseSummary && i.Detail.Contains("근거 단서")));
        }

        [Test]
        public void Validate_OptionalSectionsNotRequired_WhenTruthFieldsEmpty()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: false);

            var required = ReportRequirementResolver.GetRequiredSections(contract);

            Assert.IsFalse(required.Contains(ReportSection.Perpetrator));
            Assert.IsFalse(required.Contains(ReportSection.Motive));
            Assert.IsFalse(required.Contains(ReportSection.MeansAndTool));
            Assert.IsFalse(required.Contains(ReportSection.SubCrime));
        }

        [Test]
        public void Validate_SubCrimeRequired_WhenSubCrimeExists()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: true);

            var required = ReportRequirementResolver.GetRequiredSections(contract);

            Assert.IsTrue(required.Contains(ReportSection.SubCrime));
        }

        [Test]
        public void Validate_LinkedClueNotInRepository_FailsSubmission()
        {
            var contract = BuildContract(withPerpetrator: false, withMotive: false, withTool: false, withSubCrime: false);
            var repo = BuildRepositoryWithClues(); // 비어 있음
            var report = new ContractReportState("contract-1");
            foreach (var section in ReportRequirementResolver.GetRequiredSections(contract))
            {
                report.SetContent(section, "내용");
                report.LinkClue(section, "clue-not-exist");
            }

            var result = new ContractReportValidator().Validate(contract, report, repo);

            Assert.IsFalse(result.CanSubmit);
            Assert.IsTrue(result.Issues.Exists(i => i.Detail.Contains("찾을 수 없습니다")));
        }
    }
}
