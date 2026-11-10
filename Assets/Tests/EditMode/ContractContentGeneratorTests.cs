using System;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractContentGeneratorTests
    {
        private static ContractInstance BuildBareContract(string id = "c1")
        {
            var contract = new ContractInstance(id)
            {
                PrimaryCrimeType = CrimeType.Murder,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House
            };
            contract.Truth.TimelineEvents.Add("사건 발생");
            return contract;
        }

        [Test]
        public void CreateCast_AssignsPerpetratorVictimAndWitness()
        {
            var contract = BuildBareContract();
            var npcRegistry = new NpcRegistry();

            new ContractContentGenerator(new Random(1)).CreateCast(contract, npcRegistry);

            Assert.IsTrue(npcRegistry.Exists(contract.Truth.PerpetratorId));
            Assert.IsTrue(npcRegistry.Exists(contract.Truth.VictimId));
            Assert.AreEqual(3, npcRegistry.GetByContract("c1").Count);
            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.PrimaryMotive));
            Assert.IsFalse(string.IsNullOrEmpty(contract.Truth.Tool));
        }

        [Test]
        public void Pipeline_DoesNotGrantAnyClueAutomatically()
        {
            // 04 확정: 현장에 없는 단서를 자동 지급하지 않는다 — 생성 결과는 단서 저장소를 건드리지 않는다.
            var pipeline = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(11));
            var generated = pipeline.Generate(() => "c1");
            var repository = new ClueRepository();

            Assert.AreEqual(0, repository.GetAllClueIds("c1").Count);
            Assert.IsTrue(generated.Materials.Count > 0, "조사로 얻을 자료는 생성되어 있어야 한다.");
        }

        [Test]
        public void Pipeline_WitnessActuallyWitnessesCoreIncident()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var generated = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(seed)).Generate(() => $"c{seed}");
                var core = generated.Timeline.CoreIncident;

                foreach (var witness in generated.Npcs.GetByContract(generated.Contract.ContractId)
                             .Where(n => n.ActualRole == NpcActualRole.Witness))
                {
                    Assert.AreEqual(core.RoomId, generated.Timeline.GetRoomAt(witness.NpcId, core.StartMinute));
                    Assert.IsTrue(witness.WitnessedEventIds.Contains(core.EventId));
                }
            }
        }

        [Test]
        public void Pipeline_ProducesValidatedContract_WithBoundOrganizationTarget()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var generated = new ContractPipeline(ContractGenerationConfig.CreateDefault(), new Random(seed)).Generate(() => $"c{seed}");
                var contract = generated.Contract;

                Assert.IsTrue(new ContractValidator().Validate(contract).IsValid);
                var condition = contract.OrgCondition;
                if (condition.Type == OrganizationConditionType.RecoverBody || condition.Type == OrganizationConditionType.RecoverItem)
                    Assert.IsNotNull(generated.GetMaterial(condition.TargetId), "회수 대상은 실제로 생성된 자료여야 한다.");
                if (condition.Type == OrganizationConditionType.DesignateOfficialCulprit)
                    Assert.IsTrue(generated.Npcs.Exists(condition.TargetId));
                Assert.IsFalse(string.IsNullOrEmpty(contract.ExternalState.ReportedSummary));
            }
        }

        [Test]
        public void FalseAlibiChance_FollowsPersonalityTable()
        {
            Assert.AreEqual(0.70, EvidenceMaterialGenerator.FalseAlibiChance(NpcPersonality.FrequentLiar));
            Assert.AreEqual(0.60, EvidenceMaterialGenerator.FalseAlibiChance(NpcPersonality.Secretive));
            Assert.AreEqual(0.50, EvidenceMaterialGenerator.FalseAlibiChance(NpcPersonality.Suspicious));
            Assert.AreEqual(0.50, EvidenceMaterialGenerator.FalseAlibiChance(NpcPersonality.Greedy));
            Assert.AreEqual(0.40, EvidenceMaterialGenerator.FalseAlibiChance(NpcPersonality.Indifferent));
        }
    }
}
