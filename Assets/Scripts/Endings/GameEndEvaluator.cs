using System.Collections.Generic;
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
    /// 우선순위(07페이지 확정): 배정된 경찰의 의심도가 100에 도달해 체포 조건이 충족되면
    /// 추적 사건 발동 여부와 무관하게 체포를 우선한다. 따라서 체포를 가장 먼저 검사한다.
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

        public GameEndingType EvaluateAutomaticEndings(string playerId, IReadOnlyCollection<string> deceasedPoliceIds)
        {
            if (_policeSuspicionService.AnyPoliceReachedArrestThreshold(playerId))
                return GameEndingType.Arrest;

            if (deceasedPoliceIds != null && deceasedPoliceIds.Count >= PoliceRoster.AllPoliceIds.Count)
                return GameEndingType.PoliceAllEliminated;

            if (_organizationState.HasReachedZero)
                return GameEndingType.OrganizationTrustZero;

            return GameEndingType.None;
        }

        /// <summary>
        /// §8단계 확정: 이제 경찰 생존 상태를 PoliceLifeStateService가 지속적으로 저장하므로,
        /// 호출자가 죽은 목록을 매번 수동으로 모아 넘기지 않고 이 서비스를 직접 연결할 수 있다.
        /// </summary>
        public GameEndingType EvaluateAutomaticEndings(string playerId, PoliceLifeStateService policeLifeState) =>
            EvaluateAutomaticEndings(playerId, policeLifeState?.GetDeceasedIds());
    }
}
