using System.Linq;
using CaseFileOmission.MainCase;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>2026-11-16 사용자 지정 「참가자 운영 규칙 안내문 — 게임 적용 기준」: 배포본과 내부 상세본은 서로 다른 문서다.</summary>
    public class MainCaseDocumentCatalogTests
    {
        [Test]
        public void RulesNotice_DistributedAndInternal_AreSeparateDocuments()
        {
            var distributed = MainCaseDocumentCatalog.Infos[MainCaseClueNames.ParticipantOperatingRulesNotice];
            var internalCopy = MainCaseDocumentCatalog.Infos[MainCaseClueNames.ParticipantOperatingRulesInternal];

            Assert.AreEqual("특조-84-1017-04", distributed.DocumentNumber);
            Assert.AreEqual("1984년 10월 17일", distributed.WrittenDate);
            Assert.AreEqual("1984년 10월 18일", distributed.DistributionConfirmedDate);
            Assert.AreEqual("국가보안국 특별조사과", distributed.Department);
            Assert.IsTrue(distributed.IsStageClue, "배포본이 주요 조사 단서");

            Assert.AreEqual("운영-규칙-84-01", internalCopy.DocumentNumber);
            Assert.IsFalse(internalCopy.IsStageClue, "내부 상세본은 보조 조사 단서");
            StringAssert.DoesNotContain("침상", internalCopy.FoundAt, "내부 상세본은 참가자 침상에 배포하지 않는다");

            Assert.AreEqual(distributed.Title, internalCopy.Title, "문서명은 같다");
            Assert.AreNotEqual(MainCaseDocumentCatalog.DisplayTitle(distributed.ClueName), MainCaseDocumentCatalog.DisplayTitle(internalCopy.ClueName),
                "목록에서는 문서 번호로 구분한다");
        }

        [Test]
        public void RulesNotice_OnlyDistributedVersion_CountsForStageAndFinalReport()
        {
            CollectionAssert.Contains(MainCaseClueNames.ExperimentTruthClues.ToList(), MainCaseClueNames.ParticipantOperatingRulesNotice);
            CollectionAssert.DoesNotContain(MainCaseClueNames.ExperimentTruthClues.ToList(), MainCaseClueNames.ParticipantOperatingRulesInternal);

            var config = FinalReportAnswerConfig.CreateDefault();
            Assert.IsFalse(config.Entries.Any(e => e.AcceptedClueNames.Contains(MainCaseClueNames.ParticipantOperatingRulesInternal)),
                "최종 보고서 정답 단서는 배포본");
        }
    }
}
