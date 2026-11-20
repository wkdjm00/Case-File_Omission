using System;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Core;
using CaseFileOmission.Game;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>TEST_TEMP 현장 조사: 담당 경찰 순찰·조사, NPC '조사' 행동의 효과, 저장 복원.</summary>
    public class SceneInvestigationTests
    {
        private static GameSession StartContract(int seed = 7)
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
            s.StartNewGame();
            s.CompletePrologue();
            s.OfferContract();
            Assert.IsTrue(s.AcceptContract().Success);
            return s;
        }

        [Test]
        public void HandlerPolice_InspectsObjectsHourly_AndCountsFoundCluesPerRoom()
        {
            var s = StartContract();
            Assume.That(s.GetInvestigationObjects().Count, Is.GreaterThan(0));
            Assert.AreEqual(0, s.Campaign.PoliceInspectedObjectIds.Count, "의뢰 시작 시에는 경찰이 본 대상이 없다.");

            Assert.IsTrue(s.PassTime(GameStateController.TimeSlotsPerBlock / 2).Success); // 4시간

            Assert.That(s.Campaign.PoliceInspectedObjectIds.Count, Is.InRange(1, 4), "1시간에 대상 하나씩만 본다.");
            Assert.That(s.RecentInvestigations.Any(r => r.ActorId == s.HandlerPoliceId), "경찰 조사 기록이 남는다.");
            var room = s.GetInvestigationObjects().First(o => o.ObjectId == s.Campaign.PoliceInspectedObjectIds[0]).RoomId;
            int expected = s.Materials.Count(m => m.Acquisition == MaterialAcquisition.InvestigateRoom && m.RoomId == room
                                                  && s.Campaign.PoliceInspectedObjectIds.Contains(m.ObjectId));
            Assert.AreEqual(expected, s.PoliceCluesFoundIn(room));
            Assert.That(s.Clues.GetAllClueIds(s.CurrentContract.ContractId), Is.Empty, "경찰 조사로 플레이어 단서가 생기지 않는다.");
        }

        [Test]
        public void NpcInvestigateCandidate_TeachesFactsOfObjectInSameRoom_Once()
        {
            // 의뢰 시작 시점에 NPC가 조사 대상이 있는 방에 있고 아직 모르는 사실이 남은 시드를 찾는다(시드 1에서 성립).
            for (int seed = 1; seed <= 10; seed++)
                if (TryInvestigateOnce(StartContract(seed))) return;
            Assert.Inconclusive("시드 1~10 모두 시작 시점에 조사 후보가 있는 NPC가 없다.");
        }

        private static bool TryInvestigateOnce(GameSession s)
        {
            var source = new NpcSceneInvestigationSource(s);
            foreach (var npc in s.Npcs.GetByContract(s.CurrentContract.ContractId).Where(n => n.IsAlive && s.Occupancy.HasPosition(n.NpcId)))
            {
                string room = s.Occupancy.GetRoom(npc.NpcId);
                var candidate = source.GetCandidates(npc, room, s.WorldMinute).FirstOrDefault();
                if (candidate == null) continue;

                var facts = s.Materials.Where(m => m.ObjectId == candidate.TargetId && m.Acquisition == MaterialAcquisition.InvestigateRoom)
                    .SelectMany(m => m.StatedInfoIds).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                candidate.Execute();
                Assert.That(facts.All(npc.Knows), "조사한 대상의 자료가 말하는 사실을 알게 된다.");
                Assert.That(source.GetCandidates(npc, room, s.WorldMinute).All(c => c.TargetId != candidate.TargetId), "이미 다 아는 대상은 다시 후보가 되지 않는다.");
                Assert.That(s.RecentInvestigations.Any(r => r.ActorId == npc.NpcId && r.ObjectId == candidate.TargetId));
                return true;
            }
            return false;
        }

        [Test]
        public void PoliceInspectedObjects_SurviveSaveLoad()
        {
            var s = StartContract();
            s.PassTime(8);
            Assume.That(s.Campaign.PoliceInspectedObjectIds.Count, Is.GreaterThan(0));

            var json = SaveLoad.SaveGameService.ToJson(s.CaptureSave());
            var loaded = GameSession.Load(SaveLoad.SaveGameService.FromJson(json), new GameSessionConfig { Random = new Random(1) });

            CollectionAssert.AreEqual(s.Campaign.PoliceInspectedObjectIds, loaded.Campaign.PoliceInspectedObjectIds);
        }

        [Test]
        public void NewContract_ClearsPoliceInspection()
        {
            var s = StartContract();
            s.PassTime(8);
            s.AbandonContract();
            s.OfferContract();
            Assert.IsTrue(s.AcceptContract().Success);
            Assert.AreEqual(0, s.Campaign.PoliceInspectedObjectIds.Count);
            Assert.AreEqual(0, s.RecentInvestigations.Count);
        }
    }
}
