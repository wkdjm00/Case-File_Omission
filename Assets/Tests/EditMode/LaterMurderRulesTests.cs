using System;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>사용자 지정 2026-11-20: 조사 중 추가 피해자(입막음 살해), 최초 피해자 포함 최대 5명.</summary>
    public class LaterMurderRulesTests
    {
        /// <summary>가해자와 범행을 아는 NPC를 단둘이 두고 시간을 보내 입막음 살해가 일어나는 의뢰를 찾는다.</summary>
        private static (GameSession s, string target) FindSilencing()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                s.CompletePrologue();
                s.OfferContract();
                if (!s.AcceptContract().Success) continue;
                string perp = s.CurrentContract.Truth.PerpetratorId;
                if (!s.Npcs.Get(perp).IsAlive || !s.Occupancy.HasPosition(perp)) continue;
                var target = s.Npcs.GetByContract(s.CurrentContract.ContractId).FirstOrDefault(n => n.NpcId != perp && n.IsAlive
                    && s.Materials.Any(m => (m.IdentifiesPerpetrator || m.SubjectNpcId == perp) && m.StatedInfoIds.Any(n.Knows)));
                if (target == null) continue;

                for (int turn = 0; turn < 40 && !s.State.IsTimeBlockEnded; turn++)
                {
                    // 매 턴 둘만 같은 방(플레이어·경찰·다른 NPC 없음)에 둔다.
                    string room = s.Rooms.GetAdjacentRooms(s.PlayerRoom).First();
                    foreach (var n in s.Npcs.GetByContract(s.CurrentContract.ContractId).Where(n => s.Occupancy.HasPosition(n.NpcId)))
                        if (n.NpcId != perp && n.NpcId != target.NpcId && s.Occupancy.GetRoom(n.NpcId) == room) s.Occupancy.PlaceAt(n.NpcId, s.PlayerRoom);
                    foreach (var p in Police.PoliceRoster.AllPoliceIds.Where(s.Occupancy.HasPosition)) s.Occupancy.PlaceAt(p, s.PlayerRoom);
                    s.Occupancy.PlaceAt(perp, room);
                    s.Occupancy.PlaceAt(target.NpcId, room);
                    s.PassTime(1);
                    if (!target.IsAlive) return (s, target.NpcId);
                }
            }
            Assert.Inconclusive("입막음 살해가 일어나는 의뢰를 찾지 못했다.");
            return default;
        }

        [Test]
        public void Perpetrator_SilencesWitnessAlone_RecordedAndSaved()
        {
            var (s, target) = FindSilencing();
            CollectionAssert.Contains(s.Campaign.LaterVictimIds, target);
            Assert.IsTrue(s.Occupancy.HasPosition(target), "시신은 그 방에 남는다");
            Assert.That(s.ContractDeathCount, Is.LessThanOrEqualTo(GameSession.MaxContractDeaths));

            var loaded = GameSession.Load(SaveGameService.FromJson(SaveGameService.ToJson(s.CaptureSave())), new GameSessionConfig { Random = new Random(1) });
            CollectionAssert.AreEqual(s.Campaign.LaterVictimIds, loaded.Campaign.LaterVictimIds);
            Assert.IsFalse(loaded.Npcs.Get(target).IsAlive);
        }
    }
}
