using System.Collections.Generic;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.Contracts
{
    public sealed class ContractValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public readonly List<ContractValidationErrorCode> Errors = new List<ContractValidationErrorCode>();
    }

    public interface IContractValidator
    {
        ContractValidationResult Validate(ContractInstance contract);
    }

    /// <summary>
    /// 04페이지 "조합 검증" 12개 항목 중, 현재 스텁 데이터 모델로 판정 가능한 항목만 구현한다.
    /// 장소·행동 후보·조사 가능 물체 등 모델링 종속 항목(장소 적합성 세부, NPC 이동 등)은
    /// 최종 장소 목록이 확정되기 전까지 보류하며, 여기서는 통과시키지 않는다.
    /// </summary>
    public sealed class ContractValidator : IContractValidator
    {
        public ContractValidationResult Validate(ContractInstance contract)
        {
            var result = new ContractValidationResult();

            // 2. 실내/실외 조건과 구체적 장소가 일치하는가.
            // 사용자 지시(2026-11-09): 의뢰 사건 공간에서 실외를 제외했으므로 Outdoor는 항상 무효 처리한다.
            if (contract.LocationCategory == LocationCategory.Outdoor)
            {
                result.Errors.Add(ContractValidationErrorCode.LOCATION_INVALID);
            }
            else
            {
                bool locationMatches =
                    contract.LocationCategory == LocationCategory.Indoor && contract.Location == StubLocation.House;
                if (!locationMatches)
                    result.Errors.Add(ContractValidationErrorCode.LOCATION_INVALID);
            }

            // 8. 부수 범죄가 대표 사건과 연결되는가. 04 확정: 인과관계나 인물 관계로 연결된 후보만 쓰고 연결 이유를 기록한다.
            // (대표 유형과 같은 유형을 금지하는 규칙은 원문에 없다 — 부수 범죄 표에 다섯 유형이 모두 있다.)
            foreach (var subCrime in contract.SubCrimes)
            {
                bool linked = contract.SubCrimeLinks.Exists(l =>
                    l.Type == subCrime && !string.IsNullOrWhiteSpace(l.LinkReason) && l.LinkedNpcIds.Count > 0);
                if (!linked)
                {
                    result.Errors.Add(ContractValidationErrorCode.CONTRADICTION);
                    break;
                }
            }

            // 11. 조직 조건과 충돌하지 않는가 (예: 시신이 남지 않는 실종 사건에 시신 회수 조건)
            if (contract.OrgCondition.Type == OrganizationConditionType.RecoverBody &&
                contract.PrimaryCrimeType == CrimeType.Missing)
                result.Errors.Add(ContractValidationErrorCode.CONTRACT_INVALID);

            // 10. 실제 진실/외부 상태/공식 결론/조직 조건이 구조적으로 분리되어 있는가 (아키텍처 불변식)
            if (contract.Truth == null || contract.ExternalState == null ||
                contract.OfficialConclusion == null || contract.OrgCondition == null)
                result.Errors.Add(ContractValidationErrorCode.CONTRACT_INVALID);

            // 7. 시간순서: 최소 1개 이상의 타임라인 기록이 있어야 한다.
            // (세부 순서·인과관계 검증은 장소·행동 후보가 확정된 뒤 보강한다.)
            if (contract.Truth != null && contract.Truth.TimelineEvents.Count == 0)
                result.Errors.Add(ContractValidationErrorCode.TIMELINE_CONFLICT);

            // 도구와 행동(범행 방법)의 호환성 검증 — 전체 호환 조합표는 미정이므로,
            // "도구가 있는데 범행 방법이 없는" 명백히 불가능한 조합만 우선 배제한다.
            if (contract.Truth != null &&
                !string.IsNullOrEmpty(contract.Truth.Tool) &&
                string.IsNullOrEmpty(contract.Truth.Method))
            {
                result.Errors.Add(ContractValidationErrorCode.CONTRADICTION);
            }

            return result;
        }

        /// <summary>
        /// §3-4 "가해자가 해당 장소와 시간에 행동할 수 있었는가?" 중 공간 조건만 검증한다.
        /// 가해자와 피해자가 서로 도달 가능한 방에 있지 않으면 범행이 성립하지 않는다.
        /// npcRoomAssignments에 아직 위치가 배정되지 않은 인물이 있으면 이 검사는 건너뛴다
        /// (배치 자체는 별도 생성 단계의 책임).
        /// </summary>
        public ContractValidationResult ValidateSpatial(
            ContractInstance contract,
            RoomGraph rooms,
            IReadOnlyDictionary<string, string> npcRoomAssignments)
        {
            var result = new ContractValidationResult();

            if (contract?.Truth == null || rooms == null || npcRoomAssignments == null)
                return result;

            if (string.IsNullOrEmpty(contract.Truth.PerpetratorId) || string.IsNullOrEmpty(contract.Truth.VictimId))
                return result;

            bool hasPerpRoom = npcRoomAssignments.TryGetValue(contract.Truth.PerpetratorId, out var perpRoom);
            bool hasVictimRoom = npcRoomAssignments.TryGetValue(contract.Truth.VictimId, out var victimRoom);
            if (!hasPerpRoom || !hasVictimRoom)
                return result;

            if (!rooms.RoomExists(perpRoom) || !rooms.RoomExists(victimRoom))
            {
                result.Errors.Add(ContractValidationErrorCode.LOCATION_INVALID);
                return result;
            }

            if (perpRoom != victimRoom && !rooms.CanReach(perpRoom, victimRoom))
                result.Errors.Add(ContractValidationErrorCode.CONTRADICTION);

            return result;
        }
    }
}
