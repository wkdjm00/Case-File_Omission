using System;
using System.Linq;
using CaseFileOmission.Endings;
using CaseFileOmission.Game;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>07 공개 역할, 08 엿듣기(현재 대화만), 10 체포 조건 ②(공식 결론이 플레이어 지목).</summary>
    public class PublicRoleAndOverhearTests
    {
        private static GameSession Start(int seed = 7)
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
            s.StartNewGame();
            s.CompletePrologue();
            Assert.IsNotNull(s.OfferContract());
            Assert.IsTrue(s.AcceptContract().Success);
            return s;
        }

        [Test]
        public void PublicRole_StartsAsCaseRelated_AndNeverRevealsActualPerpetrator()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var s = Start(seed);
                var c = s.CurrentContract;
                foreach (var npc in s.Npcs.GetByContract(c.ContractId))
                {
                    var role = s.GetPublicRole(npc.NpcId);
                    if (npc.ActualRole == NpcActualRole.Victim)
                        Assert.That(role, Is.EqualTo(PublicRole.Victim).Or.EqualTo(PublicRole.CaseRelated));
                    else
                        Assert.AreEqual(PublicRole.CaseRelated, role, $"seed {seed}: {npc.ActualRole}는 처음에 '사건 관련 인물'");
                }
            }
        }

        [Test]
        public void PublicRole_Victim_OnlyWhenPoliceSnapshotNamesVictim()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var s = Start(seed);
                var c = s.CurrentContract;
                if (c.Truth.VictimId == null || !s.Npcs.Exists(c.Truth.VictimId)) continue;
                string name = s.Npcs.Get(c.Truth.VictimId).DisplayName;
                bool named = c.PoliceKnownFacts.Any(f => f.Contains(name));
                Assert.AreEqual(named ? PublicRole.Victim : PublicRole.CaseRelated, s.GetPublicRole(c.Truth.VictimId), $"seed {seed}");
            }
        }

        [Test]
        public void Overhear_OnlyCurrentTurnExchangeInSameRoom()
        {
            var s = Start();
            var npcs = s.Npcs.GetByContract(s.CurrentContract.ContractId).Where(n => n.IsAlive).Take(2).ToList();
            Assume.That(npcs.Count, Is.EqualTo(2));
            foreach (var n in npcs) s.Occupancy.PlaceAt(n.NpcId, s.PlayerRoom);

            s.RecentExchanges.Add(new OverheardExchange
            {
                SpeakerId = npcs[0].NpcId, ListenerId = npcs[1].NpcId, InfoId = "info-old", RoomId = s.PlayerRoom, Minute = s.WorldMinute - 15
            });
            Assert.AreEqual(0, s.CurrentOverhearableExchanges().Count, "지나간 대화는 들을 수 없다");
            int before = s.State.RemainingTimeSlots;
            Assert.IsFalse(s.Overhear().Success);
            Assert.AreEqual(before, s.State.RemainingTimeSlots, "들을 대화가 없으면 시간을 쓰지 않는다");

            s.RecentExchanges.Add(new OverheardExchange
            {
                SpeakerId = npcs[0].NpcId, ListenerId = npcs[1].NpcId, InfoId = "info-now", RoomId = s.PlayerRoom, Minute = s.WorldMinute
            });
            Assert.AreEqual(1, s.CurrentOverhearableExchanges().Count);
            var result = s.Overhear();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(before - GameSession.OverhearCost, s.State.RemainingTimeSlots);
            Assert.AreEqual(1, result.NewClueIds.Count);
        }

        [Test]
        public void OfficialConclusionAccusingPlayer_ArrestsImmediately_WithoutOrganizationResult()
        {
            var s = Start();
            float trust = s.Organization.Trust;
            s.EndInvestigationEarly();
            Assert.IsTrue(s.StartMeeting().Success);
            var votes = s.MeetingFlow.ParticipantIds.Where(id => id != s.PlayerId).ToDictionary(id => id, _ => true);

            var conclusion = s.ProposeConclusion(s.PlayerId, votes);

            Assert.AreEqual(ConclusionOutcome.Adopted, conclusion.Outcome);
            Assert.AreEqual(GameEndingType.Arrest, s.Endings.GameOverEnding);
            Assert.IsTrue(s.IsGameOver);
            Assert.AreEqual(trust, s.Organization.Trust, "체포 뒤 조직 조건·신뢰도 처리를 실행하지 않는다");
        }

        [Test]
        public void OfficialConclusion_MarksAccusedAsPublicPerpetrator_AndSurvivesSave()
        {
            var s = Start();
            var accused = s.Npcs.GetByContract(s.CurrentContract.ContractId).First(n => n.IsAlive).NpcId;
            s.EndInvestigationEarly();
            Assert.IsTrue(s.StartMeeting().Success);
            var votes = s.MeetingFlow.ParticipantIds.Where(id => id != s.PlayerId).ToDictionary(id => id, _ => true);
            Assert.AreEqual(ConclusionOutcome.Adopted, s.ProposeConclusion(accused, votes).Outcome);
            Assert.AreEqual(PublicRole.Perpetrator, s.GetPublicRole(accused));

            var loaded = GameSession.Load(s.CaptureSave(), new GameSessionConfig { Random = new Random(1) });
            Assert.AreEqual(PublicRole.Perpetrator, loaded.GetPublicRole(accused));
        }
    }
}
