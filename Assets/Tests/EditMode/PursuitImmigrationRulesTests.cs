using System;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Game;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>사용자 확정 2026-11-20: 이민 엔딩 자격·선택, 경찰 추적 단계 진행, 사망 경찰 집계 제외.</summary>
    public class PursuitImmigrationRulesTests
    {
        private static GameSession StartContract(Func<ContractInstance, bool> want, int maxSeed = 60)
        {
            for (int seed = 1; seed <= maxSeed; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                // 프롤로그 뒤(메인 사건 2단계)에는 메인 단서 제약으로 살인 의뢰만 나오므로, 유형을 고를 수 있게 프롤로그 전 의뢰를 쓴다.
                s.StartNewGame();
                var offer = s.OfferContract();
                if (offer == null || !want(offer.Contract)) continue;
                Assert.IsTrue(s.AcceptContract().Success);
                return s;
            }
            Assert.Inconclusive("조건에 맞는 의뢰를 만들지 못했다.");
            return null;
        }

        [Test]
        public void Immigration_RequiresActualMurderAndAccusedStateInCurrentCase()
        {
            var s = StartContract(c => c.PrimaryCrimeType == CrimeType.Murder);
            Assert.IsFalse(s.CanEmigrate, "지목 상태 없이는 이민 불가");
            Assert.IsTrue(s.RecordPlayerAccused("test"));
            Assert.IsTrue(s.CanEmigrate);
            Assert.IsTrue(s.Emigrate().Success);
            Assert.AreEqual(GameEndingType.Immigration, s.Endings.GameOverEnding);
            Assert.IsTrue(s.IsGameOver);
        }

        [Test]
        public void Immigration_NotAllowedForNonMurder()
        {
            var s = StartContract(c => c.PrimaryCrimeType != CrimeType.Murder);
            s.RecordPlayerAccused("test");
            Assert.IsFalse(s.CanEmigrate, "실제 유형이 살인이 아니면 자격 없음");
        }

        [Test]
        public void Immigration_BlockedDuringPursuitOrArrestCondition()
        {
            var s = StartContract(c => c.PrimaryCrimeType == CrimeType.Murder);
            s.RecordPlayerAccused("test");
            s.Pursuit.RestoreStage(PursuitStage.GatheringInformation, "death-x");
            Assert.IsFalse(s.CanEmigrate, "추적이 시작되면 이민 불가");
            s.Pursuit.RestoreStage(PursuitStage.NotActive);
            s.PoliceSuspicion.SetSuspicion(s.HandlerPoliceId, s.PlayerId, 100f);
            Assert.IsFalse(s.CanEmigrate, "체포 조건이 있으면 체포 우선");
        }

        [Test]
        public void AccusedState_DoesNotCarryToNextContract_AndSurvivesSave()
        {
            var s = StartContract(c => c.PrimaryCrimeType == CrimeType.Murder);
            s.RecordPlayerAccused("test");
            var loaded = GameSession.Load(SaveLoad.SaveGameService.FromJson(SaveLoad.SaveGameService.ToJson(s.CaptureSave())), new GameSessionConfig { Random = new Random(1) });
            Assert.AreEqual(s.CurrentContract.ContractId, loaded.Campaign.PlayerAccusedCaseId);

            s.AbandonContract();
            s.OfferContract();
            s.AcceptContract();
            Assert.IsNull(s.Campaign.PlayerAccusedCaseId);
        }

        [Test]
        public void ArrestEnding_UsesAnyLivingPolice_NotOnlyAssignedHandler()
        {
            var s = StartContract(_ => true);
            string handler = s.HandlerPoliceId;
            string other = PoliceRoster.AllPoliceIds.First(id => id != handler);
            s.PoliceSuspicion.SetSuspicion(handler, s.PlayerId, 25f);
            s.PoliceSuspicion.SetSuspicion(other, s.PlayerId, 100f);

            s.CheckEndings();

            Assert.AreEqual(GameEndingType.Arrest, s.Endings.GameOverEnding);
            Assert.IsTrue(s.IsGameOver);
        }

        [Test]
        public void PursuitTrigger_IgnoresDeadPolice()
        {
            var service = new PoliceSuspicionService();
            var pursuit = new PolicePursuitController(service, new GameEventLog());
            foreach (var id in PoliceRoster.AllPoliceIds.Take(3))
                service.SetSuspicion(id, "player", 80f);

            var alive = PoliceRoster.AllPoliceIds.Skip(1).ToList(); // 70 이상인 경찰 한 명이 사망
            Assert.IsFalse(pursuit.TryTrigger("player", "death-1", alive), "살아 있는 경찰 중 70 이상이 2명뿐이면 발동하지 않는다");
            Assert.IsTrue(pursuit.TryTrigger("player", "death-2", PoliceRoster.AllPoliceIds));
            Assert.IsFalse(pursuit.TryTrigger("player", "death-2", PoliceRoster.AllPoliceIds), "같은 사망으로 중복 발동 금지");
        }

        [Test]
        public void PursuitStages_AdvanceThroughGatheringToPlayerResponse()
        {
            var s = StartContract(_ => true);
            foreach (var id in PoliceRoster.AllPoliceIds.Take(3))
                s.PoliceSuspicion.SetSuspicion(id, s.PlayerId, 80f);
            Assert.IsTrue(s.Pursuit.TryTrigger(s.PlayerId, "death-test", PoliceRoster.AllPoliceIds));

            s.PassTime(1);
            Assert.AreEqual(PursuitStage.GatheringInformation, s.Pursuit.Stage, "발동 다음 턴부터 정보 수집");

            s.EndInvestigationEarly();
            Assert.IsTrue(s.StartMeeting(s.Clues.GetHeldByPlayerClueIds(s.CurrentContract.ContractId).Take(5).ToList()).Success);
            Assert.AreEqual(PursuitStage.PlayerResponse, s.Pursuit.Stage, "회의가 플레이어 대응 단계");
            Assert.IsFalse(s.IsGameOver, "대응 단계 자체로 게임 오버가 되지 않는다");
        }
    }
}
