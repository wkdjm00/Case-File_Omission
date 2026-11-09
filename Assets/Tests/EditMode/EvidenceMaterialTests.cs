using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;
using NUnit.Framework;
using static CaseFileOmission.Tests.ContractTimelineTests;

namespace CaseFileOmission.Tests
{
    public class EvidenceMaterialTests
    {
        private sealed class Generated
        {
            public ContractInstance Contract;
            public NpcRegistry Registry;
            public RoomGraph Rooms;
            public ContractTimeline Timeline;
            public MaterialGenerationResult Materials;
        }

        private static Generated Make(int seed, CrimeType crime = CrimeType.Murder, bool? includeAlibi = true)
        {
            var (contract, registry, rooms) = BuildCase(crime);
            var timeline = new ContractTimelineGenerator(new Random(seed)).Generate(contract, registry, rooms).Timeline;
            Assert.IsNotNull(timeline);
            var generator = new EvidenceMaterialGenerator(new Random(seed)) { IncludePerpetratorFalseAlibi = includeAlibi };
            return new Generated
            {
                Contract = contract,
                Registry = registry,
                Rooms = rooms,
                Timeline = timeline,
                Materials = generator.Generate(timeline, registry, rooms)
            };
        }

        private static ContractValidationResult Validate(Generated g, IReadOnlyList<EvidenceMaterial> materials = null) =>
            new EvidenceMaterialValidator().Validate(materials ?? g.Materials.Materials, g.Timeline, g.Registry, g.Rooms);

        [Test]
        public void GeneratedMaterials_AreValid_ForAllCrimeTypesAndSeeds()
        {
            foreach (CrimeType crime in Enum.GetValues(typeof(CrimeType)))
            {
                for (int seed = 0; seed < 25; seed++)
                {
                    var g = Make(seed, crime, includeAlibi: null);
                    Assert.IsTrue(g.Materials.Success);
                    var validation = Validate(g);
                    Assert.IsTrue(validation.IsValid, $"seed {seed} {crime}: {string.Join(",", validation.Errors)}");
                }
            }
        }

        [Test]
        public void SceneTrace_IsInvestigatedAtScene_AndNotAutoDiscovered()
        {
            var g = Make(1);
            var trace = g.Materials.Materials.Single(m => m.MaterialId.EndsWith("scene-trace"));

            Assert.AreEqual(MaterialAcquisition.InvestigateRoom, trace.Acquisition);
            Assert.AreEqual(g.Timeline.SceneRoomId, trace.RoomId);
            Assert.AreEqual(g.Timeline.CoreIncident.StartMinute, trace.AboutMinute);

            var def = trace.ToClueDefinition();
            Assert.AreEqual(ClueFormat.PhysicalObject, def.Format);
            Assert.AreEqual($"investigate:{g.Timeline.SceneRoomId}", def.AcquisitionCondition);
            Assert.AreEqual(g.Timeline.CoreIncident.EventId, def.RelatedTruthNote);
            Assert.IsNull(def.DisplayedContent, "원본에 없는 표시 문장을 만들지 않는다.");

            var repository = new ClueRepository();
            Assert.IsFalse(repository.Exists(trace.MaterialId), "생성만으로 단서를 지급하지 않는다.");
        }

        [Test]
        public void WitnessTestimony_ComesOnlyFromLivingDirectWitnesses_WhoKnowIt()
        {
            for (int seed = 0; seed < 15; seed++)
            {
                var g = Make(seed);
                var testimonies = g.Materials.Materials.Where(m => m.MaterialId.EndsWith("witness-testimony")).ToList();

                CollectionAssert.IsNotEmpty(testimonies, "목격자 역할 인물은 실제로 목격했으므로 증언이 있다.");
                foreach (var t in testimonies)
                {
                    Assert.AreNotEqual(Victim, t.SourceNpcId, "사망한 피해자는 증언하지 않는다.");
                    Assert.AreNotEqual(Perp, t.SourceNpcId);
                    Assert.IsTrue(g.Timeline.HasGrant(t.SourceNpcId, g.Timeline.CoreIncident.InfoId));
                    Assert.IsTrue(t.IdentifiesPerpetrator);
                    Assert.AreEqual($"talk:{t.SourceNpcId}", t.ToClueDefinition().AcquisitionCondition);
                }
            }
        }

