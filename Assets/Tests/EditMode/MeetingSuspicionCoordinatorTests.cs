using System;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;
using CaseFileOmission.Police;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MeetingSuspicionCoordinatorTests
    {
        [Test]
        public void ApplyTurnSkip_UpdatesActualPoliceSuspicionState()
        {
            var suspicionService = new PoliceSuspicionService();
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);

            float result = coordinator.ApplyTurnSkip(PoliceRoster.PoliceA, "player", 1);

            Assert.AreEqual(5f, result);
            Assert.AreEqual(5f, suspicionService.GetSuspicion(PoliceRoster.PoliceA, "player"));
        }

        [Test]
        public void ApplyAccepted_WithVerifiedBonus_AddsExtraDecrease()
        {
            var suspicionService = new PoliceSuspicionService();
            suspicionService.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 50f });
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);

            // 1회 연속 수용(-4) + 검증 보너스(-8) = -12 -> 50-12=38
            float result = coordinator.ApplyAccepted(PoliceRoster.PoliceA, "player", 1, verifiedAccept: true);

            Assert.AreEqual(38f, result);
        }

        [Test]
        public void ApplyLieAttemptResult_Success_IsNotTreatedAsAcceptance()
        {
            // 06 확정: 거짓말 성공은 사실로 믿었다는 뜻이 아니다 — 수용 판단이 따로 내려지기 전에는 의심도가 줄지 않는다.
            var suspicionService = new PoliceSuspicionService();
            suspicionService.ApplyBatch(PoliceRoster.PoliceA, "player", new[] { 50f });
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);

            var lieResult = new LieAttemptResult { Success = true, ProbabilityUsed = 80f };
            float result = coordinator.ApplyLieAttemptResult(PoliceRoster.PoliceA, "player", lieResult, 0);

            Assert.AreEqual(50f, result);
        }

        [Test]
        public void ApplyLieAttemptResult_Detected_BehavesLikeResponseFail()
        {
            var suspicionService = new PoliceSuspicionService();
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);

            var lieResult = new LieAttemptResult { Success = false, ProbabilityUsed = 20f };
            float result = coordinator.ApplyLieAttemptResult(PoliceRoster.PoliceA, "player", lieResult, 1);

            Assert.AreEqual(8f, result);
        }

        [Test]
        public void RequestAndRecordClueConfirmation_WhenRequested_MarksClueConfirmed()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));

            var suspicionService = new PoliceSuspicionService();
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);

            // 의심도 100, 수사중심형(A) + 단서 10개 발견 -> 확률 상한 50%에 도달하도록 설계.
            // 확정적으로 요청이 일어나게 하려면 여러 번 시도해 최소 한 번은 발생하는지로 검증한다.
            var requestService = new ClueConfirmationRequestService(new Random(1));
            bool everConfirmed = false;
            for (int i = 0; i < 100 && !everConfirmed; i++)
            {
                coordinator.RequestAndRecordClueConfirmation(
                    requestService, repo, PoliceRoster.PoliceA, "clue-1",
                    sameRoomAsPlayer: true, currentSuspicion: 100f, cluesFoundByPoliceInThisRoom: 10, acceptedLiesCount: 0);

                everConfirmed = repo.GetRecord("clue-1").ConfirmedByPoliceIds.Contains(PoliceRoster.PoliceA);
            }

            Assert.IsTrue(everConfirmed, "최대 확률(50%)로 100회 시도했는데 한 번도 확인되지 않는 것은 비정상적으로 낮은 확률이다.");
        }

        [Test]
        public void RequestAndRecordClueConfirmation_NotSameRoom_NeverMarksConfirmed()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));

            var suspicionService = new PoliceSuspicionService();
            var coordinator = new MeetingSuspicionCoordinator(suspicionService);
            var requestService = new ClueConfirmationRequestService(new Random(1));

            coordinator.RequestAndRecordClueConfirmation(
                requestService, repo, PoliceRoster.PoliceA, "clue-1",
                sameRoomAsPlayer: false, currentSuspicion: 100f, cluesFoundByPoliceInThisRoom: 10, acceptedLiesCount: 10);

            Assert.IsFalse(repo.GetRecord("clue-1").ConfirmedByPoliceIds.Contains(PoliceRoster.PoliceA));
        }
    }
}
