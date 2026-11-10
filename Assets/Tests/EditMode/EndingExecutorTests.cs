using System.Collections.Generic;
using CaseFileOmission.Endings;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class EndingExecutorTests
    {
        [Test]
        public void TryExecute_FirstCall_Succeeds()
        {
            var executor = new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));

            bool result = executor.TryExecute(GameEndingType.Arrest);

            Assert.IsTrue(result);
            Assert.IsTrue(executor.HasExecuted);
            Assert.AreEqual(GameEndingType.Arrest, executor.ExecutedEnding);
        }

        [Test]
        public void TryExecute_SecondCall_DoesNotOverwrite()
        {
            var executor = new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));

            executor.TryExecute(GameEndingType.Arrest);
            bool second = executor.TryExecute(GameEndingType.OrganizationTrustZero);

            Assert.IsFalse(second);
            Assert.AreEqual(GameEndingType.Arrest, executor.ExecutedEnding, "이미 시작된 엔딩을 다른 조건이 덮어쓰면 안 된다.");
        }

        [Test]
        public void TryExecute_None_DoesNotExecute()
        {
            var executor = new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));

            bool result = executor.TryExecute(GameEndingType.None);

            Assert.IsFalse(result);
            Assert.IsFalse(executor.HasExecuted);
        }

        [Test]
        public void ShouldStopGameLoop_TrueForGameOverEnding()
        {
            var executor = new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));
            executor.TryExecute(GameEndingType.Arrest);

            Assert.IsTrue(executor.ShouldStopGameLoop);
        }

        [Test]
        public void ShouldStopGameLoop_FalseForMainStoryEnding()
        {
            var executor = new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));
            executor.TryExecute(GameEndingType.RebelRecruitAccepted);

            Assert.IsFalse(executor.ShouldStopGameLoop, "메인 서사 엔딩 이후에는 의뢰 진행이 계속되어야 한다.");
        }

        [Test]
        public void TryEvaluateAndExecute_ArrestCondition_ExecutesArrest()
        {
            var suspicion = new PoliceSuspicionService();
            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f });

            var evaluator = new GameEndEvaluator(suspicion, new OrganizationState());
            var executor = new EndingExecutor(evaluator);

            bool executed = executor.TryEvaluateAndExecute("player", new List<string>());

            Assert.IsTrue(executed);
            Assert.AreEqual(GameEndingType.Arrest, executor.ExecutedEnding);
        }

        [Test]
        public void TryEvaluateAndExecute_AfterAlreadyExecuted_DoesNothing()
        {
            var suspicion = new PoliceSuspicionService();
            var evaluator = new GameEndEvaluator(suspicion, new OrganizationState());
            var executor = new EndingExecutor(evaluator);
            executor.TryExecute(GameEndingType.PhoneCallRejected);

            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f }); // 이후에 체포 조건이 충족돼도

            bool executed = executor.TryEvaluateAndExecute("player", new List<string>());

            Assert.IsFalse(executed);
            Assert.AreEqual(GameEndingType.PhoneCallRejected, executor.ExecutedEnding);
        }

        [Test]
        public void MainStoryEnding_DoesNotBlockLaterGameOver()
        {
            // 10 확정: 메인 서사 엔딩 뒤에도 의뢰 반복 진행은 계속된다 — 그 뒤의 체포 등은 여전히 실행되어야 한다.
            var suspicion = new PoliceSuspicionService();
            var executor = new EndingExecutor(new GameEndEvaluator(suspicion, new OrganizationState()));

            Assert.IsTrue(executor.TryExecute(GameEndingType.BossHiddenEnding));
            Assert.IsFalse(executor.TryExecute(GameEndingType.RebelRecruitAccepted), "메인 서사 엔딩은 한 번만 실행된다.");

            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f });
            Assert.IsTrue(executor.TryEvaluateAndExecute("player", new List<string>()));
            Assert.AreEqual(GameEndingType.Arrest, executor.GameOverEnding);
            Assert.AreEqual(GameEndingType.BossHiddenEnding, executor.MainNarrativeEnding);
            Assert.IsTrue(executor.ShouldStopGameLoop);
        }

        [Test]
        public void Arrest_UsesHandlerPolice_WhenContractIsActive()
        {
            // 07 2026-11-09: 배정된 경찰의 의심도 100이 체포 조건이다.
            var suspicion = new PoliceSuspicionService();
            suspicion.ApplyBatch(PoliceRoster.PoliceB, "player", new[] { 100f });
            var evaluator = new GameEndEvaluator(suspicion, new OrganizationState());

            Assert.AreEqual(GameEndingType.None, evaluator.EvaluateAutomaticEndings("player", new List<string>(), PoliceRoster.PoliceA));
            Assert.AreEqual(GameEndingType.Arrest, evaluator.EvaluateAutomaticEndings("player", new List<string>(), PoliceRoster.PoliceB));
        }

        [Test]
        public void Arrest_IgnoresDeceasedPolice()
        {
            var suspicion = new PoliceSuspicionService();
            suspicion.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 100f });
            var evaluator = new GameEndEvaluator(suspicion, new OrganizationState());

            Assert.AreEqual(GameEndingType.None, evaluator.EvaluateAutomaticEndings("player", new List<string> { PoliceRoster.PoliceA }));
        }

        [Test]
        public void TryEvaluateAndExecute_WithPoliceLifeStateService_Works()
        {
            var lifeState = new PoliceLifeStateService();
            foreach (var id in PoliceRoster.AllPoliceIds)
                lifeState.MarkDeceased(id);

            var evaluator = new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState());
            var executor = new EndingExecutor(evaluator);

            bool executed = executor.TryEvaluateAndExecute("player", lifeState);

            Assert.IsTrue(executed);
            Assert.AreEqual(GameEndingType.PoliceAllEliminated, executor.ExecutedEnding);
        }
    }
}
