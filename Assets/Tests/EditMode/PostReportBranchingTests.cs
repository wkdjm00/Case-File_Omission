using System;
using CaseFileOmission.Endings;
using CaseFileOmission.MainCase;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PostReportBranchingTests
    {
        [Test]
        public void AnswerPhoneQuestion_BeforeTrigger_Throws()
        {
            var branching = new PostReportBranching();
            Assert.Throws<InvalidOperationException>(() => branching.AnswerPhoneQuestion(true));
        }

        [Test]
        public void AnswerPhoneQuestion_Reject_ReturnsPhoneCallRejectedEnding()
        {
            var branching = new PostReportBranching();
            branching.TriggerPhoneCall();

            var ending = branching.AnswerPhoneQuestion(false);

            Assert.AreEqual(GameEndingType.PhoneCallRejected, ending);
            Assert.AreEqual(PhoneCallStage.PlayerKilled, branching.PhoneStage);
        }

        [Test]
        public void AnswerPhoneQuestion_Accept_NoEndingYet_OffersRebelRecruitment()
        {
            var branching = new PostReportBranching();
            branching.TriggerPhoneCall();

            var ending = branching.AnswerPhoneQuestion(true);

            Assert.IsNull(ending, "수락 직후에는 아직 엔딩이 아니다 — 반란군 방문으로 이어진다.");
            Assert.AreEqual(PhoneCallStage.InformationRevealed, branching.PhoneStage);
            Assert.AreEqual(RebelRecruitmentStage.Offered, branching.RebelStage);
        }

        [Test]
        public void DecideRebelRecruitment_BeforeOffered_Throws()
        {
            var branching = new PostReportBranching();
            Assert.Throws<InvalidOperationException>(() => branching.DecideRebelRecruitment(true));
        }

        [Test]
        public void DecideRebelRecruitment_Accept_ReturnsRebelRecruitAccepted()
        {
            var branching = new PostReportBranching();
            branching.TriggerPhoneCall();
            branching.AnswerPhoneQuestion(true);

            var ending = branching.DecideRebelRecruitment(true);

            Assert.AreEqual(GameEndingType.RebelRecruitAccepted, ending);
        }

        [Test]
        public void DecideRebelRecruitment_Reject_ReturnsBossHiddenEnding()
        {
            var branching = new PostReportBranching();
            branching.TriggerPhoneCall();
            branching.AnswerPhoneQuestion(true);

            var ending = branching.DecideRebelRecruitment(false);

            Assert.AreEqual(GameEndingType.BossHiddenEnding, ending);
        }

        [Test]
        public void PhoneRejectionAndRebelRejection_AreDistinctEndings()
        {
            // 확정 사항: 수화기 질문 거절과 반란군 영입 거절은 서로 다른 분기다.
            var branchA = new PostReportBranching();
            branchA.TriggerPhoneCall();
            var phoneRejectEnding = branchA.AnswerPhoneQuestion(false);

            var branchB = new PostReportBranching();
            branchB.TriggerPhoneCall();
            branchB.AnswerPhoneQuestion(true);
            var rebelRejectEnding = branchB.DecideRebelRecruitment(false);

            Assert.AreNotEqual(phoneRejectEnding, rebelRejectEnding);
        }
    }
}
