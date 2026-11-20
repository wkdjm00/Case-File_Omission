using System.Collections.Generic;
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

    /// <summary>추적 진행 중 상태 검사 결과.</summary>
    public enum PursuitCheckResult
    {
        NotActive,
        Continuing,
        /// <summary>의심도 100인 경찰이 있다 — 추적 성공 판정보다 체포가 우선한다.</summary>
        ArrestRequired,
        /// <summary>의심도 70 이상 경찰이 2명 이하가 되어 추적 성공으로 끝났다.</summary>
        CompletedSuccess
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

        /// <summary>지금 추적을 일으킨 사망 이벤트 ID(완료 처리 중복 방지용). 추적 중이 아니면 null.</summary>
        public string ActiveTriggerId { get; private set; }

        public const float HighSuspicionThreshold = 70f;
        public const int TriggerCount = 3;
        /// <summary>2026-11-16 확정: 의심도 70 이상 경찰이 이 수 이하로 줄면 추적 성공.</summary>
        public const int SuccessMaxHighSuspicionCount = 2;
        /// <summary>07 3-3: 추적 완료 후 경찰 의심도를 약 50으로 조정한다.</summary>
        public const float PostPursuitSuspicion = 50f;
        public const float ArrestThreshold = 100f;

        public PolicePursuitController(PoliceSuspicionService suspicionService, GameEventLog eventLog)
        {
            _suspicionService = suspicionService;
            _eventLog = eventLog;
        }

        /// <param name="alivePoliceIds">살아 있는 경찰. 사망한 경찰의 의심도는 세지 않는다. null이면 5명 전원.</param>
        public bool CheckTriggerConditionMet(string playerId, IEnumerable<string> alivePoliceIds = null)
        {
            int highSuspicionCount = (alivePoliceIds ?? PoliceRoster.AllPoliceIds)
                .Count(policeId => _suspicionService.GetSuspicion(policeId, playerId) >= HighSuspicionThreshold);
            return highSuspicionCount >= TriggerCount;
        }

        /// <summary>
        /// 추가 사망 사건 발생 시 호출한다. deathEventId로 중복 발동을 막는다
        /// (같은 사망 사건으로 추적이 두 번 시작되지 않도록 한다).
        /// </summary>
        public bool TryTrigger(string playerId, string deathEventId, IEnumerable<string> alivePoliceIds = null)
        {
            if (Stage != PursuitStage.NotActive)
                return false;

            if (!CheckTriggerConditionMet(playerId, alivePoliceIds))
                return false;

            // 사망 이벤트 자체가 같은 로그에 이미 기록되어 있어도 추적 발동은 따로 판정해야 하므로 별도 키를 쓴다.
            var evt = new GameEvent(TriggerKey(deathEventId), GameEventType.Custom, contractId: null, payload: "pursuit-trigger");
            if (!_eventLog.TryRecord(evt))
                return false; // 이미 같은 사망 사건으로 처리됨

            Stage = PursuitStage.Triggered;
            ActiveTriggerId = deathEventId;
            return true;
        }

        /// <summary>
        /// 추적 중 상태를 검사한다(의심도가 바뀔 때마다 호출).
        /// 1) 살아 있는 경찰 중 한 명이라도 의심도 100이면 체포가 우선한다 — 다른 경찰이 70 미만으로 내려가도 무시하지 않는다.
        /// 2) 2026-11-16 확정: 살아 있는 경찰 중 의심도 70 이상이 2명 이하로 줄면 추적 성공 — 의심도가 50보다 높은 경찰을
        ///    약 50(50)으로 낮추고 추적을 끝내 일반 의뢰 진행으로 돌아간다. 같은 추적의 완료는 이벤트 ID로 한 번만 처리한다.
        /// "관련 경찰"은 의심도가 50보다 높은 경찰로 본다(50 이하인 경찰의 의심도는 올리지 않는다 — 구현 해석).
        /// </summary>
        public PursuitCheckResult Evaluate(string playerId, IEnumerable<string> alivePoliceIds)
        {
            if (Stage == PursuitStage.NotActive) return PursuitCheckResult.NotActive;
            var alive = alivePoliceIds.ToList();
            if (alive.Any(id => _suspicionService.GetSuspicion(id, playerId) >= ArrestThreshold))
                return PursuitCheckResult.ArrestRequired;

            int high = alive.Count(id => _suspicionService.GetSuspicion(id, playerId) >= HighSuspicionThreshold);
            if (high > SuccessMaxHighSuspicionCount) return PursuitCheckResult.Continuing;

            var evt = new GameEvent("pursuit-complete:" + (ActiveTriggerId ?? "unknown"), GameEventType.Custom, contractId: null, payload: "pursuit-success");
            if (_eventLog.TryRecord(evt))
            {
                foreach (var id in alive.Where(id => _suspicionService.GetSuspicion(id, playerId) > PostPursuitSuspicion))
                    _suspicionService.SetSuspicion(id, playerId, PostPursuitSuspicion);
            }
            Stage = PursuitStage.NotActive;
            ActiveTriggerId = null;
            return PursuitCheckResult.CompletedSuccess;
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

        public static string TriggerKey(string deathEventId) => "pursuit-trigger:" + deathEventId;

        /// <summary>
        /// 대응 성공으로 추적이 해소된 뒤, 이후 다른 사망 사건으로 다시 발동할 수 있도록 비활성 상태로 되돌린다
        /// (같은 사망 사건은 이벤트 기록으로 계속 막힌다).
        /// </summary>
        public void ClearAfterSuccess()
        {
            if (Stage != PursuitStage.ResolvedSuccess)
                throw new System.InvalidOperationException("대응 성공 상태에서만 추적을 해소할 수 있습니다.");
            Stage = PursuitStage.NotActive;
        }

        /// <summary>저장/불러오기 전용. 전이 규칙을 다시 실행하지 않고 단계만 되살린다.</summary>
        public void RestoreStage(PursuitStage stage, string activeTriggerId = null)
        {
            Stage = stage;
            ActiveTriggerId = stage == PursuitStage.NotActive ? null : activeTriggerId;
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