        [Test]
        public void FalseAlibi_ClaimsAnotherRoom_HasReason_AndIsRebuttable()
        {
            for (int seed = 0; seed < 15; seed++)
            {
                var g = Make(seed);
                var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);

                Assert.AreEqual(Perp, alibi.SourceNpcId);
                Assert.AreEqual(ClueFormat.Testimony, alibi.Format);
                Assert.AreNotEqual(g.Timeline.SceneRoomId, alibi.AboutRoomId);
                Assert.IsFalse(string.IsNullOrEmpty(alibi.FalsehoodReason));
                CollectionAssert.IsNotEmpty(alibi.RebuttedByMaterialIds);
                foreach (var id in alibi.RebuttedByMaterialIds)
                    Assert.IsTrue(g.Materials.Materials.Any(m => m.MaterialId == id && !m.IsFalseOrFabricated));
            }
        }

        [Test]
        public void NoFalseAlibi_IsAllowed()
        {
            var g = Make(2, includeAlibi: false);
            Assert.AreEqual(0, g.Materials.Materials.Count(m => m.IsFalseOrFabricated), "거짓 자료 0개도 허용(확정).");
            Assert.IsTrue(Validate(g).IsValid);
        }

        [Test]
        public void Validator_RejectsFalseMaterialWithoutRebuttalOrReason()
        {
            var g = Make(3);
            var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);

