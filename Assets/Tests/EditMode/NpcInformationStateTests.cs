using System;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.NPC;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>AI 계약 v1(2026-11-12) NPC 정보 상태 4종과 획득 출처·시점·경로.</summary>
    public class NpcInformationStateTests
    {
        [Test]
        public void ReacquiringFromAnotherSource_AppendsHistory_WithoutOverwriting()
        {
            var npc = new NpcState("n1", "c1", NpcActualRole.Witness, NpcPersonality.Timid);
            npc.LearnInfo("fact", NpcJudgement.Believe, new NpcInfoRecord { State = NpcInformationState.KnowsFact, Method = NpcInfoAcquisitionMethod.DirectObservation, SourceEventId = "e1", Minute = 10 });
            npc.LearnInfo("fact", NpcJudgement.Withhold, new NpcInfoRecord { State = NpcInformationState.Suspects, Method = NpcInfoAcquisitionMethod.NpcTransfer, SourcePersonId = "n2", Minute = 40 });
            npc.LearnInfo("fact", NpcJudgement.Withhold, new NpcInfoRecord { State = NpcInformationState.Suspects, Method = NpcInfoAcquisitionMethod.NpcTransfer, SourcePersonId = "n2", Minute = 55 });

            Assert.AreEqual(2, npc.InfoHistory.Count, "같은 출처의 같은 정보는 다시 쌓지 않는다");
            Assert.AreEqual("e1", npc.InfoHistory[0].SourceEventId, "앞선 출처를 덮지 않는다");
            Assert.AreEqual(NpcInformationState.Suspects, npc.GetInformationState("fact"));
        }

        [Test]
        public void LearningWithoutSource_DoesNotInventOne()
        {
            var npc = new NpcState("n1", "c1", NpcActualRole.Witness, NpcPersonality.Timid);
            npc.LearnInfo("fact", NpcJudgement.Believe);
            Assert.IsTrue(npc.Knows("fact"));
            Assert.IsNull(npc.GetInformationState("fact"));
        }

        [Test]
        public void GeneratedKnowledge_HasSourceRecords_AndSurvivesSaveLoad()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(3) });
            s.StartNewGame();
            s.CompletePrologue();
            s.OfferContract();
            Assume.That(s.AcceptContract().Success);
            var witness = s.Npcs.GetByContract(s.CurrentContract.ContractId).First(n => n.KnownInfoIds.Count > 0);
            Assert.That(witness.InfoHistory, Is.Not.Empty, "시간표 근거로 얻은 정보에는 출처 기록이 있다");
            Assert.That(witness.InfoHistory.All(r => r.Minute.HasValue));

            var loaded = GameSession.Load(SaveGameService.FromJson(SaveGameService.ToJson(s.CaptureSave())), new GameSessionConfig { Random = new Random(3) });
            var back = loaded.Npcs.Get(witness.NpcId);
            Assert.AreEqual(witness.InfoHistory.Count, back.InfoHistory.Count);
            Assert.AreEqual(witness.InfoHistory[0].Method, back.InfoHistory[0].Method);
            Assert.AreEqual(witness.InfoHistory[0].State, back.InfoHistory[0].State);
        }
    }
}
