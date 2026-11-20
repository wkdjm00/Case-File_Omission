using System;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>사용자 지정 2026-11-20: 살아 있는 피해자는 담당 경찰 주변에만 머문다.</summary>
    public class LivingVictimRulesTests
    {
        [Test]
        public void LivingVictim_StaysInHandlerPoliceRoom()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                s.CompletePrologue();
                s.OfferContract();
                if (!s.AcceptContract().Success) continue;
                string victim = s.CurrentContract.Truth.VictimId;
                if (victim == null || !s.Npcs.Exists(victim) || !s.Occupancy.HasPosition(victim) || !s.Occupancy.HasPosition(s.HandlerPoliceId)) continue;

                // 초반 의뢰는 대부분 살인이라 피해자가 숨져 있다 — 저장 데이터에서 피해자를 살려 둔 채 불러와 규칙만 본다.
                var save = s.CaptureSave();
                save.World.Npc.Npcs.Single(n => n.NpcId == victim).IsAlive = true;
                s = GameSession.Load(SaveGameService.FromJson(SaveGameService.ToJson(save)), new GameSessionConfig { Random = new Random(seed) });
                Assume.That(s.Npcs.Get(victim).IsAlive);

                // 경찰 옆 방에 떼어 놓아도 다음 턴에 경찰 방으로 붙고, 그 뒤로는 떨어지지 않는다.
                string far = s.Rooms.GetAdjacentRooms(s.Occupancy.GetRoom(s.HandlerPoliceId)).First();
                s.Occupancy.PlaceAt(victim, far);
                for (int i = 0; i < 4; i++)
                {
                    s.PassTime(1);
                    if (s.State.IsTimeBlockEnded || s.CurrentContract == null) break;
                    Assert.AreEqual(s.Occupancy.GetRoom(s.HandlerPoliceId), s.Occupancy.GetRoom(victim), $"seed {seed} turn {i}");
                }
                return;
            }
            Assert.Inconclusive("피해자와 담당 경찰이 배치된 의뢰를 찾지 못했다.");
        }
    }
}
