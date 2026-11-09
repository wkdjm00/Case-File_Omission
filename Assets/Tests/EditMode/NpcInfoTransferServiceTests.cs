using System;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class NpcInfoTransferServiceTests
    {
        private static NpcState BuildSender(NpcPersonality personality, bool isPerpetrator)
        {
            var npc = new NpcState("npc-1", "contract-1",
                isPerpetrator ? NpcActualRole.Perpetrator : NpcActualRole.Witness, personality);
            npc.LearnInfo("info-1", NpcJudgement.Believe);
            return npc;
        }

        private static NpcState BuildReceiver(string npcId) =>
            new NpcState(npcId, "contract-1", NpcActualRole.Witness, NpcPersonality.Indifferent);

        [Test]
        public void TryTransfer_SenderDoesNotKnowInfo_NotAttempted()
        {
            var sender = new NpcState("npc-1", "contract-1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(1));

            var result = service.TryTransfer(sender, receiver, "info-unknown", true, SuspicionLevel.None, false, false);

            Assert.AreEqual(InfoTransferOutcome.NotAttempted, result.Outcome);
        }

        [Test]
        public void TryTransfer_CannotContact_NotAttempted()
        {
            var sender = BuildSender(NpcPersonality.BlindlyTrusting, false);
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(1));

            var result = service.TryTransfer(sender, receiver, "info-1", false, SuspicionLevel.None, false, false);

            Assert.AreEqual(InfoTransferOutcome.NotAttempted, result.Outcome);
        }

        [Test]
        public void TryTransfer_SuccessfulTransfer_IsNotRepeatedAfterward()
        {
            var sender = BuildSender(NpcPersonality.BlindlyTrusting, false);
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(2)); // 높은 확률 보정 + 시드로 성공 유도

            var first = service.TryTransfer(sender, receiver, "info-1", true, SuspicionLevel.None, true, false);
            Assume.That(first.Outcome == InfoTransferOutcome.Succeeded || first.Outcome == InfoTransferOutcome.Failed);

            if (first.Outcome == InfoTransferOutcome.Succeeded)
            {
                var second = service.TryTransfer(sender, receiver, "info-1", true, SuspicionLevel.None, true, false);
                Assert.AreEqual(InfoTransferOutcome.AlreadyTransferred, second.Outcome);
            }
        }

        [Test]
        public void TryTransfer_Success_ReceiverActuallyLearnsInfo()
        {
            var sender = BuildSender(NpcPersonality.BlindlyTrusting, false);
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(2));

            var result = service.TryTransfer(sender, receiver, "info-1", true, SuspicionLevel.None, true, false);

            if (result.Outcome == InfoTransferOutcome.Succeeded)
            {
                Assert.IsTrue(receiver.Knows("info-1"), "전달에 성공하면 수신자가 실제로 정보를 알게 되어야 한다.");
                Assert.AreEqual(NpcJudgement.Withhold, receiver.Beliefs["info-1"]);
            }
            else
            {
                Assert.IsFalse(receiver.Knows("info-1"));
            }
        }

        [Test]
        public void TryTransfer_Failure_ReceiverDoesNotLearnInfo()
        {
            var sender = BuildSender(NpcPersonality.Secretive, true); // 확률을 최대한 낮춘 조합
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(999));

            var result = service.TryTransfer(sender, receiver, "info-1", true, SuspicionLevel.Strong, false, true);

            if (result.Outcome == InfoTransferOutcome.Failed)
                Assert.IsFalse(receiver.Knows("info-1"));
        }

        [Test]
        public void TryTransfer_SecretivePersonality_LowersProbabilityComparedToBlindlyTrusting()
        {
            var secretive = BuildSender(NpcPersonality.Secretive, false);
            var trusting = BuildSender(NpcPersonality.BlindlyTrusting, false);
            var receiverA = BuildReceiver("npc-2");
            var receiverB = BuildReceiver("npc-3");

            var service = new NpcInfoTransferService(new Random(1));
            var secretiveResult = service.TryTransfer(secretive, receiverA, "info-1", true, SuspicionLevel.None, false, false);
            var trustingResult = service.TryTransfer(trusting, receiverB, "info-1", true, SuspicionLevel.None, false, false);

            Assert.Less(secretiveResult.ProbabilityUsed, trustingResult.ProbabilityUsed);
        }

        [Test]
        public void TryTransfer_ProbabilityNeverExceedsBounds()
        {
            var npc = BuildSender(NpcPersonality.BlindlyTrusting, false);
            var receiver = BuildReceiver("npc-2");
            var service = new NpcInfoTransferService(new Random(1));

            var result = service.TryTransfer(npc, receiver, "info-1", true, SuspicionLevel.None, true, false);

            Assert.GreaterOrEqual(result.ProbabilityUsed, 5f);
            Assert.LessOrEqual(result.ProbabilityUsed, 95f);
        }
    }
}
