using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;

namespace CaseFileOmission.Police
{
    /// <summary>
    /// 06페이지 20·21절 확정 수치표(<see cref="MeetingSuspicionTable"/>)와 거짓말 판정
    /// (<see cref="LieDetectionService"/>)은 순수 계산기다. 이 클래스는 그 계산 결과를
    /// 실제 <see cref="PoliceSuspicionService"/> 상태와 <see cref="ClueRepository"/> 상태에
    /// 반영하는 연결 계층이다 — 계산기 자체를 다시 구현하지 않고 조합만 한다.
    /// </summary>
    public sealed class MeetingSuspicionCoordinator
    {
        private readonly PoliceSuspicionService _suspicionService;

        public MeetingSuspicionCoordinator(PoliceSuspicionService suspicionService)
        {
            _suspicionService = suspicionService;
        }

        /// <summary>참가자가 차례를 연속으로 넘겼을 때 적용한다.</summary>
        public float ApplyTurnSkip(string observerId, string suspectId, int consecutiveSkipCount)
        {
            float delta = MeetingSuspicionTable.ConsecutiveTurnSkipIncrease(consecutiveSkipCount);
            return _suspicionService.ApplyBatch(observerId, suspectId, new[] { delta });
        }

        /// <summary>
        /// 카드(또는 거짓말) 제시가 최종적으로 수용되었을 때 적용한다. verifiedAccept는 의문 제기 후
        /// 검증을 거쳐 수용된 경우에만 true로 넘긴다(추가 보너스 -8이 적용된다).
        /// </summary>
        public float ApplyAccepted(string observerId, string suspectId, int consecutiveAcceptCount, bool verifiedAccept)
        {
            float delta = MeetingSuspicionTable.ConsecutiveAcceptDecrease(consecutiveAcceptCount);
            if (verifiedAccept)
                delta += MeetingSuspicionTable.VerifiedAcceptBonus;
            return _suspicionService.ApplyBatch(observerId, suspectId, new[] { delta });
        }

        public float ApplyResponseFailed(string observerId, string suspectId, int consecutiveFailCount)
        {
            float delta = MeetingSuspicionTable.ConsecutiveResponseFailIncrease(consecutiveFailCount);
            return _suspicionService.ApplyBatch(observerId, suspectId, new[] { delta });
        }

        public float ApplyGiveUp(string observerId, string suspectId, int consecutiveGiveUpCount)
        {
            float delta = MeetingSuspicionTable.ConsecutiveGiveUpIncrease(consecutiveGiveUpCount);
            return _suspicionService.ApplyBatch(observerId, suspectId, new[] { delta });
        }

        /// <summary>
        /// 거짓말 시도 결과를 의심도에 반영한다. 06 2026-11-09 확정: "거짓말 성공은 상대가 사실로 믿었다는 뜻이 아니다."
        /// 그래서 발각되지 않은 것만으로는 수용(의심도 감소)으로 처리하지 않는다 — 수용 여부는 판정자의 카드 판단(수용/의문)이
        /// 따로 정하고, 수용되면 그때 <see cref="ApplyAccepted"/>를 적용한다. 발각되면 대응 실패 표를 적용한다
        /// (발각 시 의심도 연결 규칙은 06에서 미정이라 같은 수치 체계를 쓰는 임시 해석).
        /// </summary>
        public float ApplyLieAttemptResult(
            string observerId,
            string suspectId,
            LieAttemptResult lieResult,
            int consecutiveFailCount)
        {
            return lieResult.Success
                ? _suspicionService.GetSuspicion(observerId, suspectId)
                : ApplyResponseFailed(observerId, suspectId, consecutiveFailCount);
        }

        /// <summary>
        /// 담당 경찰의 단서 확인 요청을 굴리고, 실제로 요청이 발생하면 해당 단서에
        /// 확인 기록을 남긴다(경찰이 내용을 기억하게 됨 — 소유권은 플레이어에게 유지).
        /// </summary>
        public ConfirmationRequestResult RequestAndRecordClueConfirmation(
            ClueConfirmationRequestService requestService,
            ClueRepository clueRepository,
            string policeId,
            string clueId,
            bool sameRoomAsPlayer,
            float currentSuspicion,
            int cluesFoundByPoliceInThisRoom,
            int acceptedLiesCount)
        {
            var result = requestService.Roll(
                policeId, sameRoomAsPlayer, currentSuspicion, cluesFoundByPoliceInThisRoom, acceptedLiesCount);

            if (result.Requested && clueRepository.Exists(clueId))
                clueRepository.GetRecord(clueId).MarkConfirmedByPolice(policeId);

            return result;
        }
    }
}
