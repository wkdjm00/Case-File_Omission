using System;
using CaseFileOmission.Core;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class GameStateControllerTests
    {
        [Test]
        public void BeginContract_SetsInvestigationPhase()
        {
            var controller = new GameStateController();
            controller.BeginContract("contract-1");

            Assert.AreEqual(GamePhase.Investigation, controller.CurrentPhase);
            Assert.AreEqual("contract-1", controller.CurrentContractId);
        }

        [Test]
        public void EnterMeeting_WithoutInvestigation_Throws()
        {
            var controller = new GameStateController();
            Assert.Throws<InvalidOperationException>(() => controller.EnterMeeting());
        }

        [Test]
        public void ResolveContract_WithoutMeeting_Throws()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            Assert.Throws<InvalidOperationException>(() => controller.ResolveContract());
        }

        [Test]
        public void FullLifecycle_TransitionsInOrder()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");
            controller.EnterMeeting();
            controller.ResolveContract();

            Assert.AreEqual(GamePhase.Resolved, controller.CurrentPhase);
        }

        [Test]
        public void ResetToIdle_ClearsContract()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            controller.ResetToIdle();

            Assert.AreEqual(GamePhase.Idle, controller.CurrentPhase);
            Assert.IsNull(controller.CurrentContractId);
        }

        [Test]
        public void BeginContract_StartsAtMorningDayOne()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            Assert.AreEqual(TimeBlock.Morning, controller.CurrentTimeBlock);
            Assert.AreEqual(1, controller.CurrentDay);
        }

        [Test]
        public void AdvanceTimeBlock_MorningToAfternoon_SameDay()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            controller.AdvanceTimeBlock();

            Assert.AreEqual(TimeBlock.Afternoon, controller.CurrentTimeBlock);
            Assert.AreEqual(1, controller.CurrentDay);
        }

        [Test]
        public void AdvanceTimeBlock_AfternoonToMorning_IncrementsDay()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            controller.AdvanceTimeBlock(); // -> 오후, 1일차
            controller.AdvanceTimeBlock(); // -> 오전, 2일차

            Assert.AreEqual(TimeBlock.Morning, controller.CurrentTimeBlock);
            Assert.AreEqual(2, controller.CurrentDay);
        }

        [Test]
        public void AdvanceTimeBlock_DuringMeeting_Throws()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");
            controller.EnterMeeting();

            Assert.Throws<InvalidOperationException>(() => controller.AdvanceTimeBlock());
        }

        [Test]
        public void AdvanceTimeBlock_RaisesEventWithNewBlockAndDay()
        {
            var controller = new GameStateController();
            controller.BeginContract("c1");

            TimeBlock? reportedBlock = null;
            int reportedDay = 0;
            controller.TimeBlockAdvanced += (block, day) =>
            {
                reportedBlock = block;
                reportedDay = day;
            };

            controller.AdvanceTimeBlock();

            Assert.AreEqual(TimeBlock.Afternoon, reportedBlock);
            Assert.AreEqual(1, reportedDay);
        }
    }
}
