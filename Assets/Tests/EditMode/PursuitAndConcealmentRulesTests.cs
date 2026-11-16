using System;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using CaseFileOmission.Police;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>2026-11-16 최종 확정: 경찰 추적 성공 기준(70 이상 2명 이하)·체포 우선, 은폐는 지정한 단서 하나의 접근 상태만 변경.</summary>
    public class PursuitAndConcealmentRulesTests
    {
        private const string Player = "player";

        private static (PolicePursuitController pursuit, PoliceSuspicionService suspicion) TriggeredPursuit(params float[] values)
        {
            var suspicion = new PoliceSuspicionService();
            var ids = PoliceRoster.AllPoliceIds.ToList();
            for (int i = 0; i < ids.Count; i++) suspicion.SetSuspicion(ids[i], Player, i < values.Length ? values[i] : 0f);
            var pursuit = new PolicePursuitController(suspicion, new GameEventLog());
            Assert.IsTrue(pursuit.TryTrigger(Player, "death-1"));
            return (pursuit, suspicion);
        }

        [Test]
        public void Pursuit_ContinuesWhileThreeOrMoreAtSeventy()
        {
            var (pursuit, _) = TriggeredPursuit(80, 75, 70, 40, 10);
            Assert.AreEqual(PursuitCheckResult.Continuing, pursuit.Evaluate(Player, PoliceRoster.AllPoliceIds));
            Assert.AreNotEqual(PursuitStage.NotActive, pursuit.Stage);
        }

        [Test]
        public void Pursuit_SucceedsWhenTwoOrFewerAtSeventy_AdjustsToFifty_AndEnds()
        {
            var (pursuit, suspicion) = TriggeredPursuit(80, 75, 70, 40, 10);
            var ids = PoliceRoster.AllPoliceIds.ToList();
            suspicion.SetSuspicion(ids[2], Player, 69f);

            Assert.AreEqual(PursuitCheckResult.CompletedSuccess, pursuit.Evaluate(Player, ids));
            Assert.AreEqual(PursuitStage.NotActive, pursuit.Stage);
            Assert.IsNull(pursuit.ActiveTriggerId);
            Assert.AreEqual(50f, suspicion.GetSuspicion(ids[0], Player));
            Assert.AreEqual(50f, suspicion.GetSuspicion(ids[1], Player));
            Assert.AreEqual(50f, suspicion.GetSuspicion(ids[2], Player));
            Assert.AreEqual(40f, suspicion.GetSuspicion(ids[3], Player), "50 이하인 경찰은 올리지 않는다");
            Assert.AreEqual(PursuitCheckResult.NotActive, pursuit.Evaluate(Player, ids), "추적 종료 뒤에는 다시 판정하지 않는다");
            Assert.IsFalse(pursuit.TryTrigger(Player, "death-1"), "같은 사망 사건으로 다시 발동하지 않는다");
        }

        [Test]
        public void Pursuit_ArrestTakesPriorityOverSuccess()
        {
            var (pursuit, suspicion) = TriggeredPursuit(80, 75, 70, 40, 10);
            var ids = PoliceRoster.AllPoliceIds.ToList();
            suspicion.SetSuspicion(ids[0], Player, 100f);
            suspicion.SetSuspicion(ids[1], Player, 20f);
            suspicion.SetSuspicion(ids[2], Player, 20f);

            Assert.AreEqual(PursuitCheckResult.ArrestRequired, pursuit.Evaluate(Player, ids),
                "다른 경찰이 70 미만으로 내려가도 의심도 100이면 체포가 우선한다");
            Assert.AreNotEqual(PursuitStage.NotActive, pursuit.Stage);
        }

        [Test]
        public void Session_PursuitWithPoliceAtHundred_EndsInArrest()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(5) });
            s.StartNewGame();
            var ids = PoliceRoster.AllPoliceIds.ToList();
            for (int i = 0; i < 3; i++) s.PoliceSuspicion.SetSuspicion(ids[i], s.PlayerId, 80f);
            Assert.IsTrue(s.Pursuit.TryTrigger(s.PlayerId, "death-x"));
            s.PoliceSuspicion.SetSuspicion(ids[4], s.PlayerId, 100f);
            s.CheckEndings();
            Assert.AreEqual(GameEndingType.Arrest, s.Endings.GameOverEnding);
        }

        [Test]
        public void Session_PursuitSuccess_ThroughCheckEndings_AndSaveKeepsActivePursuit()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(6) });
            s.StartNewGame();
            var ids = PoliceRoster.AllPoliceIds.ToList();
            for (int i = 0; i < 3; i++) s.PoliceSuspicion.SetSuspicion(ids[i], s.PlayerId, 80f);
            Assert.IsTrue(s.Pursuit.TryTrigger(s.PlayerId, "death-y"));
            s.CheckEndings();
            Assert.AreEqual(PursuitStage.Triggered, s.Pursuit.Stage, "70 이상이 3명이면 계속");

            var loaded = GameSession.Load(s.CaptureSave(), new GameSessionConfig { Random = new Random(1) });
            Assert.AreEqual(PursuitStage.Triggered, loaded.Pursuit.Stage);
            Assert.AreEqual("death-y", loaded.Pursuit.ActiveTriggerId);

            loaded.PoliceSuspicion.SetSuspicion(ids[0], loaded.PlayerId, 60f);
            loaded.CheckEndings();
            Assert.AreEqual(PursuitStage.NotActive, loaded.Pursuit.Stage);
            Assert.IsFalse(loaded.IsGameOver);
            Assert.AreEqual(50f, loaded.PoliceSuspicion.GetSuspicion(ids[0], loaded.PlayerId));
            Assert.AreEqual(50f, loaded.PoliceSuspicion.GetSuspicion(ids[1], loaded.PlayerId));
        }

        private static ClueDefinition Clue(string id) => new ClueDefinition(id, "c1") { Format = ClueFormat.PhysicalObject, DisplayName = id };

        [Test]
        public void Concealment_ChangesOnlyTheChosenClue_AndKeepsOriginal()
        {
            var repo = new ClueRepository();
            repo.Discover(Clue("a"));
            repo.Discover(Clue("b"));

            repo.GetRecord("a").ApplyConcealment(succeeded: true, detected: false);

            Assert.IsTrue(repo.GetRecord("a").IsConcealed);
            Assert.AreEqual(ConcealmentResult.Succeeded, repo.GetRecord("a").Concealment);
            Assert.IsTrue(repo.Exists("a"), "원본 증거를 삭제하지 않는다");
            Assert.AreEqual(ClueWorldState.HeldByPlayer, repo.GetRecord("a").WorldState, "보유 상태는 바꾸지 않는다");
            Assert.IsFalse(repo.GetRecord("b").IsConcealed, "대상이 아닌 단서는 바꾸지 않는다");
            Assert.AreEqual(ConcealmentResult.NotAttempted, repo.GetRecord("b").Concealment);
        }

        [Test]
        public void Concealment_FailureKeepsAccess_DetectionIsSeparate()
        {
            var record = new ClueRecord("a");
            record.ApplyConcealment(succeeded: false, detected: true);
            Assert.IsFalse(record.IsConcealed);
            Assert.AreEqual(ConcealmentResult.Failed, record.Concealment);
            Assert.IsTrue(record.ConcealmentDetected);

            record.ApplyConcealment(succeeded: true, detected: false);
            Assert.IsTrue(record.IsConcealed);
            Assert.IsTrue(record.ConcealmentDetected, "발각 기록은 성공 여부와 별도로 남는다");
        }

        [Test]
        public void Concealment_DoesNotTouchNpcKnowledge()
        {
            var npc = new NPC.NpcState("n1", "c1", NPC.NpcActualRole.Witness, NPC.NpcPersonality.Timid);
            npc.LearnInfo("info-a", NPC.NpcJudgement.Believe);
            var record = new ClueRecord("info-a");
            record.ApplyConcealment(true, false);
            Assert.IsTrue(npc.Knows("info-a"), "다른 NPC가 이미 아는 정보·기억은 지우지 않는다");
        }
    }
}
