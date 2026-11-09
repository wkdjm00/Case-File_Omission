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
