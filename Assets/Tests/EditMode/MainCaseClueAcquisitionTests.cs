using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.MainCase;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MainCaseClueAcquisitionTests
    {
        private static MainCaseClueContext SecretaryKnifeSuspicious() => new MainCaseClueContext
        {
            ContractId = "c1",
            VictimCount = 1,
            VictimOccupations = { "비서" },
            CauseOfDeathTool = "칼",
            PerpetratorPersonality = NpcPersonality.Suspicious
        };

        [Test]
        public void ContactLog_RequiresThreeVictims()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();
            var repo = new ClueRepository();

            var two = new MainCaseClueContext { ContractId = "c1", VictimCount = 2 };
            Assert.AreEqual(MainCaseClueAcquireOutcome.NotEligible,
                service.TryAcquire(MainCaseClueCode.GovernmentDepartmentContactLog, MainCaseClueRoute.FoundInContract, two, tracker, repo));
            Assert.IsFalse(repo.Exists(MainCaseClueAcquisitionService.ToClueId(MainCaseClueCode.GovernmentDepartmentContactLog)));

            var three = new MainCaseClueContext { ContractId = "c1", VictimCount = 3 };
            Assert.AreEqual(MainCaseClueAcquireOutcome.Acquired,
                service.TryAcquire(MainCaseClueCode.GovernmentDepartmentContactLog, MainCaseClueRoute.FoundInContract, three, tracker, repo));

            Assert.IsTrue(tracker.HasAcquired(MainCaseClueNames.GovernmentDepartmentContactLog));
            Assert.IsTrue(repo.Exists(MainCaseClueAcquisitionService.ToClueId(MainCaseClueCode.GovernmentDepartmentContactLog)));
        }

        [Test]
        public void SameClue_IsNotAcquiredTwice()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();
            var repo = new ClueRepository();
            var ctx = new MainCaseClueContext { ContractId = "c1", VictimCount = 3 };

            service.TryAcquire(MainCaseClueCode.GovernmentDepartmentContactLog, MainCaseClueRoute.FoundInContract, ctx, tracker, repo);

            Assert.AreEqual(MainCaseClueAcquireOutcome.AlreadyAcquired,
                service.TryAcquire(MainCaseClueCode.GovernmentDepartmentContactLog, MainCaseClueRoute.FoundInContract, ctx, tracker, repo));
        }

        [Test]
        public void RecordingAndRoster_RequireSecretaryKnifeSuspicious()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();

            Assert.IsTrue(service.IsEligible(MainCaseClueCode.ResultReportRecording, SecretaryKnifeSuspicious(), tracker));
            Assert.IsTrue(service.IsEligible(MainCaseClueCode.ParticipantRosterDeliveryConfirmation, SecretaryKnifeSuspicious(), tracker));

            var wrongTool = SecretaryKnifeSuspicious();
            wrongTool.CauseOfDeathTool = "둔기";
            Assert.IsFalse(service.IsEligible(MainCaseClueCode.ResultReportRecording, wrongTool, tracker));

            var wrongJob = SecretaryKnifeSuspicious();
            wrongJob.VictimOccupations.Clear();
            wrongJob.VictimOccupations.Add("교사");
            Assert.IsFalse(service.IsEligible(MainCaseClueCode.ResultReportRecording, wrongJob, tracker));

            var wrongTrait = SecretaryKnifeSuspicious();
            wrongTrait.PerpetratorPersonality = NpcPersonality.Timid;
            Assert.IsFalse(service.IsEligible(MainCaseClueCode.ParticipantRosterDeliveryConfirmation, wrongTrait, tracker));
        }

        [Test]
        public void SeniorOfficialLetter_RequiresDesireTrait()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();

            Assert.IsTrue(service.IsEligible(MainCaseClueCode.SeniorOfficialLetter,
                new MainCaseClueContext { PerpetratorPersonality = NpcPersonality.Greedy }, tracker));
            Assert.IsFalse(service.IsEligible(MainCaseClueCode.SeniorOfficialLetter,
                new MainCaseClueContext { PerpetratorPersonality = NpcPersonality.Suspicious }, tracker));
        }

        [Test]
        public void ManagementTable_RequiresNoteThenTwoMoreContracts_AndOnlyOrgOrRandomRoute()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();
            var repo = new ClueRepository();
            var ctx = new MainCaseClueContext { ContractId = "c3", IsInterContractDeliveryWindow = true };

            Assert.IsFalse(service.IsEligible(MainCaseClueCode.SubjectManagementTable, ctx, tracker));

            tracker.AcquireClue(MainCaseClueNames.NumberNoteLeftForLover);
            tracker.RecordContractCompleted();
            Assert.IsFalse(service.IsEligible(MainCaseClueCode.SubjectManagementTable, ctx, tracker), "1건만 더 진행됨");

            tracker.RecordContractCompleted();
            Assert.IsTrue(service.IsEligible(MainCaseClueCode.SubjectManagementTable, ctx, tracker));

            Assert.AreEqual(MainCaseClueAcquireOutcome.RouteNotAllowed,
                service.TryAcquire(MainCaseClueCode.SubjectManagementTable, MainCaseClueRoute.FoundInContract, ctx, tracker, repo));

            Assert.AreEqual(MainCaseClueAcquireOutcome.RandomPolicyNotConfigured,
                service.TryAcquire(MainCaseClueCode.SubjectManagementTable, MainCaseClueRoute.RandomAppearance, ctx, tracker, repo));

            var delivered = service.DeliverEligibleViaOrganization(ctx, tracker, repo);
            CollectionAssert.AreEqual(new[] { MainCaseClueCode.SubjectManagementTable }, delivered);
            Assert.IsTrue(tracker.HasAcquired(MainCaseClueNames.SubjectManagementTable));
        }

        [Test]
        public void OrganizationDelivery_OutsideWindow_IsRejected()
        {
            var service = new MainCaseClueAcquisitionService();
            var tracker = new MainCaseProgressTracker();
            tracker.AcquireClue(MainCaseClueNames.NumberNoteLeftForLover);
            tracker.RecordContractCompleted();
            tracker.RecordContractCompleted();

            var ctx = new MainCaseClueContext { ContractId = "c3", IsInterContractDeliveryWindow = false };

            Assert.AreEqual(MainCaseClueAcquireOutcome.OutsideDeliveryWindow,
                service.TryAcquire(MainCaseClueCode.SubjectManagementTable, MainCaseClueRoute.OrganizationDelivery, ctx, tracker, new ClueRepository()));
            Assert.AreEqual(0, service.DeliverEligibleViaOrganization(ctx, tracker, new ClueRepository()).Count);
        }

        [Test]
        public void SubjectNumberFile_RequiresComputerLetterAndConfiguredPassword()
        {
            var tracker = new MainCaseProgressTracker();
            var repo = new ClueRepository();
            var ctx = new MainCaseClueContext { ContractId = "c1", RoomHasComputer = true };

            var unconfigured = new MainCaseClueAcquisitionService(new MainCaseClueRuleConfig { SubjectNumberFilePassword = null });
            Assert.AreEqual(MainCaseClueAcquireOutcome.NotEligible,
                unconfigured.TryAcquire(MainCaseClueCode.SubjectNumberFile, MainCaseClueRoute.FoundInContract, ctx, tracker, repo));

            tracker.AcquireClue(MainCaseClueNames.SeniorOfficialLetter);
            Assert.AreEqual(MainCaseClueAcquireOutcome.PasswordNotConfigured,
                unconfigured.TryAcquire(MainCaseClueCode.SubjectNumberFile, MainCaseClueRoute.FoundInContract, ctx, tracker, repo, "아무거나"));

            var configured = new MainCaseClueAcquisitionService(new MainCaseClueRuleConfig { SubjectNumberFilePassword = "TEST-PW" });
            Assert.AreEqual(MainCaseClueAcquireOutcome.WrongPassword,
                configured.TryAcquire(MainCaseClueCode.SubjectNumberFile, MainCaseClueRoute.FoundInContract, ctx, tracker, repo, "틀림"));
            Assert.AreEqual(MainCaseClueAcquireOutcome.Acquired,
                configured.TryAcquire(MainCaseClueCode.SubjectNumberFile, MainCaseClueRoute.FoundInContract, ctx, tracker, repo, "TEST-PW"));
        }

        [Test]
        public void ContextFromContract_ReadsVictimsOccupationToolAndPerpetratorTrait()
        {
            var contract = new ContractInstance("c1");
            contract.Truth.Tool = "칼";
            contract.Truth.PerpetratorId = "perp";

            var npcs = new NpcRegistry();
            npcs.Add(new NpcState("victim", "c1", NpcActualRole.Victim, NpcPersonality.Indifferent) { Occupation = "비서" });
            npcs.Add(new NpcState("perp", "c1", NpcActualRole.Perpetrator, NpcPersonality.Suspicious));

            var ctx = MainCaseClueContext.FromContract(contract, npcs);

            Assert.AreEqual(1, ctx.VictimCount);
            CollectionAssert.Contains(ctx.VictimOccupations, "비서");
            Assert.AreEqual("칼", ctx.CauseOfDeathTool);
            Assert.AreEqual(NpcPersonality.Suspicious, ctx.PerpetratorPersonality);
            Assert.IsTrue(new MainCaseClueAcquisitionService().IsEligible(MainCaseClueCode.ResultReportRecording, ctx, new MainCaseProgressTracker()));
        }
    }
}
