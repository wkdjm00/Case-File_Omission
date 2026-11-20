using System;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>06 절도·시신 탈취(사용자 지정 2026-11-20: 조직 지정 물건 또는 금전적으로 도움이 될 단서만), 단서 버리기·줍기.</summary>
    public class TheftRulesTests
    {
        /// <summary>훔칠 수 있는 것이 있는 방에 플레이어를 둔 의뢰를 찾는다.</summary>
        private static (GameSession s, EvidenceMaterial m) FindStealable(Func<GameSession, EvidenceMaterial, bool> want)
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                s.CompletePrologue();
                s.OfferContract();
                if (!s.AcceptContract().Success) continue;
                foreach (var room in s.Materials.Where(x => x.Acquisition == MaterialAcquisition.InvestigateRoom).Select(x => x.RoomId).Distinct().ToList())
                {
                    var path = s.Rooms.FindPath(s.PlayerRoom, room);
                    if (room != s.PlayerRoom && path == null) continue;
                    if (path != null) foreach (var r in path) s.MovePlayer(r);
                    var m = s.StealableMaterialsHere().FirstOrDefault(x => want(s, x));
                    if (m != null) return (s, m);
                }
            }
            Assert.Inconclusive("훔칠 수 있는 대상이 있는 의뢰를 찾지 못했다.");
            return default;
        }

        [Test]
        public void StealingBody_Costs15Minutes_RemovesFromScene_RecordsWitnesses()
        {
            var (s, body) = FindStealable((g, m) => g.IsBodyMaterial(m.MaterialId));
            int before = s.State.RemainingTimeSlots;
            Assert.IsTrue(s.Steal(body.MaterialId).Success);

            Assert.AreEqual(before - GameSession.BodyRemovalCost, s.State.RemainingTimeSlots);
            Assert.IsFalse(s.Occupancy.HasPosition(s.CurrentContract.Truth.VictimId), "시신은 현장에서 사라진다");
            Assert.That(s.Clues.Exists(body.MaterialId) && s.Clues.GetRecord(body.MaterialId).WorldState == ClueWorldState.HeldByPlayer);
            Assert.IsFalse(s.IsStealable(body), "같은 것을 두 번 가져갈 수 없다");
            var incident = s.Campaign.TheftIncidents.FirstOrDefault(i => i.MaterialId == body.MaterialId);
            if (incident != null) Assert.That(incident.WitnessIds.Count, Is.GreaterThan(0), "목격자가 있을 때만 부수사건을 바로 만든다");
        }

        [Test]
        public void OnlyOrgTargetValuablesOrBody_AreStealable()
        {
            var (s, m) = FindStealable((g, x) => true);
            foreach (var x in s.Materials.Where(x => x.Acquisition == MaterialAcquisition.InvestigateRoom && x.RoomId == s.PlayerRoom))
            {
                bool expected = s.IsBodyMaterial(x.MaterialId) || x.MaterialId == s.CurrentContract.OrgCondition.TargetId
                                || (x.Format == ClueFormat.PhysicalObject && x.ObjectId != null && GameSession.ValuableObjectNames.Contains(CaseCatalog.ObjectName(x.ObjectId)));
                Assert.AreEqual(expected, s.IsStealable(x), x.MaterialId);
            }
        }

        [Test]
        public void TheftState_SurvivesSaveLoad()
        {
            var (s, m) = FindStealable((g, x) => true);
            s.Steal(m.MaterialId);
            var loaded = GameSession.Load(SaveLoad.SaveGameService.FromJson(SaveLoad.SaveGameService.ToJson(s.CaptureSave())), new GameSessionConfig { Random = new Random(1) });
            CollectionAssert.AreEqual(s.Campaign.StolenMaterialIds, loaded.Campaign.StolenMaterialIds);
            Assert.AreEqual(s.Campaign.TheftIncidents.Count, loaded.Campaign.TheftIncidents.Count);
        }

        [Test]
        public void DroppedClue_CanBePickedUpAgainInSameRoom()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(7) });
            s.StartNewGame();
            s.CompletePrologue();
            s.OfferContract();
            s.AcceptContract();
            var obj = s.Materials.First(x => x.MaterialId.Contains("scene-trace"));
            foreach (var r in s.Rooms.FindPath(s.PlayerRoom, obj.RoomId) ?? Array.Empty<string>()) s.MovePlayer(r);
            s.InvestigateObject(obj.ObjectId);
            Assume.That(s.Clues.Exists(obj.MaterialId));

            Assert.IsTrue(s.DropClue(obj.MaterialId).Success);
            CollectionAssert.Contains(s.DroppedCluesHere(), obj.MaterialId);
            Assert.IsTrue(s.PickUpClue(obj.MaterialId).Success);
            Assert.AreEqual(ClueWorldState.HeldByPlayer, s.Clues.GetRecord(obj.MaterialId).WorldState);
        }
    }
}
