using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;

namespace CaseFileOmission.Endings
{
    /// <summary>
    /// 상태만으로 자동 판정 가능한 엔딩만 평가한다: 체포, 경찰 전원 제거, 조직 신뢰도 0.
    /// 반란군 영입 수락/거절, 이민, 수화기 질문 거절은 플레이어의 대화 선택에서 직접 발생하는
    /// 분기이며 상태로부터 유도할 수 없으므로 이 평가기의 범위에 포함하지 않는다 —
    /// 해당 엔딩은 대화/연출 시스템이 <see cref="GameEndingType"/> 값을 직접 지정해 종료 처리한다.
    ///
    /// 우선순위(07·10 최신 확정): 생존한 경찰 중 누구라도 플레이어 의심도 100에 도달하면 체포한다.
    /// 현재 의뢰의 담당 경찰인지 여부와 관계없이 판정하며, 추적 사건 발동 여부와 무관하게 먼저 검사한다.
    /// 경찰 전원 제거와 신뢰도 0 사이의 순서는 원문에 없어 그 다음 순서(임시)로 둔다.
    /// </summary>
    public sealed class GameEndEvaluator
    {
        private readonly PoliceSuspicionService _policeSuspicionService;
        private readonly OrganizationState _organizationState;

        public GameEndEvaluator(PoliceSuspicionService policeSuspicionService, OrganizationState organizationState)
        {
            _policeSuspicionService = policeSuspicionService;
            _organizationState = organizationState;
        }

        /// <param name="handlerPoliceId">현재 의뢰의 담당 경찰. 의뢰 사이면 null.</param>
        public GameEndingType EvaluateAutomaticEndings(
            string playerId,
            IReadOnlyCollection<string> deceasedPoliceIds,
            string handlerPoliceId = null)
        {
            var deceased = deceasedPoliceIds ?? new string[0];
            bool Alive(string id) => !deceased.Contains(id);

            // 체포 기준은 현재 담당 경찰 한 명으로 제한하지 않는다. 경찰 A~E 중 생존자가 100에 도달하면 즉시 체포다.
            bool arrested = PoliceRoster.AllPoliceIds.Any(id =>
                Alive(id) && _policeSuspicionService.GetSuspicion(id, playerId) >= PoliceSuspicionService.ArrestThreshold);
            if (arrested)
                return GameEndingType.Arrest;

            if (PoliceRoster.AllPoliceIds.All(id => deceased.Contains(id)))
                return GameEndingType.PoliceAllEliminated;

            if (_organizationState.HasReachedZero)
                return GameEndingType.OrganizationTrustZero;

            return GameEndingType.None;
        }

        /// <summary>
        /// §8단계 확정: 이제 경찰 생존 상태를 PoliceLifeStateService가 지속적으로 저장하므로,
        /// 호출자가 죽은 목록을 매번 수동으로 모아 넘기지 않고 이 서비스를 직접 연결할 수 있다.
        /// </summary>
        public GameEndingType EvaluateAutomaticEndings(string playerId, PoliceLifeStateService policeLifeState, string handlerPoliceId = null) =>
            EvaluateAutomaticEndings(playerId, policeLifeState?.GetDeceasedIds(), handlerPoliceId);
    }
}
