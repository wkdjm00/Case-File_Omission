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
        /// 거짓말 시도 결과를 의심도에 반영한다. 성공(발각되지 않음)은 수용과 같은 처리 절차를,
        /// 발각되면 대응 실패와 같은 처리 절차를 재사용한다. 06페이지가 이 둘을 하나의 규칙으로
        /// 명시 통합하지는 않았지만, 같은 수치 체계(연속 수용/대응 실패 표)를 쓰는 것이 가장
        /// 일관적인 해석이라 판단해 이렇게 연결했다 — 공식 결론 반영 세부 판정은 여전히 미정이다.
        /// </summary>
        public float ApplyLieAttemptResult(
            string observerId,
            string suspectId,
            LieAttemptResult lieResult,
            int consecutiveAcceptCount,
            int consecutiveFailCount,
            bool verifiedAccept)
        {
            return lieResult.Success
                ? ApplyAccepted(observerId, suspectId, consecutiveAcceptCount, verifiedAccept)
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