            alibi.RebuttedByMaterialIds.Clear();
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.CONTRADICTION);

            g = Make(3);
            g.Materials.Materials.Single(m => m.IsFalseOrFabricated).FalsehoodReason = null;
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.CONTRADICTION);
        }

        [Test]
        public void Validator_RejectsRebuttalThatDoesNotActuallyContradict()
        {
            var g = Make(4);
            var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);
            var trace = g.Materials.Materials.Single(m => m.MaterialId.EndsWith("scene-trace"));

            // 현장 흔적은 가해자의 위치를 직접 보여 주지 않으므로 알리바이 반박 근거가 아니다.
            alibi.RebuttedByMaterialIds.Clear();
            alibi.RebuttedByMaterialIds.Add(trace.MaterialId);
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.CONTRADICTION);
        }

        [Test]
        public void Validator_RejectsKnowledgeLeakAndUnreachableAndTimeMismatch()
        {
            var g = Make(5);
            var witness = g.Materials.Materials.First(m => m.MaterialId.EndsWith("witness-testimony"));
            witness.StatedInfoIds.Add("info-nobody-saw");
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.KNOWLEDGE_LEAK);

            g = Make(5);
            var fromVictim = new EvidenceMaterial
            {
                MaterialId = "c1-mat-victim", ContractId = ContractId, Format = ClueFormat.Testimony,
                SourceNpcId = Victim, Acquisition = MaterialAcquisition.TalkToNpc
            };
            var list = g.Materials.Materials.Concat(new[] { fromVictim }).ToList();
            CollectionAssert.Contains(Validate(g, list).Errors, ContractValidationErrorCode.CLUE_UNREACHABLE);

            g = Make(5);
            g.Materials.Materials.Single(m => m.MaterialId.EndsWith("scene-trace")).AboutMinute += 1;
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.TIMELINE_CONFLICT);

            g = Make(5);
            g.Materials.Materials.Single(m => m.MaterialId.EndsWith("scene-trace")).RoomId = "garden";
            CollectionAssert.Contains(Validate(g).Errors, ContractValidationErrorCode.CLUE_UNREACHABLE);
        }

        [Test]
        public void Validator_EnforcesFalseMaterialLimits()
        {
            var g = Make(6);
            var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);

            EvidenceMaterial Copy(string id, ClueFormat format)
            {
                var m = new EvidenceMaterial
                {
                    MaterialId = id, ContractId = ContractId, Format = format, TruthStatus = ClueTruthStatus.Fabricated,
                    SourceNpcId = format == ClueFormat.Testimony ? Perp : null,
                    RoomId = format == ClueFormat.Testimony ? null : g.Timeline.SceneRoomId,
                    Acquisition = format == ClueFormat.Testimony ? MaterialAcquisition.TalkToNpc : MaterialAcquisition.InvestigateRoom,
                    AboutMinute = alibi.AboutMinute, AboutRoomId = alibi.AboutRoomId, SubjectNpcId = Perp,
                    FalsehoodReason = "test"
                };
                m.RebuttedByMaterialIds.AddRange(alibi.RebuttedByMaterialIds);
                return m;
            }

            var withinLimit = g.Materials.Materials
                .Concat(new[] { Copy("v2", ClueFormat.Testimony), Copy("v3", ClueFormat.Testimony) })
                .Concat(new[] { Copy("p1", ClueFormat.Document), Copy("p2", ClueFormat.PhysicalObject), Copy("p3", ClueFormat.Recording) })
                .ToList();
            Assert.IsFalse(Validate(g, withinLimit).Errors.Contains(ContractValidationErrorCode.CONTRACT_INVALID), "물리 3 + 언어 3 = 6은 허용.");

            var tooManyVerbal = withinLimit.Concat(new[] { Copy("v4", ClueFormat.Testimony) }).ToList();
            CollectionAssert.Contains(Validate(g, tooManyVerbal).Errors, ContractValidationErrorCode.CONTRACT_INVALID);

            var tooManyPhysical = withinLimit.Concat(new[] { Copy("p4", ClueFormat.Document) }).ToList();
            CollectionAssert.Contains(Validate(g, tooManyPhysical).Errors, ContractValidationErrorCode.CONTRACT_INVALID);
        }

        [Test]
        public void ContradictionIndex_CountsOnlyRebuttalsTheJudgeActuallyKnows()
        {
            var g = Make(7);
            var occupancy = new RoomOccupancyTracker(g.Rooms);
            ContractTimelineApplier.Apply(g.Timeline, g.Contract, g.Registry, occupancy);

            var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);
            var index = EvidenceMaterialGenerator.BuildContradictionIndex(g.Materials.Materials);

            var witness = g.Registry.Get(Witness);
            Assert.AreEqual(ContradictionLevel.Clear, index.Evaluate(alibi.MaterialId, witness.Knows),
                "핵심 사건을 직접 본 인물은 거짓 알리바이와 모순되는 정보를 안다.");
            Assert.AreEqual(ContradictionLevel.None, index.Evaluate(alibi.MaterialId, _ => false),
                "반박 정보를 모르면 모순으로 보지 않는다.");
            Assert.AreEqual(ContradictionLevel.Clear,
                index.Evaluate(alibi.MaterialId, id => id == alibi.RebuttedByMaterialIds[0]),
                "반박 자료를 직접 보유한 판정자도 모순을 안다.");
        }

        [Test]
        public void NpcCardJudge_UsesMaterialRebuttals_ForFalseAlibiCard()
        {
            var g = Make(8);
            ContractTimelineApplier.Apply(g.Timeline, g.Contract, g.Registry, new RoomOccupancyTracker(g.Rooms));
            var alibi = g.Materials.Materials.Single(m => m.IsFalseOrFabricated);
            var index = EvidenceMaterialGenerator.BuildContradictionIndex(g.Materials.Materials);

            var judge = new NpcCardJudge(g.Registry.Get(Witness), new NpcRelationshipStore(),
                new LieDetectionService(new ClueRepository(), new Random(1)), index);
            var judgement = judge.Judge(new CardJudgementInput { PresenterId = "player", ClueId = alibi.MaterialId });

            Assert.AreEqual(ContradictionLevel.Clear, judgement.Contradiction);
            Assert.IsTrue(judgement.RaisesQuestion);
        }
    }
}
