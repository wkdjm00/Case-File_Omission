using System;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using CaseFileOmission.Organization;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>05·06 2026-11-13 조직 보조 자료 20개(ORG_SUPPLEMENTARY_REPORT) 판정·전달.</summary>
    public class OrgSupplementarySupportTests
    {
        private static OrgSupportContext AllMet(float trust) => new OrgSupportContext
        {
            Trust = trust,
            ContractId = "c1",
            PlayerHolds = _ => true,
            StageUnlocked = _ => true,
            ContractsCompletedSinceUnlock = _ => 2,
            SourceAvailable = _ => true
        };

        [Test]
        public void TwentyReports_FivePerStage_WithConditionsAndBodies()
        {
            Assert.AreEqual(20, OrgSupplementaryReports.All.Length);
            foreach (OrgTruthStage stage in Enum.GetValues(typeof(OrgTruthStage)))
                Assert.AreEqual(5, OrgSupplementaryReports.All.Count(r => r.Stage == stage), stage.ToString());
            Assert.AreEqual(20, OrgSupplementaryReports.All.Select(r => r.Id).Distinct().Count());
            foreach (var r in OrgSupplementaryReports.All)
            {
                Assert.IsNotEmpty(r.ConditionA); Assert.IsNotEmpty(r.ConditionB); Assert.IsNotEmpty(r.ConditionC);
                Assert.IsNotEmpty(r.SourceLabels);
                StringAssert.StartsWith(r.Title, r.Body, "본문은 제목부터");
            }
        }

        [TestCase(20f, 0f, "1-20")]
        [TestCase(21f, 20f, "21-39")]
        [TestCase(39f, 20f, "21-39")]
        [TestCase(40f, 35f, "40-69")]
        [TestCase(69f, 35f, "40-69")]
        [TestCase(70f, 50f, "70-89")]
        [TestCase(89f, 50f, "70-89")]
        [TestCase(90f, 70f, "90-100")]
        public void TrustBands_Boundaries(float trust, float chance, string band)
        {
            Assert.AreEqual(chance, OrgSupplementarySupportService.ProbabilityFor(trust));
            Assert.AreEqual(band, OrgSupplementarySupportService.BandFor(trust));
        }

        [Test]
        public void RequiredConditions_PerBand()
        {
            var def = OrgSupplementaryReports.All[0];
            OrgSupportContext Holding(float trust, bool b, bool c, bool d) => new OrgSupportContext
            {
                Trust = trust,
                PlayerHolds = n => n == def.ConditionA || (b && n == def.ConditionB) || (c && n == def.ConditionC),
                ContractsCompletedSinceUnlock = _ => d ? 2 : 1
            };
            Assert.IsTrue(OrgSupplementarySupportService.ConditionsMet(def, Holding(90f, false, false, false)), "90~100: A");
            Assert.IsFalse(OrgSupplementarySupportService.ConditionsMet(def, Holding(70f, false, true, true)), "70~89: A+B");
            Assert.IsTrue(OrgSupplementarySupportService.ConditionsMet(def, Holding(70f, true, false, false)));
            Assert.IsFalse(OrgSupplementarySupportService.ConditionsMet(def, Holding(40f, true, false, true)), "40~69: A+B+C");
            Assert.IsTrue(OrgSupplementarySupportService.ConditionsMet(def, Holding(40f, true, true, false)));
            Assert.IsFalse(OrgSupplementarySupportService.ConditionsMet(def, Holding(21f, true, true, false)), "21~39: A+B+C+D");
            Assert.IsTrue(OrgSupplementarySupportService.ConditionsMet(def, Holding(21f, true, true, true)));
        }

        [Test]
        public void LowTrust_Waits_ThenRollsOnce_NoReroll()
        {
            var service = new OrgSupplementarySupportService(new Random(1));
            service.Evaluate(AllMet(15f));
            Assert.IsTrue(service.States.All(s => s.RollStatus == OrgReportRollStatus.LowTrustWait), "1~20은 판정하지 않고 대기");

            service.Evaluate(AllMet(95f));
            Assert.IsTrue(service.States.All(s => s.RollStatus == OrgReportRollStatus.Success || s.RollStatus == OrgReportRollStatus.Failed));
            Assert.IsTrue(service.States.All(s => s.EligibilityBand == "90-100"));
            var snapshot = service.States.ToDictionary(s => s.Id, s => s.RollStatus);

            service.Evaluate(AllMet(95f));
            Assert.IsTrue(service.States.All(s => snapshot[s.Id] == s.RollStatus), "실패해도 다시 굴리지 않는다");
            Assert.IsTrue(service.States.Where(s => s.RollStatus == OrgReportRollStatus.Success).All(s => s.DeliveryStatus == OrgReportDeliveryStatus.Pending));
        }

        [Test]
        public void ZeroTrust_NoSupport_AndDesignerReportsOnlyBeforeFinalReport()
        {
            var service = new OrgSupplementarySupportService(new Random(2));
            service.Evaluate(AllMet(0f));
            Assert.IsTrue(service.States.All(s => s.RollStatus == OrgReportRollStatus.NotChecked));

            var ctx = AllMet(95f);
            ctx.FinalReportComplete = true;
            service.Evaluate(ctx);
            foreach (var def in OrgSupplementaryReports.All.Where(d => d.Stage == OrgTruthStage.Designer))
                Assert.AreEqual(OrgReportRollStatus.NotChecked, service.GetState(def.Id).RollStatus, "최종 보고서 완성 뒤에는 설계자 자료를 판정하지 않는다");
        }

        [Test]
        public void FixedDataSource_RequiresExplicitRegisteredId()
        {
            Assert.IsTrue(OrgSupplementaryReports.IsKnownFixedDataSource("EXPERIMENT_PRESSURE_V1"));
            Assert.IsTrue(OrgSupplementaryReports.IsKnownFixedDataSource("CONTROL_COLLAR_V1"));
            Assert.IsFalse(OrgSupplementaryReports.IsKnownFixedDataSource("MISSING_SOURCE_123"));
            Assert.IsFalse(OrgSupplementaryReports.IsKnownFixedDataSource("ORG_SYS_01"));
        }

        [Test]
        public void MissingSource_IsNotEligible()
        {
            var service = new OrgSupplementarySupportService(new Random(3));
            var ctx = AllMet(95f);
            ctx.SourceAvailable = _ => false;
            service.Evaluate(ctx);
            Assert.IsTrue(service.States.All(s => s.RollStatus == OrgReportRollStatus.NotChecked), "원본 자료가 실제로 없으면 판정하지 않는다");
        }

        [Test]
        public void Delivery_AllPendingAtOnce_ThenRead()
        {
            var service = new OrgSupplementarySupportService(new Random(4));
            service.Evaluate(AllMet(95f));
            int pending = service.States.Count(s => s.DeliveryStatus == OrgReportDeliveryStatus.Pending);
            Assume.That(pending, Is.GreaterThan(0));
            var delivered = service.DeliverPending(600);
            Assert.AreEqual(pending, delivered.Count);
            Assert.IsTrue(delivered.All(id => service.GetState(id).DeliveredWorldMinute == 600 && !service.GetState(id).Read));
            service.MarkRead(delivered[0]);
            Assert.IsTrue(service.GetState(delivered[0]).Read);
            Assert.AreEqual(0, service.DeliverPending(700).Count);
        }

        [Test]
        public void Session_ContractEnd_RollsAfterTrustChange_DeliversWithOneCall_AndSaves()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(21) });
            s.StartNewGame();
            s.CompletePrologue();
            foreach (var name in MainCaseClueNames.SystemicTruthClues) s.MainCase.AcquireClue(name);
            s.MainCase.TryAdvanceStage();
            s.Organization.SetTrust(95f);

            Assert.IsNotNull(s.OfferContract());
            Assert.IsTrue(s.AcceptContract().Success);
            Assert.IsTrue(s.AbandonContract().Success);

            var systemic = OrgSupplementaryReports.All.Where(d => d.Stage == OrgTruthStage.Systemic).ToList();
            Assert.IsTrue(systemic.All(d => s.OrgSupplementary.GetState(d.Id).RollStatus != OrgReportRollStatus.NotChecked
                                            || !d.SourceLabels.All(l => s.MainCase.HasAcquired(l))),
                "원본이 있는 체계 자료는 의뢰 종료 뒤 판정된다");
            bool anyDelivered = s.DeliveredOrgReports.Count > 0;
            Assert.AreEqual(anyDelivered, s.ConsumeOrgDeliveryCall(), "전달이 있으면 전화 1회");
            Assert.IsFalse(s.ConsumeOrgDeliveryCall(), "같은 경계의 전화는 한 번뿐");

            var loaded = GameSession.Load(s.CaptureSave(), new GameSessionConfig { Random = new Random(1) });
            foreach (var st in s.OrgSupplementary.States)
            {
                var restored = loaded.OrgSupplementary.GetState(st.Id);
                Assert.AreEqual(st.RollStatus, restored.RollStatus, st.Id);
                Assert.AreEqual(st.DeliveryStatus, restored.DeliveryStatus, st.Id);
            }
            Assert.AreEqual(s.Campaign.CompletedContractCount, loaded.Campaign.CompletedContractCount);
        }
    }
}
