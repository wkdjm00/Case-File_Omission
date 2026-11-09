using System.Linq;
using CaseFileOmission.Core;

namespace CaseFileOmission.Police
{
    public enum PursuitStage
    {
        NotActive,
        Triggered,
        GatheringInformation,
        PlayerResponse,
        ResolvedSuccess,
        ResolvedFailure
    }

    /// <summary>
    /// 07페이지 2026-11-09 확정: 발동 조건은 경찰 5명 중 3명 이상이 각각 의심도 70% 이상인 상태에서
    /// 추가 사망 사건이 발생하는 것. 진행 단계는 발동→정보수집→플레이어대응→결과판정이며,
    /// 별도 미니게임 없이 기존 카드·진술·보고서 시스템을 사용한다(그 시스템 자체는 이 클래스 밖에서 호출).
    /// 단계 내부의 구체 콘텐츠(정보수집 화면, 대응 연출)는 이 단계에서 정의하지 않는다.
    /// </summary>
    public sealed class PolicePursuitController
    {
        private readonly PoliceSuspicionService _suspicionService;
        private readonly GameEventLog _eventLog;

        public PursuitStage Stage { get; private set; } = PursuitStage.NotActive;

        public PolicePursuitController(PoliceSuspicionService suspicionService, GameEventLog eventLog)
        {
            _suspicionService = suspicionService;
            _eventLog = eventLog;
        }

        public bool CheckTriggerConditionMet(string playerId)
        {
            int highSuspicionCount = PoliceRoster.AllPoliceIds
                .Count(policeId => _suspicionService.GetSuspicion(policeId, playerId) >= 70f);
            return highSuspicionCount >= 3;
        }

        /// <summary>
        /// 추가 사망 사건 발생 시 호출한다. deathEventId로 중복 발동을 막는다
        /// (같은 사망 사건으로 추적이 두 번 시작되지 않도록 한다).
        /// </summary>
        public bool TryTrigger(string playerId, string deathEventId)
        {
            if (Stage != PursuitStage.NotActive)
                return false;

            if (!CheckTriggerConditionMet(playerId))
                return false;

            var evt = new GameEvent(deathEventId, GameEventType.Custom, contractId: null, payload: "pursuit-trigger");
            if (!_eventLog.TryRecord(evt))
                return false; // 이미 같은 사망 사건으로 처리됨

            Stage = PursuitStage.Triggered;
            return true;
        }

        public void AdvanceToGatheringInformation()
        {
            if (Stage != PursuitStage.Triggered)
                throw new System.InvalidOperationException("발동 상태에서만 정보 수집 단계로 넘어갈 수 있습니다.");
            Stage = PursuitStage.GatheringInformation;
        }

        public void AdvanceToPlayerResponse()
        {
            if (Stage != PursuitStage.GatheringInformation)
                throw new System.InvalidOperationException("정보 수집 단계에서만 플레이어 대응 단계로 넘어갈 수 있습니다.");
            Stage = PursuitStage.PlayerResponse;
        }

        /// <summary>대응 성공 시 추적을 해소하거나 일시 중단한다(확정). 실패 시 추적은 계속된다.</summary>
        public void ResolveResponse(bool success)
        {
            if (Stage != PursuitStage.PlayerResponse)
                throw new System.InvalidOperationException("플레이어 대응 단계에서만 결과를 판정할 수 있습니다.");

            Stage = success ? PursuitStage.ResolvedSuccess : PursuitStage.ResolvedFailure;
        }

        /// <summary>추적 실패 후에도 즉시 게임 오버가 아니므로, 다시 대응을 이어갈 수 있도록 재개한다.</summary>
        public void ResumeAfterFailedResponse()
        {
            if (Stage != PursuitStage.ResolvedFailure)
                throw new System.InvalidOperationException("실패 상태에서만 재개할 수 있습니다.");
            Stage = PursuitStage.PlayerResponse;
        }
    }
}
